using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using ZXing;
using ZXing.Common;

namespace NFC_System;

internal static class QrCredentialDisplay
{
    internal static async Task HideAsync(ContentDialog dialog)
    {
        var closed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnClosed(ContentDialog sender, ContentDialogClosedEventArgs args) => closed.TrySetResult(true);
        dialog.Closed += OnClosed;
        try
        {
            dialog.Hide();
            await closed.Task;
        }
        finally { dialog.Closed -= OnClosed; }
    }

    internal static async Task ShowAsync(Window owner, string studentId, string credential)
    {
        if (string.IsNullOrWhiteSpace(credential)) return;
        var pixels = new BarcodeWriterPixelData
        {
            Format = BarcodeFormat.QR_CODE,
            Options = new EncodingOptions { Width = 800, Height = 800, Margin = 4 }
        }.Write(credential);
        var bitmap = new WriteableBitmap(pixels.Width, pixels.Height);
        using (var stream = bitmap.PixelBuffer.AsStream()) stream.Write(pixels.Pixels, 0, pixels.Pixels.Length);
        var dialog = new ContentDialog
        {
            Title = $"Student QR: {studentId}",
            Content = new Image { Source = bitmap, Width = 320, Height = 320 },
            PrimaryButtonText = "Export PNG",
            CloseButtonText = "Close",
            XamlRoot = owner.Content.XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        try
        {
            var picker = new Windows.Storage.Pickers.FileSavePicker { SuggestedFileName = "Student-QR" };
            picker.FileTypeChoices.Add("PNG image", new[] { ".png" });
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(owner));
            var file = await picker.PickSaveFileAsync();
            if (file == null) return;
            using var output = await file.OpenAsync(Windows.Storage.FileAccessMode.ReadWrite);
            output.Size = 0;
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, (uint)pixels.Width, (uint)pixels.Height, 96, 96, pixels.Pixels);
            await encoder.FlushAsync();
        }
        catch (Exception)
        {
            await new ContentDialog { Title = "QR export failed", Content = "The credential is saved. Open the student's QR again to retry exporting.", CloseButtonText = "Close", XamlRoot = owner.Content.XamlRoot }.ShowAsync();
        }
    }
}
