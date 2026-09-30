using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using NFC_System;
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;

namespace NFC_System.WinUiSmoke;

internal static class Program
{
    internal static string ResultPath = Path.Combine(AppContext.BaseDirectory, "winui-smoke-result.txt");

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length > 0) ResultPath = Path.GetFullPath(args[0]);
        try
        {
            File.WriteAllText(ResultPath, "RUNNING: WinUI startup");
            WinRT.ComWrappersSupport.InitializeComWrappers();
            Application.Start(parameters =>
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
                _ = new SmokeApplication();
            });
            return File.Exists(ResultPath) && File.ReadAllText(ResultPath).StartsWith("PASS", StringComparison.Ordinal) ? 0 : 1;
        }
        catch (Exception ex)
        {
            File.WriteAllText(ResultPath, $"FAIL startup: {ex}");
            return 1;
        }
    }
}

public sealed partial class SmokeApplication : Application
{
    private Window? _window;
    private bool _started;

    public SmokeApplication()
    {
        InitializeComponent();
        UnhandledException += (_, args) =>
        {
            File.WriteAllText(Program.ResultPath, $"FAIL UI: {args.Exception}");
            args.Handled = true;
            Exit();
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new Window { Title = "Signed QR WinUI smoke test" };
        var root = new Grid();
        _window.Content = root;
        root.Loaded += async (_, _) =>
        {
            if (_started) return;
            _started = true;
            _window.AppWindow.Hide();
            await RunAsync(root);
        };
        _window.Activate();
    }

    private async Task RunAsync(Grid root)
    {
        try
        {
            await CheckBrandingAsync(root);
            await CheckGraduationDialogsAsync(root);
            foreach (var pair in new[] { ("00-00010", "00-00010"), (" 2026-001 ", "2026-001"), ("A/B:C", "A_B_C"), ("...", "Student-QR"), ("CON", "Student-CON") })
                if (QrCredentialDisplay.GetExportFileName(pair.Item1) != pair.Item2)
                    throw new Exception($"Unexpected QR export filename for {pair.Item1}.");
            var editField = new TextBox { Text = "Unsaved student edit" };
            var editor = new ContentDialog { XamlRoot = root.XamlRoot, Title = "Student editor test", Content = editField, CloseButtonText = "Close" };
            var opened = new TaskCompletionSource<bool>();
            bool closed = false;
            editor.Opened += (_, _) => opened.TrySetResult(true);
            editor.Closed += (_, _) => closed = true;
            var editorOperation = editor.ShowAsync();
            await opened.Task;
            await QrCredentialDisplay.HideAsync(editor);
            await editorOperation;
            if (!closed) throw new Exception("Editor close was not awaited.");

            await CheckQrPreviewAsync(root, credentialReplaced: false);
            await CheckQrPreviewAsync(root, credentialReplaced: true);
            await CheckQrPreviewAsync(root, credentialReplaced: false);

            opened = new TaskCompletionSource<bool>();
            editorOperation = editor.ShowAsync();
            await opened.Task;
            if (editField.Text != "Unsaved student edit") throw new Exception("Reopening editor lost unsaved fields.");
            await QrCredentialDisplay.HideAsync(editor);
            await editorOperation;

            var picker = new Windows.Storage.Pickers.FileSavePicker { SuggestedFileName = QrCredentialDisplay.GetExportFileName("00-00010") };
            if (picker.SuggestedFileName != "00-00010") throw new Exception("Student ID was not used as the picker filename.");
            picker.FileTypeChoices.Add("PNG", new[] { ".png" });
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(_window!));
            File.WriteAllText(Program.ResultPath, "PASS: WinUI startup; clearance directory/editor/confirmation dialogs rendered without body overflow; held-only graduation confirmation disabled; branding image load and layout at 320/640 pixels; ICO assignment; XamlRoot; awaited dialog closure; QR bitmap preview; replacement success notice; ordinary preview without stale success notice; editor reopening; unsaved fields; student-ID export filenames; desktop file-picker owner initialization. Interactive export selection was not exercised.");
        }
        catch (Exception ex) { File.WriteAllText(Program.ResultPath, $"FAIL: {ex}"); }
        finally { _window?.Close(); Exit(); }
    }

    private async Task CheckGraduationDialogsAsync(Grid root)
    {
        _window!.AppWindow.Resize(new Windows.Graphics.SizeInt32(1000, 800));
        await Task.Delay(200);
        var student = new GraduationStudent { StudentId = "00-001", FullName = "Student Name", Course = "BSIT", EnrollmentStatus = "Active", Clearance = GraduationClearanceStatus.OnHold, Reason = "Pending requirement" };
        var preview = GraduationRules.Preview(new[] { student }, "Graduated", 1, DateTime.UtcNow);
        foreach (string title in new[] { "Graduation Clearance", "Review Clearance", "Confirm Student Status" })
        {
            var observed = new TaskCompletionSource<bool>();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            int ticks = 0;
            timer.Tick += async (_, _) =>
            {
                if (++ticks > 70) { timer.Stop(); observed.TrySetException(new Exception("Clearance dialog did not open.")); return; }
                var dialog = VisualTreeHelper.GetOpenPopupsForXamlRoot(root.XamlRoot).Select(p => p.Child).OfType<ContentDialog>().FirstOrDefault();
                if (dialog == null) return;
                timer.Stop();
                try
                {
                    if (dialog.Title?.ToString() != title || dialog.Content is not FrameworkElement content)
                        throw new Exception("Unexpected clearance dialog content.");
                    content.UpdateLayout();
                    if (content.ActualWidth <= 0 || content.ActualWidth > root.XamlRoot.Size.Width || content.ActualHeight > root.XamlRoot.Size.Height)
                        throw new Exception("Clearance dialog body overflows its window.");
                    if (title == "Confirm Student Status" && dialog.IsPrimaryButtonEnabled)
                        throw new Exception("Held student incorrectly enabled graduation confirmation.");
                    var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(content);
                    byte[] pixels = (await bitmap.GetPixelsAsync()).ToArray();
                    if (pixels.Length == 0) throw new Exception("Clearance dialog rendered empty.");
                    using var output = File.Create(Path.Combine(AppContext.BaseDirectory, title.Replace(' ', '-') + ".png"));
                    var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, output.AsRandomAccessStream());
                    encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
                        (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels);
                    await encoder.FlushAsync();
                    observed.TrySetResult(true);
                }
                catch (Exception ex) { observed.TrySetException(ex); }
                finally { dialog.Hide(); }
            };
            timer.Start();
            Task task = title switch
            {
                "Graduation Clearance" => GraduationDialogs.SelectStudentAsync(root.XamlRoot, new[] { student }),
                "Review Clearance" => GraduationDialogs.EditClearanceAsync(root.XamlRoot, student),
                _ => GraduationDialogs.ConfirmStatusAsync(root.XamlRoot, preview)
            };
            try { await observed.Task; await task; }
            finally { timer.Stop(); }
        }
    }

    private async Task CheckQrPreviewAsync(Grid root, bool credentialReplaced)
    {
        var previewObserved = new TaskCompletionSource<bool>();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        int ticks = 0;
        timer.Tick += (_, _) =>
        {
            if (++ticks > 100)
            {
                timer.Stop();
                previewObserved.TrySetException(new Exception("QR dialog did not appear."));
                return;
            }
            var preview = VisualTreeHelper.GetOpenPopupsForXamlRoot(root.XamlRoot).Select(p => p.Child).OfType<ContentDialog>().FirstOrDefault();
            if (preview == null) return;
            timer.Stop();
            try
            {
                Image? image;
                if (credentialReplaced)
                {
                    if (preview.Content is not StackPanel panel || panel.Children.Count != 2 ||
                        panel.Children[0] is not InfoBar { IsOpen: true, IsClosable: false, Severity: InfoBarSeverity.Success } notice ||
                        notice.Title != "QR replaced successfully" || notice.Message != "Saved for student 2026-001.")
                        throw new Exception("Saved QR replacement did not show the expected success notice.");
                    image = panel.Children[1] as Image;
                }
                else
                {
                    image = preview.Content as Image;
                    if (image == null) throw new Exception("Ordinary QR preview unexpectedly included a success notice.");
                }
                if (image?.Source is not WriteableBitmap { PixelWidth: 800, PixelHeight: 800 } ||
                    preview.PrimaryButtonText != "Export PNG" || preview.Title?.ToString() != "Student QR: 2026-001")
                    throw new Exception("QR preview or export action was not preserved.");
                previewObserved.TrySetResult(true);
            }
            catch (Exception ex) { previewObserved.TrySetException(ex); }
            finally { preview.Hide(); }
        };
        timer.Start();
        try
        {
            var previewTask = QrCredentialDisplay.ShowAsync(_window!, "2026-001", "NFC1.WinUI-render-smoke-test", credentialReplaced);
            await previewObserved.Task;
            await previewTask;
        }
        finally { timer.Stop(); }
    }

    private async Task CheckBrandingAsync(Grid root)
    {
        _window!.AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Branding", "AppIcon.ico"));
        root.Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 24, 24, 28));
        var logo = new BrandLogo { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Height = 64 };
        var image = (Image)logo.Children[0];
        var bitmap = (BitmapImage)image.Source;
        var loaded = new TaskCompletionSource<bool>();
        bitmap.ImageOpened += (_, _) => loaded.TrySetResult(true);
        bitmap.ImageFailed += (_, args) => loaded.TrySetException(new Exception(args.ErrorMessage));
        root.Children.Add(logo);
        if (bitmap.PixelWidth == 0) await loaded.Task.WaitAsync(TimeSpan.FromSeconds(10));
        if (bitmap.PixelWidth != 512) throw new Exception("Brand icon was not loaded from packaged assets.");
        foreach (int width in new[] { 320, 640 })
        {
            root.Width = width; root.Height = 180;
            _window.AppWindow.Resize(new Windows.Graphics.SizeInt32(width + 32, 240));
            root.UpdateLayout();
            await Task.Delay(150);
            if (logo.ActualWidth > width || ((TextBlock)logo.Children[1]).IsTextTrimmed)
                throw new Exception($"Brand logo overflow at width {width}.");
            var rendered = new RenderTargetBitmap();
            await rendered.RenderAsync(root);
            byte[] pixels = (await rendered.GetPixelsAsync()).ToArray();
            if (pixels.Length == 0) throw new Exception("Brand render was empty.");
            using var output = File.Create(Path.Combine(AppContext.BaseDirectory, $"branding-{width}.png"));
            var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, output.AsRandomAccessStream());
            encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
                (uint)rendered.PixelWidth, (uint)rendered.PixelHeight, 96, 96, pixels);
            await encoder.FlushAsync();
        }
        root.Children.Clear(); root.Width = double.NaN; root.Height = double.NaN;
    }
}
