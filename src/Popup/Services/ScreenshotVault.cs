using System.Globalization;
using System.Windows.Media.Imaging;

namespace Popup.Services;

/// <summary>
/// Картинка из буфера, ставшая файлом.
///
/// Win+Shift+S кладёт вырезку только в буфер: следующее копирование её
/// затирает, и снимок, который был нужен через минуту, исчезает. Здесь он
/// становится файлом на полке, который можно перетащить куда угодно.
/// </summary>
public static class ScreenshotVault
{
    public static string? Save(BitmapSource image)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Screenshots);
            // Имя из времени: папка сама собой оказывается отсортированной по
            // порядку появления снимков.
            var name = DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss", CultureInfo.InvariantCulture);
            var path = Unique(Path.Combine(AppPaths.Screenshots, name + ".png"));

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using var stream = File.Create(path);
            encoder.Save(stream);
            return path;
        }
        catch (Exception e)
        {
            Log.Error("screenshot", "не сохранить снимок", e);
            return null;
        }
    }

    /// <summary>Две вырезки в одну секунду — редкость, но не невозможность.</summary>
    private static string Unique(string path)
    {
        if (!File.Exists(path)) return path;
        var folder = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var i = 2; ; i++)
        {
            var candidate = Path.Combine(folder, $"{name} ({i}){extension}");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    public static void Clear()
    {
        try
        {
            if (!Directory.Exists(AppPaths.Screenshots)) return;
            foreach (var file in Directory.EnumerateFiles(AppPaths.Screenshots, "*.png"))
                File.Delete(file);
        }
        catch (Exception e)
        {
            Log.Error("screenshot", "не очистить папку", e);
        }
    }
}
