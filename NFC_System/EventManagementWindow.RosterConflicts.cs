using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NFC_System;

public sealed partial class EventManagementWindow
{
    private async void RosterConflicts_Click(object sender, RoutedEventArgs e)
    {
        RosterConflictsButton.IsEnabled = false;
        try
        {
            long session = AppSession.LoginVersion;
            var conflicts = await _database.GetEventRosterConflictsAsync();
            if (conflicts.Count == 0)
            {
                await ShowRosterReviewDialogAsync(new ContentDialog { Title = "Roster sync conflicts", Content = "No unresolved roster conflicts.",
                    CloseButtonText = "Close", XamlRoot = Content.XamlRoot });
                return;
            }
            var list = CreateRosterReviewList(conflicts, ListViewSelectionMode.Multiple);
            var selectAll = new CheckBox { Content = $"Select all ({conflicts.Count})", IsChecked = false };
            var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(selectAll);
            panel.Children.Add(list);
            var dialog = new ContentDialog { Title = "Excluded roster entries", Content = panel, PrimaryButtonText = "Confirm exclusion",
                SecondaryButtonText = AppSession.IsAdmin ? "Use published roster" : "",
                CloseButtonText = "Cancel", IsPrimaryButtonEnabled = false, IsSecondaryButtonEnabled = false, XamlRoot = Content.XamlRoot };
            bool updating = false;
            list.SelectionChanged += (_, _) =>
            {
                dialog.IsPrimaryButtonEnabled = list.SelectedItems.Count == 1;
                dialog.IsSecondaryButtonEnabled = AppSession.IsAdmin && list.SelectedItems.Count > 0;
                updating = true;
                selectAll.IsChecked = list.SelectedItems.Count == conflicts.Count;
                updating = false;
            };
            selectAll.Checked += (_, _) => { if (!updating) list.SelectAll(); };
            selectAll.Unchecked += (_, _) => { if (!updating) list.SelectedItems.Clear(); };
            var result = await ShowRosterReviewDialogAsync(dialog);
            if (result == ContentDialogResult.Secondary)
            {
                var selected = list.SelectedItems.Cast<EventRosterConflict>().ToArray();
                var reviews = await _database.PreviewPublishedEventRosterAsync(selected, session);
                var preview = new StackPanel { Spacing = 12 };
                preview.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap,
                    Text = $"Use this only on the receiving database after the authoritative server has uploaded its roster.\n\nReplace {reviews.Count} local conflicting entries: {reviews.Count(r => r.Approved)} included, {reviews.Count(r => !r.Approved)} excluded. Cloud records and existing logs will not be changed." });
                preview.Children.Add(CreateRosterReviewList(reviews, ListViewSelectionMode.None));
                var confirmation = await ShowRosterReviewDialogAsync(new ContentDialog {
                    Title = "Accept published roster?", Content = preview, PrimaryButtonText = "Apply to this database",
                    CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close, XamlRoot = Content.XamlRoot });
                if (confirmation != ContentDialogResult.Primary) return;
                int accepted = await _database.AcceptPublishedEventRosterAsync(reviews, session);
                await ShowRosterReviewDialogAsync(new ContentDialog { Title = "Published roster accepted",
                    Content = $"{accepted} roster entries reconciled locally. No cloud roster upload was made. Run Get New Data (Pull) again to check for newer updates.",
                    CloseButtonText = "OK", XamlRoot = Content.XamlRoot });
            }
            else if (result == ContentDialogResult.Primary && list.SelectedItems.SingleOrDefault() is EventRosterConflict selected)
            {
                await _database.ConfirmEventRosterExclusionAsync(selected, session);
                await ShowRosterReviewDialogAsync(new ContentDialog { Title = "Exclusion confirmed", Content = $"{selected.EventId} | {selected.StudentId}\nCloud upload pending.",
                    CloseButtonText = "OK", XamlRoot = Content.XamlRoot });
            }
        }
        catch (Exception ex)
        {
            await ShowRosterReviewDialogAsync(new ContentDialog { Title = "Roster review unavailable", Content = ex.Message,
                CloseButtonText = "OK", XamlRoot = Content.XamlRoot });
        }
        finally { RosterConflictsButton.IsEnabled = true; }
    }

    private ListView CreateRosterReviewList<T>(IReadOnlyList<T> rows, ListViewSelectionMode selection) => new()
    {
        ItemsSource = rows, SelectionMode = selection,
        Width = Math.Max(200, Math.Min(500, Content.XamlRoot.Size.Width - 100)),
        Height = Math.Max(100, Math.Min(280, Content.XamlRoot.Size.Height - 430)),
        ItemTemplate = (DataTemplate)XamlReader.Load(
            "<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>" +
            "<TextBlock Text='{Binding DisplayLabel}' TextWrapping='Wrap' Margin='0,8'/></DataTemplate>")
    };

    private static async Task<ContentDialogResult> ShowRosterReviewDialogAsync(ContentDialog dialog)
    {
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        dialog.Closed += (_, _) => closed.TrySetResult();
        var result = await dialog.ShowAsync();
        await closed.Task;
        return result;
    }
}
