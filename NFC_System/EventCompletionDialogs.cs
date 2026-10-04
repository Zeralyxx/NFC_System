using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace NFC_System;

public static class EventCompletionDialogs
{
    private static TextBlock Text(string value) => new() { Text = value, TextWrapping = TextWrapping.Wrap };
    private static StackPanel Body(XamlRoot root) => new() { Width = Math.Max(200, Math.Min(620, root.Size.Width - 100)), Spacing = 12 };
    private static ContentDialog Dialog(XamlRoot root, string title, StackPanel body) => new()
    {
        XamlRoot = root, Title = title, CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close,
        Content = new ScrollViewer { Content = body, MaxHeight = Math.Max(200, root.Size.Height - 200), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled },
        Resources = { ["ContentDialogMaxWidth"] = 700d }
    };

    public static async Task<(EventCompletionPolicy Policy, string Reason)?> EditRequirementsAsync(XamlRoot root, EventCompletionReport report)
    {
        DateTime start = report.Policy?.Start ?? report.DatabaseTime.AddHours(1);
        start = new DateTime(start.Year, start.Month, start.Day, start.Hour, start.Minute, 0);
        DateTime end = report.Policy?.End ?? start.AddHours(1);
        var startDate = new CalendarDatePicker { Header = "Start date", Date = new DateTimeOffset(start), HorizontalAlignment = HorizontalAlignment.Stretch };
        var startTime = new TimePicker { Header = "Start time", Time = start.TimeOfDay, HorizontalAlignment = HorizontalAlignment.Stretch };
        var endDate = new CalendarDatePicker { Header = "End date", Date = new DateTimeOffset(end), HorizontalAlignment = HorizontalAlignment.Stretch };
        var endTime = new TimePicker { Header = "End time", Time = end.TimeOfDay, HorizontalAlignment = HorizontalAlignment.Stretch };
        NumberBox Minutes(string label, int value, int min) => new() { Header = label, Minimum = min, Maximum = 10080,
            Value = value, SmallChange = 1, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        var minimum = Minutes("Required attendance (minutes)", report.Policy?.MinimumMinutes ?? 60, 1);
        var late = Minutes("Late-arrival allowance (minutes)", report.Policy?.LateAllowanceMinutes ?? 0, 0);
        var early = Minutes("Early-departure allowance (minutes)", report.Policy?.EarlyAllowanceMinutes ?? 0, 0);
        var reason = new TextBox { Header = "Reason for requirements change", MaxLength = 1000, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 80 };
        var error = Text("");
        var body = Body(root);
        foreach (var control in new UIElement[] { startDate, startTime, endDate, endTime, minimum, late, early, reason, error }) body.Children.Add(control);
        var dialog = Dialog(root, "Attendance Requirements", body);
        dialog.PrimaryButtonText = "Save Requirements";
        EventCompletionPolicy? policy = null;
        dialog.PrimaryButtonClick += (_, args) =>
        {
            try
            {
                if (!startDate.Date.HasValue || !endDate.Date.HasValue) throw new ArgumentException("Choose both dates.");
                foreach (var number in new[] { minimum.Value, late.Value, early.Value })
                    if (!double.IsFinite(number) || number != Math.Truncate(number)) throw new ArgumentException("Enter whole minutes.");
                policy = new(startDate.Date.Value.Date + startTime.Time, endDate.Date.Value.Date + endTime.Time,
                    (int)minimum.Value, (int)late.Value, (int)early.Value, report.Policy?.Revision ?? 0, report.Policy?.CoverageSince ?? default);
                EventCompletionRules.Validate(policy);
                EventCompletionRules.ValidateReason(reason.Text);
            }
            catch (ArgumentException ex) { args.Cancel = true; error.Text = ex.Message; }
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary && policy != null ? (policy, reason.Text.Trim()) : null;
    }

    public static async Task<EventCompletionRow?> SelectReviewAsync(XamlRoot root, EventCompletionReport report, bool canManage)
    {
        var body = Body(root);
        body.Children.Add(Text(report.Synchronized ? "Device synchronization confirmed at report load." : "Needs Review: device synchronization is not confirmed."));
        body.Children.Add(Text(report.EventActive ? "Event is active. Final decisions are not available." :
            report.Policy != null && report.DatabaseTime < report.Policy.End ? "Scheduled end has not been reached." : "Event is closed."));
        body.Children.Add(Text(string.Join(" | ", report.Rows.GroupBy(r => r.Effective.Status).Select(g => $"{g.Key}: {g.Count():N0}"))));
        body.Children.Add(Text(report.Policy == null ? "Attendance requirements are not configured." :
            $"{report.Policy.Start:g} - {report.Policy.End:g} | Required: {report.Policy.MinimumMinutes} min | Late: {report.Policy.LateAllowanceMinutes} min | Early: {report.Policy.EarlyAllowanceMinutes} min"));
        var search = new TextBox { PlaceholderText = "Student name or ID" };
        var filter = new ComboBox { Header = "Completion", ItemsSource = new[] { "All", EventCompletionRules.Completed, EventCompletionRules.Incomplete,
            EventCompletionRules.InProgress, EventCompletionRules.NeedsReview }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var count = Text("");
        var list = new ListView { Height = Math.Max(100, Math.Min(270, root.Size.Height - 440)), SelectionMode = ListViewSelectionMode.Single,
            ItemTemplate = (DataTemplate)XamlReader.Load("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><TextBlock Text='{Binding DisplayLabel}' TextWrapping='Wrap' Margin='0,8'/></DataTemplate>") };
        var detail = Text("");
        foreach (var control in new UIElement[] { search, filter, count, list, detail }) body.Children.Add(control);
        var dialog = Dialog(root, "Event Completion", body);
        dialog.CloseButtonText = "Close";
        if (canManage) dialog.PrimaryButtonText = "Review Student";
        dialog.IsPrimaryButtonEnabled = false;
        void Refresh()
        {
            var rows = report.Rows.Where(r => (filter.SelectedIndex == 0 || r.Effective.Status == filter.SelectedItem?.ToString()) &&
                (r.Name.Contains(search.Text.Trim(), StringComparison.OrdinalIgnoreCase) || r.StudentId.Contains(search.Text.Trim(), StringComparison.OrdinalIgnoreCase))).ToArray();
            list.ItemsSource = rows;
            count.Text = $"{rows.Length:N0} students";
            dialog.IsPrimaryButtonEnabled = false;
            detail.Text = "";
        }
        search.TextChanged += (_, _) => Refresh();
        filter.SelectionChanged += (_, _) => Refresh();
        list.SelectionChanged += (_, _) =>
        {
            var row = list.SelectedItem as EventCompletionRow;
            detail.Text = row == null ? "" : $"{row.Effective.Reason}\nAutomatic: {row.Automatic.Status}. {row.Automatic.Reason}\n{row.ReviewLabel}";
            dialog.IsPrimaryButtonEnabled = canManage && row?.CanReview == true;
        };
        Refresh();
        return await dialog.ShowAsync() == ContentDialogResult.Primary ? list.SelectedItem as EventCompletionRow : null;
    }

    public static async Task<(string Status, string Reason)?> ReviewAsync(XamlRoot root, EventCompletionRow row)
    {
        var body = Body(root);
        body.Children.Add(Text(row.DisplayLabel));
        body.Children.Add(Text($"Automatic: {row.Automatic.Status}. {row.Automatic.Reason}"));
        body.Children.Add(Text(row.ReviewLabel));
        var status = new ComboBox { Header = "Decision", ItemsSource = new[] { EventCompletionRules.Completed, EventCompletionRules.Incomplete, EventCompletionRules.NeedsReview },
            SelectedItem = row.Effective.Status, HorizontalAlignment = HorizontalAlignment.Stretch };
        var reason = new TextBox { Header = "Review reason", MaxLength = 1000, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 100 };
        var error = Text("");
        foreach (var control in new UIElement[] { status, reason, error }) body.Children.Add(control);
        var dialog = Dialog(root, "Review Completion", body);
        dialog.PrimaryButtonText = "Save Decision";
        dialog.PrimaryButtonClick += (_, args) =>
        {
            try
            {
                if (status.SelectedItem == null) throw new ArgumentException("Select a decision.");
                EventCompletionRules.ValidateReason(reason.Text);
            }
            catch (ArgumentException ex) { args.Cancel = true; error.Text = ex.Message; }
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary ? (status.SelectedItem.ToString()!, reason.Text.Trim()) : null;
    }
}
