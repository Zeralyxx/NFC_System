using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System;
using System.Runtime.InteropServices;
using WinRT.Interop;

namespace NFC_System
{
    public sealed partial class AlertDetailsWindow : Window
    {
        [DllImport("User32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        public AlertDetailsWindow()
        {
            this.InitializeComponent();

            // Re-enforces the standard 550x700 size, with center alignment
            ConfigureWindowGeometry(550, 700);
        }

        private void ConfigureWindowGeometry(int logicalWidth, int logicalHeight)
        {
            IntPtr hWnd = WindowNative.GetWindowHandle(this);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            AppWindow appWindow = AppWindow.GetFromWindowId(windowId);

            if (appWindow != null)
            {
                // 1. Calculate DPI scale factor
                uint dpi = GetDpiForWindow(hWnd);
                double scaleFactor = (dpi == 0) ? 1.0 : (dpi / 96.0);

                // 2. Convert logical DIPs to physical pixels
                int physicalWidth = (int)(logicalWidth * scaleFactor);
                int physicalHeight = (int)(logicalHeight * scaleFactor);

                // 3. Find active display and calculate center coordinates
                DisplayArea displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Nearest);
                if (displayArea != null)
                {
                    // Use WorkArea to safely bypass taskbars
                    var workArea = displayArea.WorkArea;
                    int centerX = workArea.X + (workArea.Width - physicalWidth) / 2;
                    int centerY = workArea.Y + (workArea.Height - physicalHeight) / 2;

                    // 4. Reposition and scale the window
                    appWindow.MoveAndResize(new Windows.Graphics.RectInt32(centerX, centerY, physicalWidth, physicalHeight));
                }
                else
                {
                    // Fallback resize if screen math fails
                    appWindow.Resize(new Windows.Graphics.SizeInt32(physicalWidth, physicalHeight));
                }

                // 5. Restrict standard window states
                var presenter = appWindow.Presenter as OverlappedPresenter;
                if (presenter != null)
                {
                    presenter.IsResizable = false;
                    presenter.IsMaximizable = false;
                }
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void EscalateButton_Click(object sender, RoutedEventArgs e)
        {
            // Logic to permanently lock out the UID or escalate to administration
            SeverityBadge.Text = "ESCALATED";
            SeverityBadge.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Colors.Firebrick);
        }

        private void ResolveButton_Click(object sender, RoutedEventArgs e)
        {
            // Logic to update the database and clear the alert flag
            this.Close();
        }
    }
}