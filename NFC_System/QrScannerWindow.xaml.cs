using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Graphics.Imaging;
using Windows.Media;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Storage.Streams;
using WinRT.Interop;
using ZXing;
using ZXing.Common;

namespace NFC_System
{
    public sealed partial class QrScannerWindow : Window
    {
        private MediaFrameReader? _frameReader;
        private readonly SoftwareBitmapSource _previewSource = new();
        private DateTime _lastScanTime = DateTime.MinValue;
        private bool _forceScanRequested = false; // NEW: Flag for manual capture

        private readonly BarcodeReaderGeneric _barcodeReader = new()
        {
            AutoRotate = true,
            Options = new DecodingOptions
            {
                PossibleFormats = new List<BarcodeFormat> { BarcodeFormat.QR_CODE },
                TryHarder = true
            }
        };

        // Removed the obsolete _scanTimer here
        private IReadOnlyList<DeviceInformation> _cameraDevices = Array.Empty<DeviceInformation>();
        private MediaCapture? _mediaCapture;
        private bool _isDecoding;
        private bool _isClosing;
        private bool _isDisposingCamera;

        public event EventHandler<string>? QrCodeScanned;

        [DllImport("User32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        public QrScannerWindow()
        {
            this.InitializeComponent();
            ConfigureWindowGeometry(600, 800);

            Closed += QrScannerWindow_Closed;
            CameraDeviceComboBox.SelectionChanged += CameraDeviceComboBox_SelectionChanged;

            _ = InitializeScannerAsync();
        }

        private async Task InitializeScannerAsync()
        {
            try
            {
                SetStatus("Loading camera devices...", Colors.White);

                _cameraDevices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
                CameraDeviceComboBox.Items.Clear();

                foreach (DeviceInformation device in _cameraDevices)
                {
                    CameraDeviceComboBox.Items.Add(device.Name);
                }

                if (_cameraDevices.Count == 0)
                {
                    SetStatus("No camera detected.", Colors.Orange);
                    ManualCaptureButton.IsEnabled = false;
                    return;
                }

                CameraDeviceComboBox.SelectedIndex = 0;
            }
            catch (UnauthorizedAccessException)
            {
                SetStatus("Camera permission is blocked in Windows settings.", Colors.OrangeRed);
                ManualCaptureButton.IsEnabled = false;
            }
            catch (Exception ex)
            {
                SetStatus($"Camera setup failed: {ex.Message}", Colors.OrangeRed);
                ManualCaptureButton.IsEnabled = false;
            }
        }

        private async void CameraDeviceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CameraDeviceComboBox.SelectedIndex < 0 ||
                CameraDeviceComboBox.SelectedIndex >= _cameraDevices.Count)
            {
                return;
            }

            await StartCameraAsync(_cameraDevices[CameraDeviceComboBox.SelectedIndex].Id);
        }

        private async Task StartCameraAsync(string deviceId)
        {
            try
            {
                await DisposeCameraAsync();

                SetStatus("Starting camera...", Colors.White);
                ManualCaptureButton.IsEnabled = false;

                _mediaCapture = new MediaCapture();
                await _mediaCapture.InitializeAsync(new MediaCaptureInitializationSettings
                {
                    VideoDeviceId = deviceId,
                    StreamingCaptureMode = StreamingCaptureMode.Video,
                    MemoryPreference = MediaCaptureMemoryPreference.Cpu
                });

                // Find the active video feed
                var frameSource = _mediaCapture.FrameSources.Values.FirstOrDefault(
                    fs => fs.Info.MediaStreamType == MediaStreamType.VideoPreview)
                    ?? _mediaCapture.FrameSources.Values.FirstOrDefault();

                if (frameSource == null)
                {
                    SetStatus("Could not locate video stream.", Colors.OrangeRed);
                    return;
                }

                // Create the frame reader and listen for incoming frames
                _frameReader = await _mediaCapture.CreateFrameReaderAsync(frameSource, MediaEncodingSubtypes.Bgra8);
                _frameReader.AcquisitionMode = MediaFrameReaderAcquisitionMode.Realtime;
                _frameReader.FrameArrived += FrameReader_FrameArrived;
                await _frameReader.StartAsync();

                // Bind the continuous image updater to the UI
                CameraPreviewImage.Source = _previewSource;

                ManualCaptureButton.IsEnabled = true;
                SetStatus("Searching for QR Code...", Colors.White);
            }
            catch (Exception ex)
            {
                SetStatus($"Could not start camera: {ex.Message}", Colors.OrangeRed);
            }
        }

        private void FrameReader_FrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
        {
            if (_isDecoding || _isClosing) return;

            using var frame = sender.TryAcquireLatestFrame();
            var videoFrame = frame?.VideoMediaFrame;
            using var rawBitmap = videoFrame?.SoftwareBitmap;

            if (rawBitmap == null) return;

            // NEW: Force the camera frame into the exact format WinUI 3 demands
            using SoftwareBitmap normalizedBitmap = (rawBitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8 || rawBitmap.BitmapAlphaMode == BitmapAlphaMode.Straight)
                ? SoftwareBitmap.Convert(rawBitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied)
                : SoftwareBitmap.Copy(rawBitmap);

            // 1. Send the safely formatted frame to the screen
            var displayBitmap = SoftwareBitmap.Copy(normalizedBitmap);
            DispatcherQueue.TryEnqueue(async () =>
            {
                if (!_isClosing)
                {
                    try
                    {
                        await _previewSource.SetBitmapAsync(displayBitmap);
                    }
                    catch { /* Failsafe for window teardown timing */ }
                }
                displayBitmap.Dispose();
            });

            // 2. Throttle ZXing so it only checks for a QR code every 700ms (OR if manual capture is forced)
            if (_forceScanRequested || (DateTime.Now - _lastScanTime).TotalMilliseconds >= 700)
            {
                bool wasForced = _forceScanRequested;
                _forceScanRequested = false;

                _lastScanTime = DateTime.Now;
                _isDecoding = true;

                try
                {
                    // NEW: Pass the safely formatted bitmap to ZXing as well
                    string? payload = DecodeQrPayload(normalizedBitmap);
                    if (!string.IsNullOrWhiteSpace(payload))
                    {
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            SetStatus("QR code captured.", Colors.LightGreen);
                            QrCodeScanned?.Invoke(this, payload);
                            Close();
                        });
                    }
                    else if (wasForced)
                    {
                        DispatcherQueue.TryEnqueue(() => {
                            SetStatus("No QR code found in frame.", Colors.Orange);
                        });
                    }
                }
                finally
                {
                    _isDecoding = false;
                }
            }
        }

        private string? DecodeQrPayload(SoftwareBitmap bitmap)
        {
            byte[] pixels = CopySoftwareBitmapPixels(bitmap);
            var source = new RGBLuminanceSource(
                pixels,
                bitmap.PixelWidth,
                bitmap.PixelHeight,
                RGBLuminanceSource.BitmapFormat.BGRA32);

            Result? result = _barcodeReader.Decode(source);
            return result?.Text;
        }

        private static byte[] CopySoftwareBitmapPixels(SoftwareBitmap bitmap)
        {
            uint capacity = (uint)(bitmap.PixelWidth * bitmap.PixelHeight * 4);
            var buffer = new Windows.Storage.Streams.Buffer(capacity);
            bitmap.CopyToBuffer(buffer);

            byte[] pixels = new byte[capacity];
            using DataReader reader = DataReader.FromBuffer(buffer);
            reader.ReadBytes(pixels);
            return pixels;
        }

        private void ConfigureWindowGeometry(int logicalWidth, int logicalHeight)
        {
            IntPtr hWnd = WindowNative.GetWindowHandle(this);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            AppWindow appWindow = AppWindow.GetFromWindowId(windowId);

            if (appWindow != null)
            {
                uint dpi = GetDpiForWindow(hWnd);
                double scaleFactor = dpi == 0 ? 1.0 : dpi / 96.0;

                int physicalWidth = (int)(logicalWidth * scaleFactor);
                int physicalHeight = (int)(logicalHeight * scaleFactor);

                DisplayArea displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Nearest);
                if (displayArea != null)
                {
                    Windows.Graphics.RectInt32 workArea = displayArea.WorkArea;
                    int centerX = workArea.X + (workArea.Width - physicalWidth) / 2;
                    int centerY = workArea.Y + (workArea.Height - physicalHeight) / 2;
                    appWindow.MoveAndResize(new Windows.Graphics.RectInt32(centerX, centerY, physicalWidth, physicalHeight));
                }
                else
                {
                    appWindow.Resize(new Windows.Graphics.SizeInt32(physicalWidth, physicalHeight));
                }

                if (appWindow.Presenter is OverlappedPresenter presenter)
                {
                    presenter.IsResizable = false;
                    presenter.IsMaximizable = false;
                }
            }
        }

        private void SetStatus(string message, Windows.UI.Color color)
        {
            ScanStatusText.Text = message;
            ScanStatusText.Foreground = new SolidColorBrush(color);
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _isClosing = true;
            ManualCaptureButton.IsEnabled = false;
            Close();
        }

        private void ManualCaptureButton_Click(object sender, RoutedEventArgs e)
        {
            // NEW: Signal the frame arrival loop to decode the very next incoming frame immediately
            _forceScanRequested = true;
            SetStatus("Forcing payload capture...", Colors.White);
        }

        private async void QrScannerWindow_Closed(object sender, WindowEventArgs args)
        {
            _isClosing = true;
            ManualCaptureButton.IsEnabled = false;

            try
            {
                await DisposeCameraAsync();
            }
            catch
            {
                // Window close must never crash if the camera driver is already tearing down.
            }
        }

        private async Task DisposeCameraAsync()
        {
            if (_isDisposingCamera) return;
            _isDisposingCamera = true;

            try
            {
                if (_frameReader != null)
                {
                    _frameReader.FrameArrived -= FrameReader_FrameArrived;
                    await _frameReader.StopAsync();
                    _frameReader.Dispose();
                    _frameReader = null;
                }

                if (_mediaCapture != null)
                {
                    _mediaCapture.Dispose();
                    _mediaCapture = null;
                }

                CameraPreviewImage.Source = null;
                await Task.CompletedTask;
            }
            finally
            {
                _isDisposingCamera = false;
            }
        }
    }
}