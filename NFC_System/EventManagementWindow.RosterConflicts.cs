using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using System;
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
                await new ContentDialog { Title = "Roster sync conflicts", Content = "No unresolved roster conflicts.",
                    CloseButtonText = "Close", XamlRoot = Content.XamlRoot }.ShowAsync();
                return;
            }
            var list = new ListView
            {
                ItemsSource = conflicts, SelectionMode = ListViewSelectionMode.Single,
                Width = Math.Max(200, Math.Min(500, Content.XamlRoot.Size.Width - 100)),
                Height = Math.Max(100, Math.Min(320, Content.XamlRoot.Size.Height - 300)),
                ItemTemplate = (DataTemplate)XamlReader.Load(
                    "<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>" +
                    "<TextBlock Text='{Binding DisplayLabel}' TextWrapping='Wrap' Margin='0,8'/></DataTemplate>")
            };
            var dialog = new ContentDialog { Title = "Excluded roster entries", Content = list, PrimaryButtonText = "Confirm exclusion",
                CloseButtonText = "Cancel", IsPrimaryButtonEnabled = false, XamlRoot = Content.XamlRoot };
            list.SelectionChanged += (_, _) => dialog.IsPrimaryButtonEnabled = list.SelectedItem is EventRosterConflict;
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            dialog.Closed += (_, _) => closed.TrySetResult();
            var result = await dialog.ShowAsync();
            await closed.Task;
            if (result != ContentDialogResult.Primary || list.SelectedItem is not EventRosterConflict selected) return;
            await _database.ConfirmEventRosterExclusionAsync(selected, session);
            await new ContentDialog { Title = "Exclusion confirmed", Content = $"{selected.EventId} | {selected.StudentId}\nCloud upload pending.",
                CloseButtonText = "OK", XamlRoot = Content.XamlRoot }.ShowAsync();
        }
        catch (Exception ex)
        {
            await new ContentDialog { Title = "Roster review unavailable", Content = ex.Message,
                CloseButtonText = "OK", XamlRoot = Content.XamlRoot }.ShowAsync();
        }
        finally { RosterConflictsButton.IsEnabled = true; }
    }
}
