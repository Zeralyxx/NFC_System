using Microsoft.UI.Xaml;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace NFC_System;

public sealed partial class EventReportsWindow
{
    private bool _completionBusy;
    private static bool CanManageCompletion => EventEditPolicy.CanManage(AppSession.IsLoggedIn, AppSession.IsAdmin, AppSession.IsEventOrganizer);

    private async Task WithCompletionAsync(Func<string, long, EventCompletionReport, Task> action)
    {
        if (_completionBusy) return;
        if (_currentSelectedEvent == null) { await ShowExportResultAsync("Event required", "Select an event first."); return; }
        _completionBusy = true;
        string id = _currentSelectedEvent.Event.EventId;
        long session = AppSession.LoginVersion;
        try
        {
            var report = await _database.GetEventCompletionReportAsync(id);
            if (_reportsClosed || AppSession.LoginVersion != session) return;
            await action(id, session, report);
        }
        catch (Exception ex) { if (!_reportsClosed) await ShowExportResultAsync("Event completion", ex.Message); }
        finally { _completionBusy = false; }
    }

    private async void CompletionRequirements_Click(object sender, RoutedEventArgs e) => await WithCompletionAsync(async (id, session, report) =>
    {
        if (!CanManageCompletion) throw new InvalidOperationException("Only administrators and event organizers can change requirements.");
        var change = await EventCompletionDialogs.EditRequirementsAsync(Content.XamlRoot, report);
        if (change == null) return;
        await _database.SaveEventCompletionPolicyAsync(id, change.Value.Policy, change.Value.Reason, session);
        await ShowExportResultAsync("Requirements saved", "The change was recorded in the audit history.");
    });

    private async void CompletionReview_Click(object sender, RoutedEventArgs e) => await WithCompletionAsync(async (id, session, report) =>
    {
        while (!_reportsClosed && AppSession.LoginVersion == session)
        {
            var row = await EventCompletionDialogs.SelectReviewAsync(Content.XamlRoot, report, CanManageCompletion);
            if (row == null) return;
            var decision = await EventCompletionDialogs.ReviewAsync(Content.XamlRoot, row);
            if (decision != null)
            {
                await _database.ReviewEventCompletionAsync(id, row.StudentId, decision.Value.Status, decision.Value.Reason,
                    row.EvidenceStamp, row.Review?.Id ?? 0, session);
                await ShowExportResultAsync("Decision saved", "The review was recorded without changing the original scans.");
            }
            report = await _database.GetEventCompletionReportAsync(id);
        }
    });

    private async void CompletionExport_Click(object sender, RoutedEventArgs e) => await WithCompletionAsync(async (id, session, report) =>
    {
        var table = new SpreadsheetTable("Event Completion", new[]
        {
            new SpreadsheetColumn("Event ID"), new SpreadsheetColumn("Student ID"), new SpreadsheetColumn("Student Name"),
            new SpreadsheetColumn("Completion Status"), new SpreadsheetColumn("Paired Minutes", SpreadsheetValueKind.Number),
            new SpreadsheetColumn("Reason"), new SpreadsheetColumn("Automatic Status"), new SpreadsheetColumn("Reviewer"),
            new SpreadsheetColumn("Review Time UTC", SpreadsheetValueKind.DateTime), new SpreadsheetColumn("Evidence Stamp"),
            new SpreadsheetColumn("Device Sync Confirmed"), new SpreadsheetColumn("Evaluated At", SpreadsheetValueKind.DateTime)
        }, report.Rows.Select(r => new object?[] { id, r.StudentId, r.Name, r.Effective.Status, r.Effective.Minutes, r.Effective.Reason,
            r.Automatic.Status, r.Review?.Actor ?? "", r.Review?.ReviewedAtUtc, r.EvidenceStamp, report.Synchronized ? "Yes" : "No", report.DatabaseTime }).ToArray());
        if (await SpreadsheetPicker.SaveAsync(this, $"{id}_Completion_{DateTime.Now:yyyyMMdd}", table))
            await ShowExportResultAsync("Export complete", "The completion report was saved.");
    });
}
