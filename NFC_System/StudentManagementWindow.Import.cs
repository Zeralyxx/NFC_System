using Microsoft.UI.Xaml;
using System;

namespace NFC_System;

public sealed partial class StudentManagementWindow
{
    private StudentImportWindow? _importWindow;

    private async void ImportStudents_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            GraduationRules.RequireAdministrator(DatabaseMonitor.IsOnline);
            if (_importWindow == null)
            {
                _importWindow = new StudentImportWindow();
                _importWindow.StudentsImported += async (_, _) => await LoadDataAsync();
                _importWindow.Closed += (_, _) => _importWindow = null;
            }
            _importWindow.Activate();
        }
        catch (Exception ex) { await ShowGraduationMessageAsync("Import unavailable", ex.Message); }
    }
}
