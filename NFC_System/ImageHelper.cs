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

            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(photoData.AsBuffer());
            stream.Seek(0);

            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream);
            return bitmap;
        }

        /// <summary>
        /// Takes an uncompressed image stream, centers and crops it to a perfect square, 
        /// resizes it to 250x250 pixels, and aggressively compresses it into a tiny JPEG byte array.
        /// </summary>
        public static async Task<byte[]> ProcessProfileImageAsync(IRandomAccessStream sourceStream)
        {
            var decoder = await BitmapDecoder.CreateAsync(sourceStream);

            // 1. Calculate perfect square crop from the center
            uint width = decoder.PixelWidth;
            uint height = decoder.PixelHeight;
            uint minDim = Math.Min(width, height);
            uint xOffset = (width - minDim) / 2;
            uint yOffset = (height - minDim) / 2;

            using var memStream = new InMemoryRandomAccessStream();

            // 2. Setup JPEG Encoder at ~60% quality to crush file size
            var propertySet = new BitmapPropertySet();
            var qualityValue = new BitmapTypedValue(0.6, Windows.Foundation.PropertyType.Single);
            propertySet.Add("ImageQuality", qualityValue);

            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, memStream, propertySet);
            encoder.SetSoftwareBitmap(await decoder.GetSoftwareBitmapAsync());

            // 3. Apply the crop
            encoder.BitmapTransform.Bounds = new BitmapBounds
            {
                X = xOffset,
                Y = yOffset,
                Width = minDim,
                Height = minDim
            };

            // 4. Resize to exactly 250x250
            encoder.BitmapTransform.ScaledWidth = 250;
            encoder.BitmapTransform.ScaledHeight = 250;
            encoder.BitmapTransform.InterpolationMode = BitmapInterpolationMode.Fant;

            await encoder.FlushAsync();

            // 5. Output the finished byte array
            var bytes = new byte[memStream.Size];
            memStream.Seek(0);
            await memStream.ReadAsync(bytes.AsBuffer(), (uint)memStream.Size, InputStreamOptions.None);

            return bytes;
        }
    }
}