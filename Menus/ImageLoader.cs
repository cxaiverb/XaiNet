using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;

namespace XaiNet2.Menus
{
    public static class ImageLoader
    {
        public static BitmapImage LoadImageFromResources(string resourceName)
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
                        bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                        bitmapImage.EndInit();

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
    }
}
