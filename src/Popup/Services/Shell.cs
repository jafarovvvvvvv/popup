using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Popup.Services;

/// <summary>
/// Открыть папку, файл или ссылку так, как это сделал бы двойной клик.
///
/// Единственное место в панели, которое об этом знает, — и вот почему оно
/// нужно. Очевидный способ, <c>explorer.exe "путь"</c>, ненадёжен: путь едет
/// туда одной строкой, разбирает её проводник по своим правилам, и результат
/// зависит от того, из какого сеанса и с каким уровнем целостности его позвали.
/// На этой машине он отвечает «Location is not available» на папку, которая
/// прекрасно существует.
///
/// <c>ShellExecute</c> получает путь отдельным аргументом, никакой строки
/// команд не собирается вовсе, и папка открывается тем же способом, что и по
/// двойному клику. Разбирать нечего — ломаться нечему.
/// </summary>
public static class Shell
{
    /// <summary>Открыть папку. Отсутствующую — создать: пустая папка честнее ошибки.</summary>
    public static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Start(path);
        }
        catch (Exception e)
        {
            Log.Error("shell", $"не открыть папку {path}", e);
        }
    }

    /// <summary>
    /// Открыть файл в том, чем его открывают.
    ///
    /// Ассоциация проверяется заранее, а не ловится по исключению. У
    /// <c>.json</c> на обычной Windows её нет вовсе, и <c>ShellExecute</c> в
    /// этом случае не падает, а показывает диалог «чем вы хотите открыть» —
    /// поймать такое нечем, а человек, попросивший файл настроек, получает
    /// вопрос вместо файла. Блокнот — не изящный, но всегда присутствующий
    /// ответ, и лучше него только настоящий редактор, если он назначен.
    /// </summary>
    public static void OpenFile(string path)
    {
        if (!File.Exists(path))
        {
            Log.Warn("shell", $"файла нет: {path}");
            return;
        }

        if (HasAssociation(Path.GetExtension(path)))
        {
            try
            {
                Start(path);
                return;
            }
            catch (Exception e)
            {
                Log.Warn("shell", $"ассоциация не сработала: {e.Message}");
            }
        }

        try
        {
            Process.Start(new ProcessStartInfo("notepad.exe")
            {
                ArgumentList = { path },
                UseShellExecute = false,
            });
        }
        catch (Exception e)
        {
            Log.Error("shell", $"не открыть файл {path}", e);
        }
    }

    private const int ASSOCF_NONE = 0;
    private const int ASSOCSTR_EXECUTABLE = 2;

    [DllImport("Shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int AssocQueryString(
        int flags, int str, string assoc, string? extra, StringBuilder? output, ref int outputLength);

    /// <summary>Назначено ли расширению приложение, которое его откроет.</summary>
    private static bool HasAssociation(string extension)
    {
        if (extension.Length == 0) return false;
        try
        {
            var length = 0;
            // Первый вызов только спрашивает длину; ноль в ответе означает, что
            // обработчик есть.
            AssocQueryString(ASSOCF_NONE, ASSOCSTR_EXECUTABLE, extension, null, null, ref length);
            return length > 1;
        }
        catch (Exception e)
        {
            Log.Warn("shell", $"не спросить ассоциацию {extension}: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// Показать файл в проводнике, выделив его.
    ///
    /// Здесь <c>explorer.exe</c> неизбежен — выделять умеет только он, — но
    /// путь уезжает отдельным аргументом, а не склейкой с кавычками. Не
    /// получилось — открываем хотя бы папку: увидеть файл в списке всё же
    /// лучше, чем не увидеть ничего.
    /// </summary>
    public static void RevealFile(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe")
            {
                ArgumentList = { "/select,", path },
                UseShellExecute = false,
            });
        }
        catch (Exception e)
        {
            Log.Warn("shell", $"не выделить {path}: {e.Message}");
            if (Path.GetDirectoryName(path) is { Length: > 0 } folder) OpenFolder(folder);
        }
    }

    /// <summary>Открыть ссылку в браузере.</summary>
    public static void OpenUrl(string url)
    {
        try
        {
            Start(url);
        }
        catch (Exception e)
        {
            Log.Warn("shell", $"не открыть {url}: {e.Message}");
        }
    }

    private static void Start(string target) =>
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
}
