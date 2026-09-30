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
    internal static string GetExportFileName(string studentId)
    {
        string name = studentId.Trim();
        foreach (char invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '_');
        name = name.TrimEnd('.', ' ');
        if (string.IsNullOrWhiteSpace(name)) return "Student-QR";
        string stem = name.Split('.')[0].ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" ||
            (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && char.IsDigit(stem[3])))
            name = "Student-" + name;
        return name;
    }

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

    internal static async Task ShowAsync(Window owner, string studentId, string credential, bool credentialReplaced = false)
    {
        if (string.IsNullOrWhiteSpace(credential)) return;
        var pixels = new BarcodeWriterPixelData
        {
            Format = BarcodeFormat.QR_CODE,
            Options = new EncodingOptions { Width = 800, Height = 800, Margin = 4 }
        }.Write(credential);
        var bitmap = new WriteableBitmap(pixels.Width, pixels.Height);
        using (var stream = bitmap.PixelBuffer.AsStream()) stream.Write(pixels.Pixels, 0, pixels.Pixels.Length);
        var qrImage = new Image { Source = bitmap, Width = 320, Height = 320 };
        FrameworkElement content = qrImage;
        if (credentialReplaced)
        {
            var replacementContent = new StackPanel { Width = 320, Spacing = 12 };
            replacementContent.Children.Add(new InfoBar
            {
                IsOpen = true,
                IsClosable = false,
                Severity = InfoBarSeverity.Success,
                Title = "QR replaced successfully",
                Message = $"Saved for student {studentId}."
            });
            replacementContent.Children.Add(qrImage);
            content = replacementContent;
        }
        var dialog = new ContentDialog
        {
            Title = $"Student QR: {studentId}",
            Content = content,
            PrimaryButtonText = "Export PNG",
            CloseButtonText = "Close",
            XamlRoot = owner.Content.XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        try
        {
            var picker = new Windows.Storage.Pickers.FileSavePicker { SuggestedFileName = GetExportFileName(studentId) };
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
