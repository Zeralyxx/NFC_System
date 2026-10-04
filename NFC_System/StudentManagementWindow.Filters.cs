using Microsoft.UI.Xaml;
using System;
using System.Linq;

namespace NFC_System;

public sealed partial class StudentManagementWindow
{
    private void LoadDirectoryRefinements()
    {
        foreach (var bar in new[] { DirectoryRefinements, PopupRefinements })
            bar.SetOptions(_allStudents.Select(s => s.YearLevel), _allStudents.Select(s => s.SectionName),
                _latestImportIds.Count > 0, DatabaseMonitor.IsOnline);
    }

    private void DirectoryRefinements_Changed(object? sender, EventArgs e)
    {
        if (StudentListView == null || DirectoryRefinements == null) return;
        _currentSortColumn = DirectoryRefinements.SortColumn;
        _isSortAscending = DirectoryRefinements.Ascending;
        _currentPage = 1;
        UpdateSortIcons(false);
        RefreshDataGrid();
    }

    private void PopupRefinements_Changed(object? sender, EventArgs e)
    {
        if (PopupStudentListView == null || PopupRefinements == null) return;
        _popupSortColumn = PopupRefinements.SortColumn;
        _isPopupSortAscending = PopupRefinements.Ascending;
        UpdateSortIcons(true);
        ApplyPopupFilters();
    }

    private void ClearDirectoryFilters_Click(object sender, RoutedEventArgs e) => ClearDirectoryFilters();

    private void ClearDirectoryFilters(bool latest = false)
    {
        SearchBox.Text = "";
        CourseFilterComboBox.SelectedIndex = StatusFilterComboBox.SelectedIndex = 0;
        DirectoryRefinements.Reset(latest);
    }

    private void ClearPopupFilters(bool latest = false)
    {
        PopupSearchBox.Text = "";
        PopupCourseFilter.SelectedIndex = PopupStatusFilter.SelectedIndex = 0;
        PopupRefinements.Reset(latest);
    }
}
