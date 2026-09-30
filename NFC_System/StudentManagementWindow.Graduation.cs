using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading.Tasks;

namespace NFC_System;

public sealed partial class StudentManagementWindow
{
    private bool _isProcessingAdminAuth;
    private bool _isGraduationDialogOpen;
    private StudentStatusPreview? _pendingStatusPreview;
    private GraduationClearanceChange? _pendingClearance;
    private long _pendingClearanceSession;

    private async void OpenGraduationClearance_Click(object sender, RoutedEventArgs e)
    {
        if (_isGraduationDialogOpen) return;
        _isGraduationDialogOpen = true;
        try
        {
            _pendingClearanceSession = GraduationRules.RequireAdministrator(DatabaseMonitor.IsOnline);
            while (true)
            {
                var students = await _database.GetGraduationDirectoryAsync();
                var selected = await GraduationDialogs.SelectStudentAsync(Content.XamlRoot, students);
                if (selected == null) return;
                _pendingClearance = await GraduationDialogs.EditClearanceAsync(Content.XamlRoot, selected);
                if (_pendingClearance == null) continue;
                await AuthorizeStudentActionAsync(AdminActionType.SaveClearance, highSeverity: false);
                return;
            }
        }
        catch (Exception ex) { await ShowGraduationMessageAsync("Clearance unavailable", ex.Message); }
        finally { _isGraduationDialogOpen = false; }
    }

    private async Task AuthorizeStudentActionAsync(AdminActionType action, bool highSeverity)
    {
        GraduationRules.RequireAdministrator(DatabaseMonitor.IsOnline);
        _pendingAction = action;
        _pendingAdminSeverity = highSeverity ? "HIGH" : "MODERATE";
        if (AppSession.CurrentStaffRoleLabel == "Master Admin")
        {
            await ExecutePendingAdminAction(AppSession.CurrentStaffName);
            return;
        }
        AdminPinBox.Visibility = highSeverity ? Visibility.Visible : Visibility.Collapsed;
        AdminPinBox.Password = "";
        AuthStatusText.Visibility = Visibility.Collapsed;
        AdminAuthDescriptionText.Text = highSeverity
            ? "Enter your staff PIN and tap an Administrator NFC card to confirm this selection."
            : "Tap an Administrator NFC card to confirm this change.";
        await ShowStudentAuthorizationAsync();
    }

    private async Task ShowStudentAuthorizationAsync()
    {
        _isAwaitingAdminAuth = true;
        AdminAuthDialog.XamlRoot = Content.XamlRoot;
        try { await AdminAuthDialog.ShowAsync(); }
        finally
        {
            // A dismissed dialog must not leave a pending hardware authorization armed.
            if (_isAwaitingAdminAuth)
            {
                _isAwaitingAdminAuth = false;
                _pendingAction = AdminActionType.None;
                _pendingStatusPreview = null;
                _pendingClearance = null;
            }
        }
    }

    private async Task ApplyPendingStatusPreviewAsync(string adminName)
    {
        var preview = _pendingStatusPreview ?? throw new InvalidOperationException("Preview the student selection again.");
        int count = await _database.ApplyStudentStatusUpdateAsync(preview, adminName);
        // The database commit succeeded even if refreshing this device's offline reference fails.
        string message = $"{count} student(s) updated to {preview.NewStatus}. {preview.ExcludedCount} excluded.";
        try { await _database.UpdateShadowCacheAsync(); }
        catch { message += " The offline cache could not be refreshed; keep this device online until synchronization succeeds."; }
        await ShowGraduationMessageAsync("Status update saved", message);
    }

    private Task ShowGraduationMessageAsync(string title, string message) => GraduationDialogs.ShowMessageAsync(Content.XamlRoot, title, message);
}
