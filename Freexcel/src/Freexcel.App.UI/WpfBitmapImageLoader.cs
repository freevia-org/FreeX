using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Freexcel.App.UI;

public static class WpfBitmapImageLoader
{
    public static bool TryLoad(byte[]? imageBytes, out ImageSource? image)
    {
        image = null;
        if (imageBytes is not { Length: > 0 })
            return false;

        try
        {
            using var stream = new MemoryStream(imageBytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            image = bitmap;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
