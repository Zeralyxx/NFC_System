using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System;
using System.Runtime.InteropServices;
using WinRT.Interop;

namespace NFC_System
{
    public sealed partial class QrScannerWindow : Window
    {
        // Import Win32 API to detect display scaling (DPI)
        [DllImport("User32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        public QrScannerWindow()
        {
            this.InitializeComponent();

            // Re-enforces the standard 600x800 size, but with centering logic
            ConfigureWindowGeometry(600, 800);
        }

        private void ConfigureWindowGeometry(int logicalWidth, int logicalHeight)
        {
            IntPtr hWnd = WindowNative.GetWindowHandle(this);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            AppWindow appWindow = AppWindow.GetFromWindowId(windowId);

            if (appWindow != null)
            {
                // 1. Get the screen DPI scaling factor
                uint dpi = GetDpiForWindow(hWnd);
                double scaleFactor = (dpi == 0) ? 1.0 : (dpi / 96.0);

                // 2. Convert logical dimensions (DIPs) to physical pixels
                int physicalWidth = (int)(logicalWidth * scaleFactor);
                int physicalHeight = (int)(logicalHeight * scaleFactor);

                // 3. Detect the active display area
                DisplayArea displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Nearest);
                if (displayArea != null)
                {
                    // Calculate absolute middle coordinates (ignoring taskbars)
                    var workArea = displayArea.WorkArea;
                    int centerX = workArea.X + (workArea.Width - physicalWidth) / 2;
                    int centerY = workArea.Y + (workArea.Height - physicalHeight) / 2;

                    // 4. Move and resize the window to center it
                    appWindow.MoveAndResize(new Windows.Graphics.RectInt32(centerX, centerY, physicalWidth, physicalHeight));
                }
                else
                {
                    // Fallback resize if monitor area fails to load
                    appWindow.Resize(new Windows.Graphics.SizeInt32(physicalWidth, physicalHeight));
                }

                // 5. Apply modal window styling constraints
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

        private void ManualCaptureButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}