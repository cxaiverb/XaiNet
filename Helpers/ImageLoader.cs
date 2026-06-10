using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace XaiNet2.Helpers
{
    public static class ImageLoader
    {
        public static BitmapImage LoadImageFromResources(string resourceName, int decodePixelWidth = 0)
        {
            object resource = Properties.Resources.ResourceManager.GetObject(resourceName);

            if (resource == null)
            {
                return null;
            }

            try
            {
                if (resource is byte[] imageBytes) // WPF stores .ico as byte[]
                {
                    using (MemoryStream memory = new MemoryStream(imageBytes))
                    {
                        var bitmapImage = new BitmapImage();
                        bitmapImage.BeginInit();
                        bitmapImage.StreamSource = memory;
                        // Decoding near the display size picks the closest .ico frame and avoids the
                        // fuzzy look of scaling a mismatched frame at render time.
                        if (decodePixelWidth > 0)
                        {
                            bitmapImage.DecodePixelWidth = decodePixelWidth;
                        }
                        bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                        bitmapImage.EndInit();
                        bitmapImage.Freeze();

                        return bitmapImage;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading image '{resourceName}': {ex.Message}");
            }

            return null;
        }

        // Builds a crisp icon Image for a button: decodes at ~2x the display size (sharp on HiDPI)
        // and uses high-quality scaling. Returns null if the resource is missing.
        public static Image CreateIcon(string resourceName, int size = 16)
        {
            var source = LoadImageFromResources(resourceName, size * 2);
            if (source == null)
            {
                return null;
            }

            var image = new Image { Source = source, Width = size, Height = size };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            return image;
        }

        // Sets a button's (or any ContentControl's) content to a crisp icon. No-ops to empty content
        // if the resource is missing.
        public static void SetIcon(ContentControl target, string resourceName, int size = 16)
        {
            target.Content = CreateIcon(resourceName, size);
        }
    }
}
