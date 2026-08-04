using System;
using System.IO;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;

namespace NFC_System
{
    public static class ImageHelper
    {
        /// <summary>
        /// Converts a raw byte array back into a UI-ready BitmapImage.
        /// </summary>
        public static async Task<BitmapImage?> GetBitmapAsync(byte[]? photoData)
        {
            if (photoData == null || photoData.Length == 0) return null;

            var bitmap = new BitmapImage();
            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(photoData.AsBuffer());
            stream.Seek(0);
            await bitmap.SetSourceAsync(stream);

            return bitmap;
        }

        /// <summary>
        /// Takes an uncompressed image stream, safely calculates rotation, centers and crops it to a perfect square, 
        /// resizes it to 250x250 pixels, strips transparency, and compresses it into a tiny JPEG byte array.
        /// </summary>
        public static async Task<byte[]> ProcessProfileImageAsync(IRandomAccessStream sourceStream)
        {
            var decoder = await BitmapDecoder.CreateAsync(sourceStream);

            // 1. Get dimensions with EXIF rotation safely applied
            uint rawWidth = decoder.OrientedPixelWidth;
            uint rawHeight = decoder.OrientedPixelHeight;

            // 2. Calculate scaling so the shortest side becomes exactly 250 pixels
            double ratio = (double)rawWidth / rawHeight;
            uint scaledWidth, scaledHeight;

            if (rawWidth > rawHeight)
            {
                scaledHeight = 250;
                scaledWidth = (uint)Math.Round(250 * ratio);
            }
            else
            {
                scaledWidth = 250;
                scaledHeight = (uint)Math.Round(250 / ratio);
            }

            // 3. Set up the Transform
            // CRITICAL FIX: Because WinRT scales BEFORE it crops, the X/Y Bounds 
            // must be calculated against the new Scaled dimensions, not the Raw dimensions!
            var transform = new BitmapTransform
            {
                ScaledWidth = scaledWidth,
                ScaledHeight = scaledHeight,
                Bounds = new BitmapBounds
                {
                    X = (scaledWidth - 250) / 2,
                    Y = (scaledHeight - 250) / 2,
                    Width = 250,
                    Height = 250
                },
                InterpolationMode = BitmapInterpolationMode.Fant
            };

            // 4. Extract the perfect 250x250 pixel data (ignoring transparency for safe JPEG encoding)
            var pixelData = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Ignore,
                transform,
                ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.DoNotColorManage);

            var rawBytes = pixelData.DetachPixelData();

            // 5. Encode the extracted pixels into a lightweight JPEG
            using var memStream = new InMemoryRandomAccessStream();
            var propertySet = new BitmapPropertySet();
            propertySet.Add("ImageQuality", new BitmapTypedValue(0.7f, Windows.Foundation.PropertyType.Single)); // 70% quality

            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, memStream, propertySet);

            encoder.SetPixelData(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Ignore,
                250, // Final Output Width
                250, // Final Output Height
                96,
                96,
                rawBytes);

            await encoder.FlushAsync();

            var resultBytes = new byte[memStream.Size];
            memStream.Seek(0);
            await memStream.ReadAsync(resultBytes.AsBuffer(), (uint)memStream.Size, InputStreamOptions.None);

            return resultBytes;
        }
    }
}