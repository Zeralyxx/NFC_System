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
using System.Xml.Linq;

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
            await CheckSettingsLayoutAsync(root);
            await CheckConnectionSetupLayoutAsync(root);
            await CheckConnectionControlAsync(root);
            await CheckDirectoryFiltersAsync(root);
            await CheckReportNotesAsync(root);
            await CheckCompletionDialogsAsync(root);
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
            File.WriteAllText(Program.ResultPath, "PASS: WinUI startup; directory refinement controls render at 320/640 pixels, retain selections, synchronize sorting, reset and disable uncached fields offline; dashboard settings XAML/device controls rendered at 640x480 and 1000x800 without body overflow (save handler not invoked); clearance directory/editor/confirmation dialogs rendered without body overflow; held-only graduation confirmation disabled; branding image load and layout at 320/640 pixels; ICO assignment; XamlRoot; awaited dialog closure; QR bitmap preview; replacement success notice; ordinary preview without stale success notice; editor reopening; unsaved fields; student-ID export filenames; desktop file-picker owner initialization. Interactive export selection was not exercised.");
        }
        catch (Exception ex) { File.WriteAllText(Program.ResultPath, $"FAIL: {ex}"); }
        finally { _window?.Close(); Exit(); }
    }

    private async Task CheckReportNotesAsync(Grid root)
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var source = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "EventReportsWindow.xaml.txt"));
        foreach (int width in new[] { 320, 640 })
        {
            var panel = new StackPanel { Width = width, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            root.Children.Add(panel);
            try
            {
                foreach (string name in new[] { "CampusLedgerNote", "EventReportNote", "RetentionSubText" })
                {
                    var markup = new XElement(source.Descendants().Single(e => (string?)e.Attribute(x + "Name") == name));
                    markup.SetAttributeValue(XNamespace.Xmlns + "x", x.NamespaceName);
                    markup.SetAttributeValue("Foreground", "Gray");
                    var note = (TextBlock)Microsoft.UI.Xaml.Markup.XamlReader.Load(markup.ToString());
                    note.Text = name == "RetentionSubText"
                        ? "12345 of 20000 attendees last recorded as PRESENT. Completion is not established by scan logs."
                        : "Daily totals cover all retained gate logs. Explorer/export: 10,000 rows for 2026-10-01 (limit 10,000 reached; may be incomplete).";
                    panel.Children.Add(note);
                }
                await Task.Delay(100);
                panel.UpdateLayout();
                foreach (TextBlock note in panel.Children)
                    if (note.TextWrapping != TextWrapping.Wrap || note.ActualWidth > width + 1 || note.ActualHeight <= 0 ||
                        (width == 320 && note.ActualHeight < 24) || note.IsTextTrimmed)
                        throw new Exception($"Reporting scope note layout failed: {note.Name}, width={note.ActualWidth}, height={note.ActualHeight}, panel={width}, trimmed={note.IsTextTrimmed}.");
            }
            finally { root.Children.Remove(panel); }
        }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "reporting-smoke-result.txt"), "PASS: production report scope/state TextBlocks wrap without truncation at 320 and 640 pixels. Full report navigation and database handlers are not exercised by this UI fixture.");
    }

    private async Task CheckDirectoryFiltersAsync(Grid root)
    {
        foreach (int width in new[] { 320, 640 })
        {
            var bar = new DirectoryRefinementBar { Width = width, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                RequestedTheme = ElementTheme.Dark, Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 34, 35, 42)) };
            root.Children.Add(bar);
            try
            {
                bar.SetOptions(new[] { "1", "2", "10" }, new[] { "A", "B" }, true, true);
                var year = (ComboBox)bar.FindName("YearBox");
                var section = (ComboBox)bar.FindName("SectionBox");
                int changes = 0;
                bar.Changed += (_, _) => changes++;
                year.SelectedItem = "2";
                section.SelectedItem = "B";
                if (changes != 2 || bar.Year != "2" || bar.Section != "B") throw new Exception("Directory filter events failed.");
                bar.SetOptions(new[] { "1", "2", "10" }, new[] { "A", "B" }, true, true);
                if (bar.Year != "2" || bar.Section != "B" || changes != 2) throw new Exception("Reload lost directory filters.");
                bar.SetSort("Year", false);
                if (bar.SortColumn != "Year" || bar.Ascending || changes != 2) throw new Exception("Header sort synchronization recursed.");
                ((ComboBox)bar.FindName("OrderBox")).SelectedIndex = 0;
                if (!bar.Ascending || changes != 3) throw new Exception("Order selection did not notify directory.");
                bar.Reset(true);
                if (!bar.LatestOnly || bar.Year != "All Years" || bar.Section != "All Sections" || bar.SortColumn != "FullName" || !bar.Ascending)
                    throw new Exception("Latest import reset failed.");
                bar.ShowCount(12, 100, true);
                await Task.Delay(100);
                bar.UpdateLayout();
                if (bar.ActualWidth != width || bar.ActualHeight <= 0) throw new Exception("Directory toolbar dimensions invalid.");
                foreach (string name in new[] { "SortBox", "OrderBox", "LatestBox", "ResultText" })
                {
                    var element = (FrameworkElement)bar.FindName(name);
                    var bounds = element.TransformToVisual(bar).TransformBounds(new Windows.Foundation.Rect(0, 0, element.ActualWidth, element.ActualHeight));
                    if (bounds.X < -1 || bounds.Right > width + 1) throw new Exception($"Directory {name} overflows at {width}px.");
                }
                var bitmap = new RenderTargetBitmap();
                await bitmap.RenderAsync(bar);
                byte[] pixels = (await bitmap.GetPixelsAsync()).ToArray();
                if (pixels.Length == 0 || !pixels.Any(b => b != 0)) throw new Exception("Directory filters rendered blank.");
                using var output = File.Create(Path.Combine(AppContext.BaseDirectory, $"Directory-Filters-{width}.png"));
                var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, output.AsRandomAccessStream());
                encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
                    (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels);
                await encoder.FlushAsync();
                bar.Reset();
                if (bar.LatestOnly) throw new Exception("Clear left latest import selected.");
                bar.SetOptions(Array.Empty<string>(), Array.Empty<string>(), false, false);
                if (year.IsEnabled || section.IsEnabled || ((CheckBox)bar.FindName("LatestBox")).IsEnabled)
                    throw new Exception("Unavailable directory fields remain enabled.");
            }
            finally { root.Children.Remove(bar); }
        }
    }

    private async Task CheckSettingsLayoutAsync(Grid root)
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var source = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "MainWindow.xaml.txt"));
        var markup = new XElement(source.Descendants().Single(e => (string?)e.Attribute(x + "Name") == "SettingsDialog"));
        markup.SetAttributeValue(XNamespace.Xmlns + "x", x.NamespaceName);
        markup.SetAttributeValue(XNamespace.Xmlns + "local", "using:NFC_System");
        // Render the real dialog without creating MainWindow or contacting the production database.
        foreach (var attribute in markup.DescendantsAndSelf().Attributes().Where(a =>
            a.Name.LocalName is "Click" or "PrimaryButtonClick" or "Closing").ToArray()) attribute.Remove();
        foreach (var attribute in markup.DescendantsAndSelf().Attributes().Where(a => a.Value == "{StaticResource TextSecondaryBrush}"))
            attribute.Value = "Gray";

        foreach (var size in new[] { new Windows.Graphics.SizeInt32(640, 480), new Windows.Graphics.SizeInt32(1000, 800) })
        {
            _window!.AppWindow.Resize(size);
            await Task.Delay(200);
            var dialog = (ContentDialog)Microsoft.UI.Xaml.Markup.XamlReader.Load(markup.ToString());
            dialog.XamlRoot = root.XamlRoot;
            var opened = new TaskCompletionSource<bool>();
            dialog.Opened += (_, _) => opened.TrySetResult(true);
            var operation = dialog.ShowAsync();
            try
            {
                await opened.Task.WaitAsync(TimeSpan.FromSeconds(10));
                await Task.Delay(150);
                var body = (ScrollViewer)dialog.Content;
                body.UpdateLayout();
                if (body.ActualWidth <= 0 || body.ActualHeight <= 0 ||
                    body.ActualWidth > root.XamlRoot.Size.Width || body.ActualHeight > root.XamlRoot.Size.Height)
                    throw new Exception("Settings dialog body overflows its window.");
                if (dialog.FindName("SettingsDeviceNameBox") is not TextBox name || name.MaxLength != 100 ||
                    dialog.FindName("SettingsDeviceIdBox") is not TextBox id || !id.IsReadOnly ||
                    dialog.FindName("SettingsSaveDeviceNameButton") is not Button)
                    throw new Exception("Settings device controls are missing.");
                var bitmap = new RenderTargetBitmap();
                await bitmap.RenderAsync(body);
                if ((await bitmap.GetPixelsAsync()).Length == 0) throw new Exception("Settings body rendered empty.");
            }
            finally { dialog.Hide(); await operation; }
        }
    }

    private async Task CheckConnectionSetupLayoutAsync(Grid root)
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var source = XDocument.Load(Path.Combine(AppContext.BaseDirectory,"Fixtures","MainWindow.xaml.txt"));
        var markup = new XElement(source.Descendants().Single(e => (string?)e.Attribute(x + "Name") == "DbConfigDialog"));
        markup.SetAttributeValue(XNamespace.Xmlns + "x", x.NamespaceName);
        markup.SetAttributeValue(XNamespace.Xmlns + "local", "using:NFC_System");
        foreach (var attribute in markup.DescendantsAndSelf().Attributes().Where(a => a.Name.LocalName is "PrimaryButtonClick" or "Closing").ToArray()) attribute.Remove();
        foreach (var size in new[] { new Windows.Graphics.SizeInt32(640,480),new Windows.Graphics.SizeInt32(1000,800) })
        {
            _window!.AppWindow.Resize(size);
            await Task.Delay(150);
            var dialog = (ContentDialog)Microsoft.UI.Xaml.Markup.XamlReader.Load(markup.ToString());
            dialog.XamlRoot = root.XamlRoot;
            var control = (ServerConnectionControl)dialog.FindName("SetupConnectionControl");
            string id = Guid.NewGuid().ToString("N");
            control.DiscoverServers = _ => Task.FromResult<System.Collections.Generic.IReadOnlyList<DiscoveredServer>>(
                new[] { new DiscoveredServer(id,"Main Campus Server With A Longer Computer Name","192.168.1.8") });
            control.Load(new(ServerConnectionRole.Client,"192.168.1.8",id));
            ((TextBlock)dialog.FindName("DbErrorTextBlock")).Text = "The configured main server is unavailable. Check the selected device and retry the connection.";
            var opened = new TaskCompletionSource<bool>();
            dialog.Opened += (_,_) => opened.TrySetResult(true);
            var showing = dialog.ShowAsync();
            try
            {
                await opened.Task.WaitAsync(TimeSpan.FromSeconds(10));
                await control.DetectAsync();
                await Task.Delay(100);
                var body = (ScrollViewer)dialog.Content;
                body.UpdateLayout();
                if (body.ActualWidth <= 0 || body.ActualHeight <= 0 || body.ActualWidth > root.XamlRoot.Size.Width || body.ActualHeight > root.XamlRoot.Size.Height)
                    throw new Exception("Connection setup body overflows its window.");
                if (control.Configuration.ServerId != id) throw new Exception("Loading a saved client lost its server identity.");
                var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(body);
                byte[] pixels = (await bitmap.GetPixelsAsync()).ToArray();
                if (pixels.Length == 0 || pixels.All(b => b == 0)) throw new Exception("Connection setup dialog is blank.");
                using var output = File.Create(Path.Combine(AppContext.BaseDirectory,$"Connection-Setup-{size.Width}.png"));
                var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId,output.AsRandomAccessStream());
                encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
                    (uint)bitmap.PixelWidth,(uint)bitmap.PixelHeight,96,96,pixels);
                await encoder.FlushAsync();
            }
            finally { dialog.Hide(); await showing; }
        }
    }

    private async Task CheckConnectionControlAsync(Grid root)
    {
        string firstId = Guid.NewGuid().ToString("N"), secondId = Guid.NewGuid().ToString("N");
        foreach (int width in new[] { 320, 640 })
        {
            var control = new ServerConnectionControl { Width = width, HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top, RequestedTheme = ElementTheme.Dark };
            root.Children.Add(control);
            try
            {
                await Task.Delay(100);
                control.Load(ServerConnectionConfiguration.Default);
                var role = (ComboBox)control.FindName("RoleBox");
                var host = (TextBox)control.FindName("HostBox");
                var servers = (ComboBox)control.FindName("ServersBox");
                var detect = (Button)control.FindName("DetectButton");
                int searches = 0;
                control.DiscoverServers = _ =>
                {
                    searches++;
                    return Task.FromResult<System.Collections.Generic.IReadOnlyList<DiscoveredServer>>(
                        new[] { new DiscoveredServer(firstId, "Main Campus Server", "192.168.1.8") });
                };
                role.SelectedIndex = 1;
                await Task.Delay(100);
                if (searches != 1 || control.Configuration.Host != "192.168.1.8" || control.Configuration.ServerId != firstId || control.IsSearching)
                    throw new Exception($"First-time client selection failed: searches={searches}, host={control.Configuration.Host}, id={control.Configuration.ServerId}, busy={control.IsSearching}.");
                host.Text = "192.168.1.99";
                if (control.Configuration.ServerId.Length != 0) throw new Exception("Manual IP edit retained another candidate's identity.");
                control.DiscoverServers = _ => Task.FromResult<System.Collections.Generic.IReadOnlyList<DiscoveredServer>>(new[] {
                    new DiscoveredServer(firstId,"Main Campus Server","192.168.1.8"), new DiscoveredServer(secondId,"Other Server","192.168.1.9") });
                await control.DetectAsync();
                if (servers.SelectedIndex != -1 || servers.Visibility != Visibility.Visible) throw new Exception("Multiple servers were silently auto-selected.");
                servers.SelectedIndex = 1;
                if (control.Configuration.ServerId != secondId || host.Text != "192.168.1.9") throw new Exception("Explicit server selection did not fill the connection.");
                var selected = control.Configuration;
                control.DiscoverServers = async token => { await Task.Delay(5000,token); return Array.Empty<DiscoveredServer>(); };
                var pending = control.DetectAsync();
                control.CancelDiscovery();
                await pending;
                if (control.IsSearching || control.Configuration != selected || !detect.IsEnabled) throw new Exception("Cancelled discovery changed a connection or left controls disabled.");
                await Task.Delay(100);
                control.UpdateLayout();
                foreach (string name in new[] { "RoleBox", "HostBox", "DetectButton", "ServersBox", "ConnectionNote" })
                {
                    var element = (FrameworkElement)control.FindName(name);
                    var bounds = element.TransformToVisual(control).TransformBounds(new Windows.Foundation.Rect(0,0,element.ActualWidth,element.ActualHeight));
                    if (bounds.X < -1 || bounds.Right > width + 1) throw new Exception($"Connection control {name} overflows at {width}px.");
                }
                var bitmap = new RenderTargetBitmap();
                await bitmap.RenderAsync(control);
                byte[] pixels = (await bitmap.GetPixelsAsync()).ToArray();
                if (pixels.Length == 0 || pixels.All(b => b == 0)) throw new Exception("Connection control is blank.");
                using var output = File.Create(Path.Combine(AppContext.BaseDirectory,$"Server-Connection-{width}.png"));
                var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId,output.AsRandomAccessStream());
                encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
                    (uint)bitmap.PixelWidth,(uint)bitmap.PixelHeight,96,96,pixels);
                await encoder.FlushAsync();
                role.SelectedIndex = 0;
                if (control.Configuration.Host != "127.0.0.1" || detect.IsEnabled || !host.IsReadOnly) throw new Exception("Main Server role permits client discovery or a remote database.");
            }
            finally { root.Children.Remove(control); }
        }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"server-connection-smoke-result.txt"),
            "PASS: production connection controls render at 320/640px; client role auto-detects using fake discovery, single/multiple selection, manual IP identity clearing, cancellation and Main Server gating pass. No production network/database access.");
    }

    private async Task CheckCompletionDialogsAsync(Grid root)
    {
        DateTime start = new(2026, 9, 1, 9, 0, 0);
        var policy = new EventCompletionPolicy(start, start.AddHours(1), 50, 5, 5, 1, start.AddDays(-1));
        var decision = new EventCompletionDecision("Needs Review", 25, "The last check-in is missing a check-out.");
        var row = new EventCompletionRow("00-00010", "Student With A Longer Display Name", decision, decision, "stamp", null, false);
        var report = new EventCompletionReport("event", policy, false, false, start.AddHours(2), new[] { row });
        foreach (var size in new[] { new Windows.Graphics.SizeInt32(640, 480), new Windows.Graphics.SizeInt32(1000, 800) })
        {
            _window!.AppWindow.Resize(size);
            await Task.Delay(150);
            foreach (string title in new[] { "Attendance Requirements", "Event Completion", "Review Completion" })
            {
                var observed = new TaskCompletionSource<bool>();
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
                int ticks = 0;
                timer.Tick += async (_, _) =>
                {
                    if (++ticks > 70) { timer.Stop(); observed.TrySetException(new Exception("Completion dialog did not open.")); return; }
                    var dialog = VisualTreeHelper.GetOpenPopupsForXamlRoot(root.XamlRoot).Select(p => p.Child).OfType<ContentDialog>().FirstOrDefault();
                    if (dialog == null) return;
                    timer.Stop();
                    try
                    {
                        if (dialog.Title?.ToString() != title || dialog.Content is not ScrollViewer scroll || scroll.Content is not StackPanel body)
                            throw new Exception("Unexpected completion dialog content.");
                        scroll.UpdateLayout();
                        if (scroll.ActualWidth <= 0 || scroll.ActualWidth > root.XamlRoot.Size.Width || scroll.ActualHeight > root.XamlRoot.Size.Height)
                            throw new Exception("Completion dialog overflow.");
                        if (title == "Event Completion")
                        {
                            body.Children.OfType<ListView>().Single().SelectedIndex = 0;
                            if (dialog.IsPrimaryButtonEnabled) throw new Exception("Unready attendance enabled review.");
                        }
                        if (title == "Attendance Requirements" && body.Children.OfType<NumberBox>().Count() != 3)
                            throw new Exception("Duration requirement controls are missing.");
                        var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(scroll);
                        byte[] pixels = (await bitmap.GetPixelsAsync()).ToArray();
                        if (pixels.Length == 0 || pixels.All(b => b == 0)) throw new Exception("Completion dialog is blank.");
                        using var output = File.Create(Path.Combine(AppContext.BaseDirectory, $"{title.Replace(' ', '-')}-{size.Width}.png"));
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
                Task operation = title switch
                {
                    "Attendance Requirements" => EventCompletionDialogs.EditRequirementsAsync(root.XamlRoot, report),
                    "Event Completion" => EventCompletionDialogs.SelectReviewAsync(root.XamlRoot, report, true),
                    _ => EventCompletionDialogs.ReviewAsync(root.XamlRoot, row)
                };
                try { await observed.Task; await operation; } finally { timer.Stop(); }
            }
        }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "completion-smoke-result.txt"), "PASS: requirements, completion list and review dialogs render at 640x480 and 1000x800, with bounded scroll bodies, nonblank pixels, duration controls and unready-review gating. Database writes and interactive file selection are covered separately or require manual testing.");
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
