using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;

namespace NFC_System;

public sealed partial class StudentManagementWindow
{
    private StudentImportWindow? _importWindow;
    private HashSet<string> _latestImportIds = new(StringComparer.OrdinalIgnoreCase);
    private bool _directoryClosed;

    private async void StudentsImported(object? sender, IReadOnlyList<string> ids)
    {
        if (_directoryClosed) return;
        _latestImportIds = new(ids, StringComparer.OrdinalIgnoreCase);
        await LoadDataAsync();
        if (_directoryClosed) return;
        ClearDirectoryFilters(latest: true);
        ClearPopupFilters(latest: true);
    }

    private async void ImportStudents_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            GraduationRules.RequireAdministrator(DatabaseMonitor.IsOnline);
            if (_importWindow == null)
            {
                _importWindow = new StudentImportWindow();
                _importWindow.StudentsImported += StudentsImported;
                _importWindow.Closed += (_, _) => _importWindow = null;
            }
            _importWindow.Activate();
        }
        catch (Exception ex) { await ShowGraduationMessageAsync("Import unavailable", ex.Message); }
    }
}
