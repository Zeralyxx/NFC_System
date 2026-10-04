using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NFC_System;

public sealed partial class DirectoryRefinementBar : UserControl
{
    private bool _updating = true;
    public event EventHandler? Changed;
    public string Year => YearBox.SelectedItem?.ToString() ?? "All Years";
    public string Section => SectionBox.SelectedItem?.ToString() ?? "All Sections";
    public string SortColumn => (SortBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "FullName";
    public bool Ascending => OrderBox.SelectedIndex != 1;
    public bool LatestOnly => LatestBox.IsChecked == true;

    public DirectoryRefinementBar()
    {
        InitializeComponent();
        SetOptions(Array.Empty<string>(), Array.Empty<string>(), false, true);
        _updating = false;
    }

    public void SetOptions(IEnumerable<string> years, IEnumerable<string> sections, bool hasImport, bool online)
    {
        _updating = true;
        Populate(YearBox, "All Years", years.OrderBy(v => int.TryParse(v, out int n) ? n : int.MaxValue));
        Populate(SectionBox, "All Sections", sections.OrderBy(v => v, StringComparer.OrdinalIgnoreCase));
        YearBox.IsEnabled = SectionBox.IsEnabled = online;
        if (!online) YearBox.SelectedIndex = SectionBox.SelectedIndex = 0;
        LatestBox.IsEnabled = hasImport;
        _updating = false;
    }

    private static void Populate(ComboBox box, string all, IEnumerable<string> values)
    {
        string selected = box.SelectedItem?.ToString() ?? all;
        var options = new[] { all }.Concat(values.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct()).ToList();
        box.ItemsSource = options;
        box.SelectedItem = options.Contains(selected) ? selected : all;
    }

    public void SetSort(string column, bool ascending)
    {
        _updating = true;
        SortBox.SelectedItem = SortBox.Items.Cast<ComboBoxItem>().First(i => i.Tag.ToString() == column);
        OrderBox.SelectedIndex = ascending ? 0 : 1;
        _updating = false;
    }

    public void Reset(bool latest = false)
    {
        _updating = true;
        YearBox.SelectedIndex = SectionBox.SelectedIndex = 0;
        LatestBox.IsChecked = latest;
        SortBox.SelectedIndex = 1;
        OrderBox.SelectedIndex = 0;
        _updating = false;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void ShowCount(int shown, int total, bool online) =>
        ResultText.Text = $"{shown:N0} of {total:N0} students" + (shown == 0 ? " | No matching students" : "") +
            (Year == "All Years" ? "" : $" | Year {Year}") + (Section == "All Sections" ? "" : $" | Section {Section}") +
            (online ? "" : " | Cached records only");

    private void Selection_Changed(object sender, SelectionChangedEventArgs e) { if (!_updating) Changed?.Invoke(this, EventArgs.Empty); }
    private void Latest_Changed(object sender, RoutedEventArgs e) { if (!_updating) Changed?.Invoke(this, EventArgs.Empty); }
}
