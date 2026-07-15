using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using WinRT.Interop;

namespace NFC_System
{
    public class EventRecordModel
    {
        public string EventId { get; set; }
        public string EventName { get; set; }
        public string EventDate { get; set; }
        public string Mode { get; set; }
        public List<AttendeeRecordModel> Roster { get; set; } = new();
    }

    public class AttendeeRecordModel
    {
        public string StudentId { get; set; }
        public string FullName { get; set; }
        public string ClearanceStatus { get; set; }
    }

    public sealed partial class EventManagementWindow : Window
    {
        private List<EventRecordModel> _mockEvents = new();

        public EventManagementWindow()
        {
            this.InitializeComponent();
            MaximizeWindow();
            LoadDummyData();
        }

        private void MaximizeWindow()
        {
            IntPtr hWnd = WindowNative.GetWindowHandle(this);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            AppWindow appWindow = AppWindow.GetFromWindowId(windowId);

            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.Maximize();
            }
        }

        private void LoadDummyData()
        {
            // Injecting temporary UI test data
            _mockEvents = new List<EventRecordModel>
            {
                new EventRecordModel
                {
                    EventId = "EVT-001", EventName = "Foundation Day Assembly", EventDate = "2026-07-20", Mode = "High-Security",
                    Roster = new List<AttendeeRecordModel>
                    {
                        new AttendeeRecordModel { StudentId = "26-00001", FullName = "Justin Mason", ClearanceStatus = "Approved" },
                        new AttendeeRecordModel { StudentId = "26-00045", FullName = "Alyssa Rivera", ClearanceStatus = "Approved" }
                    }
                },
                new EventRecordModel
                {
                    EventId = "EVT-002", EventName = "CS Department Seminar", EventDate = "2026-07-22", Mode = "Standard",
                    Roster = new List<AttendeeRecordModel>
                    {
                        new AttendeeRecordModel { StudentId = "26-00102", FullName = "Marcus Cruz", ClearanceStatus = "Approved" },
                        new AttendeeRecordModel { StudentId = "26-00001", FullName = "Justin Mason", ClearanceStatus = "Approved" }
                    }
                },
                new EventRecordModel
                {
                    EventId = "EVT-003", EventName = "University Intramurals", EventDate = "2026-08-01", Mode = "Fast",
                    Roster = new List<AttendeeRecordModel>
                    {
                        new AttendeeRecordModel { StudentId = "26-00214", FullName = "Elena Santos", ClearanceStatus = "Approved" }
                    }
                }
            };

            EventsListView.ItemsSource = _mockEvents;
        }

        private void EventsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (EventsListView.SelectedItem is EventRecordModel selectedEvent)
            {
                SelectedEventLabel.Text = $"Showing roster for: {selectedEvent.EventName}";
                AttendeesListView.ItemsSource = selectedEvent.Roster;
                ExportRosterButton.IsEnabled = true;
            }
            else
            {
                SelectedEventLabel.Text = "Select an event from the directory...";
                AttendeesListView.ItemsSource = null;
                ExportRosterButton.IsEnabled = false;
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            var dashboard = new MainWindow();
            dashboard.Activate();
            this.Close();
        }

        private void ExportEventsButton_Click(object sender, RoutedEventArgs e)
        {
            // Placeholder export feedback
            ExportEventsButton.Content = "Exporting...";
            System.Threading.Tasks.Task.Delay(1000).ContinueWith(_ =>
            {
                DispatcherQueue.TryEnqueue(() => ExportEventsButton.Content = "Export Events (CSV)");
            });
        }

        private void ExportRosterButton_Click(object sender, RoutedEventArgs e)
        {
            // Placeholder export feedback
            ExportRosterButton.Content = "Roster Exported!";
            System.Threading.Tasks.Task.Delay(1500).ContinueWith(_ =>
            {
                DispatcherQueue.TryEnqueue(() => ExportRosterButton.Content = "Export Roster (CSV)");
            });
        }
    }
}