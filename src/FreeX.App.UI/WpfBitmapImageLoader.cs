using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FreeX.App.UI;

public static class WpfBitmapImageLoader
{
    private static readonly ConditionalWeakTable<byte[], CachedImageSource> ImageCache = new();

    public static bool TryLoad(byte[]? imageBytes, out ImageSource? image)
    {
        image = null;
        if (imageBytes is not { Length: > 0 })
            return false;

        try
        {
            image = ImageCache.GetValue(
                imageBytes,
                static bytes => new CachedImageSource(LoadImage(bytes))).Image;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static ImageSource LoadImage(byte[] imageBytes)
    {
        using var stream = new MemoryStream(imageBytes);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private sealed class CachedImageSource
    {
        public CachedImageSource(ImageSource image)
        {
            Image = image;
        }

        public ImageSource Image { get; }
    }
}
