using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Markup;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NFC_System;

public sealed record GraduationClearanceChange(GraduationStudent Student, GraduationClearanceStatus Status, string Reason);

public static class GraduationDialogs
{
    private static double BodyWidth(XamlRoot root) => Math.Max(200, Math.Min(620, root.Size.Width - 100));
    private static double ListHeight(XamlRoot root) => Math.Max(100, Math.Min(290, root.Size.Height - 390));

    private static ListView CreateList(string labelProperty, double height) => new()
    {
        Height = height,
        SelectionMode = ListViewSelectionMode.Single,
        ItemTemplate = (DataTemplate)XamlReader.Load(
            "<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>" +
            "<TextBlock Text='{Binding " + labelProperty + "}' TextWrapping='Wrap' Margin='0,8'/></DataTemplate>")
    };

    private static ContentDialog CreateDialog(XamlRoot root, string title, object content) => new()
    {
        XamlRoot = root, Title = title, Content = content, CloseButtonText = "Cancel",
        DefaultButton = ContentDialogButton.Close,
        Resources = { ["ContentDialogMaxWidth"] = 700d }
    };

    public static async Task<GraduationStudent?> SelectStudentAsync(XamlRoot root, IReadOnlyList<GraduationStudent> students)
    {
        var search = new TextBox { PlaceholderText = "Student ID, name, course or section" };
        var filter = new ComboBox { Header = "Clearance", HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new[] { "All Clearances", "Not Reviewed", "Cleared", "On Hold" }, SelectedIndex = 0 };
        var list = CreateList(nameof(GraduationStudent.DisplayLabel), ListHeight(root));
        var detail = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxHeight = 80 };
        var count = new TextBlock();
        var body = new StackPanel { Width = BodyWidth(root), Spacing = 10 };
        body.Children.Add(search);
        body.Children.Add(filter);
        body.Children.Add(count);
        body.Children.Add(list);
        body.Children.Add(detail);
        var dialog = CreateDialog(root, "Graduation Clearance", new ScrollViewer { Content = body, MaxHeight = Math.Max(200, root.Size.Height - 200) });
        dialog.PrimaryButtonText = "Review Clearance";
        dialog.IsPrimaryButtonEnabled = false;
        dialog.CloseButtonText = "Close";
        void Refresh()
        {
            string query = search.Text.Trim();
            var rows = students.Where(s => (filter.SelectedIndex == 0 || s.ClearanceLabel == filter.SelectedItem?.ToString()) &&
                (s.DisplayLabel.Contains(query, StringComparison.OrdinalIgnoreCase) || s.Reason.Contains(query, StringComparison.OrdinalIgnoreCase))).ToArray();
            list.ItemsSource = rows;
            count.Text = $"{rows.Length} student(s)";
            detail.Text = "";
            dialog.IsPrimaryButtonEnabled = false;
        }
        search.TextChanged += (_, _) => Refresh();
        filter.SelectionChanged += (_, _) => Refresh();
        list.SelectionChanged += (_, _) =>
        {
            var selected = list.SelectedItem as GraduationStudent;
            dialog.IsPrimaryButtonEnabled = selected != null;
            detail.Text = selected == null ? "" : $"{selected.Reason}\n{selected.ReviewLabel}";
        };
        Refresh();
        return await dialog.ShowAsync() == ContentDialogResult.Primary ? list.SelectedItem as GraduationStudent : null;
    }

    public static async Task<GraduationClearanceChange?> EditClearanceAsync(XamlRoot root, GraduationStudent student)
    {
        var status = new ComboBox { Header = "Clearance", ItemsSource = new[] { "Not Reviewed", "Cleared", "On Hold" },
            SelectedItem = student.ClearanceLabel, HorizontalAlignment = HorizontalAlignment.Stretch };
        var reason = new TextBox { Header = "Reason", Text = student.Reason, AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap, MaxLength = 1000, Height = 120 };
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.IndianRed) };
        var body = new StackPanel { Width = BodyWidth(root), Spacing = 12 };
        body.Children.Add(new TextBlock { Text = student.DisplayLabel, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text = student.ReviewLabel, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(status);
        body.Children.Add(reason);
        body.Children.Add(error);
        var dialog = CreateDialog(root, "Review Clearance", new ScrollViewer { Content = body, MaxHeight = Math.Max(200, root.Size.Height - 200) });
        dialog.PrimaryButtonText = "Save Clearance";
        dialog.CloseButtonText = "Back";
        dialog.PrimaryButtonClick += (_, args) =>
        {
            try { GraduationRules.ValidateReason(GraduationRules.Parse(status.SelectedItem?.ToString()), reason.Text); }
            catch (ArgumentException ex) { args.Cancel = true; error.Text = ex.Message; }
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;
        return new GraduationClearanceChange(student, GraduationRules.Parse(status.SelectedItem?.ToString()), reason.Text.Trim());
    }

    public static async Task<bool> ConfirmStatusAsync(XamlRoot root, StudentStatusPreview preview)
    {
        var list = CreateList(nameof(StudentStatusPreviewRow.DisplayLabel), ListHeight(root));
        list.SelectionMode = ListViewSelectionMode.None;
        list.ItemsSource = preview.Rows;
        var body = new StackPanel { Width = BodyWidth(root), Spacing = 12 };
        body.Children.Add(new TextBlock { Text = $"New status: {preview.NewStatus}\n{preview.EligibleCount} included | {preview.ExcludedCount} excluded",
            TextWrapping = TextWrapping.Wrap, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        body.Children.Add(list);
        var dialog = CreateDialog(root, "Confirm Student Status", body);
        dialog.PrimaryButtonText = $"Confirm {preview.EligibleCount} Students";
        dialog.IsPrimaryButtonEnabled = preview.EligibleCount > 0;
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    public static async Task ShowMessageAsync(XamlRoot root, string title, string message)
    {
        var dialog = CreateDialog(root, title, new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = BodyWidth(root) });
        dialog.CloseButtonText = "OK";
        await dialog.ShowAsync();
    }
}
