using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Graphics;

namespace NFC_System;

public sealed partial class StudentImportWindow : Window
{
    private readonly DatabaseService _database = new();
    private SpreadsheetImportDocument? _document;
    private bool _ready;
    private bool _busy;
    public event EventHandler? StudentsImported;

    public StudentImportWindow()
    {
        if (!AppSession.CanIssueQrCredentials) throw new UnauthorizedAccessException("Only Administrators and Master Admins can import students.");
        InitializeComponent();
        AppWindow.Resize(new SizeInt32(1100, 760));
        AppWindow.Closing += (_, args) => { if (_busy) args.Cancel = true; };
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        BusyRing.IsActive = busy;
        OpenButton.IsEnabled = TemplateButton.IsEnabled = !busy;
        ImportButton.IsEnabled = !busy && _ready;
    }

    private void Notice(string title, string message, InfoBarSeverity severity)
    {
        ResultBar.Title = title;
        ResultBar.Message = message;
        ResultBar.Severity = severity;
        ResultBar.IsOpen = true;
    }

    private async void Template_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true);
        try { await SpreadsheetPicker.SaveTemplateAsync(this, StudentImportKind.Profiles); }
        catch (Exception ex) { Notice("Template not saved", ex.Message, InfoBarSeverity.Error); }
        finally { SetBusy(false); }
    }

    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true);
        _ready = false;
        _document = null;
        PreviewList.ItemsSource = null;
        SummaryText.Text = "No file selected";
        try
        {
            var file = await SpreadsheetPicker.PickImportAsync(this);
            if (file == null) return;
            var document = await Task.Run(() => SpreadsheetService.ReadImport(file.Path, StudentImportKind.Profiles));
            var preview = await _database.PreviewStudentImportAsync(document);
            _document = document;
            PreviewList.ItemsSource = preview;
            _ready = preview.Count > 0 && preview.All(r => r.Status == ImportRowStatus.Valid);
            SummaryText.Text = $"{file.Name} | {preview.Count} rows | {preview.Count(r => r.Status == ImportRowStatus.Valid)} valid | {preview.Count(r => r.Status == ImportRowStatus.Duplicate)} duplicate | {preview.Count(r => r.Status == ImportRowStatus.Unknown)} unknown | {preview.Count(r => r.Status == ImportRowStatus.Error)} errors";
            Notice(_ready ? "Ready for confirmation" : "Import blocked", _ready ? "All profiles will be saved as Pending Enrollment." : "No rows will be saved until every duplicate, unknown value, and error is corrected. Choose the corrected file again.", _ready ? InfoBarSeverity.Informational : InfoBarSeverity.Warning);
        }
        catch (Exception ex) { Notice("Preview unavailable", ex.Message, InfoBarSeverity.Error); }
        finally { SetBusy(false); }
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || !_ready || _document == null) return;
        SetBusy(true);
        try
        {
            var confirmation = new ContentDialog
            {
                XamlRoot = Content.XamlRoot, Title = "Import student profiles?",
                Content = $"Add {_document.Rows.Count} new profiles as Pending Enrollment? Existing profiles and credentials will not be changed.",
                PrimaryButtonText = "Import", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close
            };
            if (await confirmation.ShowAsync() != ContentDialogResult.Primary) return;
            int count = await _database.ImportStudentsAsync(_document);
            _ready = false;
            _document = null;
            Notice("Import complete", $"{count} profiles saved as Pending Enrollment.", InfoBarSeverity.Success);
            StudentsImported?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _ready = false;
            Notice("Import not completed", ex.Message + " Reload the file to check current records before retrying.", InfoBarSeverity.Error);
        }
        finally { SetBusy(false); }
    }
}
