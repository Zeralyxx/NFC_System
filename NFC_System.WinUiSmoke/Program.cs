using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using NFC_System;
using System;
using System.IO;
using System.Linq;
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
                bool validImage = preview.Content is Image { Source: WriteableBitmap bitmap } && bitmap.PixelWidth == 800 && bitmap.PixelHeight == 800;
                preview.Hide();
                previewObserved.TrySetResult(validImage);
            };
            timer.Start();
            var previewTask = QrCredentialDisplay.ShowAsync(_window!, "2026-001", "NFC1.WinUI-render-smoke-test");
            if (!await previewObserved.Task) throw new Exception("QR bitmap was not rendered.");
            await previewTask;

            opened = new TaskCompletionSource<bool>();
            editorOperation = editor.ShowAsync();
            await opened.Task;
            if (editField.Text != "Unsaved student edit") throw new Exception("Reopening editor lost unsaved fields.");
            await QrCredentialDisplay.HideAsync(editor);
            await editorOperation;

            var picker = new Windows.Storage.Pickers.FileSavePicker();
            picker.FileTypeChoices.Add("PNG", new[] { ".png" });
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(_window!));
            File.WriteAllText(Program.ResultPath, "PASS: WinUI startup, XamlRoot, awaited dialog closure, QR bitmap preview, editor reopening, unsaved fields, desktop file-picker owner initialization. Interactive export selection was not exercised.");
        }
        catch (Exception ex) { File.WriteAllText(Program.ResultPath, $"FAIL: {ex}"); }
        finally { _window?.Close(); Exit(); }
    }
}
