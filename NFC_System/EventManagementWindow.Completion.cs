using Microsoft.UI.Xaml;
using System;

namespace NFC_System;

public sealed partial class EventManagementWindow
{
    private bool _requirementsBusy;
    private async void EventRequirements_Click(object sender, RoutedEventArgs e)
    {
        if (_requirementsBusy || _editorBusy) return;
        if (_selectedEvent == null) { LogMessage("[INFO] Select an event first."); return; }
        if (!EventEditPolicy.CanManage(AppSession.IsLoggedIn, AppSession.IsAdmin, AppSession.IsEventOrganizer)) return;
        string id = _selectedEvent.EventId;
        long session = AppSession.LoginVersion;
        _requirementsBusy = true;
        try
        {
            var report = await _database.GetEventCompletionReportAsync(id);
            if (session != AppSession.LoginVersion) return;
            var change = await EventCompletionDialogs.EditRequirementsAsync(Content.XamlRoot, report);
            if (change == null) return;
            await _database.SaveEventCompletionPolicyAsync(id, change.Value.Policy, change.Value.Reason, session);
            await GraduationDialogs.ShowMessageAsync(Content.XamlRoot, "Requirements saved", "The requirements change was recorded in the audit history.");
        }
        catch (Exception ex) { await GraduationDialogs.ShowMessageAsync(Content.XamlRoot, "Requirements not saved", ex.Message); }
        finally { _requirementsBusy = false; }
    }
}
