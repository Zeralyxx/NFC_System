using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System;
using WinRT.Interop;

namespace NFC_System
{
    public sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            this.InitializeComponent();
            MaximizeWindow();
        }

        private void Registration_Click(object sender, RoutedEventArgs e)
        {
            var window = new RegistrationWindow();
            window.Activate();
            this.Close();
        }

        private void VerificationManagement_Click(object sender, RoutedEventArgs e)
        {
            var window = new VerificationManagementWindow();
            window.Activate();
            this.Close();
        }

        /// <summary>
        /// Opens the Event Management window (admin creates events,
        /// starts/ends attendance sessions, views attendance logs).
        /// </summary>
        private void EventManagement_Click(object sender, RoutedEventArgs e)
        {
            var window = new EventManagementWindow();
            window.Activate();
            this.Close();
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            var window = new SettingsWindow();
            window.Activate();
            this.Close();
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


    }





    public static class DispatcherQueueExtensions
    {
        public static System.Threading.Tasks.Task TryEnqueueAsync(
            this Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue,
            Action callback)
        {
            var tcs = new System.Threading.Tasks.TaskCompletionSource<object?>();
            dispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    callback();
                    tcs.SetResult(null);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });
            return tcs.Task;
        }
    }


}