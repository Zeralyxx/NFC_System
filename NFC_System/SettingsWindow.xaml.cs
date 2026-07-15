using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System;
using System.Runtime.InteropServices;
using WinRT.Interop;

namespace NFC_System
{
    public sealed partial class SettingsWindow : Window
    {
        [DllImport("User32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        public SettingsWindow()
        {
            this.InitializeComponent();

            // Increased to 600 width & 720 height to avoid cramped layouts
            ConfigureWindowGeometry(600, 720);
        }

        private void ConfigureWindowGeometry(int logicalWidth, int logicalHeight)
        {
            IntPtr hWnd = WindowNative.GetWindowHandle(this);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            AppWindow appWindow = AppWindow.GetFromWindowId(windowId);

            if (appWindow != null)
            {
                // 1. Calculate DPI Scale Factor
                uint dpi = GetDpiForWindow(hWnd);
                double scaleFactor = (dpi == 0) ? 1.0 : (dpi / 96.0);

                // 2. Convert logical DIPs to physical pixels
                int physicalWidth = (int)(logicalWidth * scaleFactor);
                int physicalHeight = (int)(logicalHeight * scaleFactor);

                // 3. Find active display and calculate center coordinates
                DisplayArea displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Nearest);
                if (displayArea != null)
                {
                    // Use WorkArea (safely ignores taskbars)
                    var workArea = displayArea.WorkArea;
                    int centerX = workArea.X + (workArea.Width - physicalWidth) / 2;
                    int centerY = workArea.Y + (workArea.Height - physicalHeight) / 2;

                    // 4. Relocate and Resize in one batch
                    appWindow.MoveAndResize(new Windows.Graphics.RectInt32(centerX, centerY, physicalWidth, physicalHeight));
                }
                else
                {
                    // Fallback resize if monitor area calculation fails
                    appWindow.Resize(new Windows.Graphics.SizeInt32(physicalWidth, physicalHeight));
                }

                // 5. Restrict Window Properties
                var presenter = appWindow.Presenter as OverlappedPresenter;
                if (presenter != null)
                {
                    presenter.IsResizable = false;
                    presenter.IsMaximizable = false;
                }
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}