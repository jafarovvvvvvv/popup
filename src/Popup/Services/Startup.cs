using Microsoft.Win32;

namespace Popup.Services;

/// <summary>
/// Автозапуск при входе в систему.
///
/// Через ветку реестра <c>HKCU\...\Run</c>, а не через ярлык в папке
/// «Автозагрузка»: она пишется без прав администратора, читается одной
/// строкой и снимается так же — а панель, которую надо запускать руками,
/// не запускают вовсе.
/// </summary>
public static class Startup
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "Popup";

    private static string ExecutablePath =>
        Environment.ProcessPath ?? System.Reflection.Assembly.GetEntryAssembly()?.Location ?? "";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(Key);
            return key?.GetValue(Name) is not null;
        }
    }

    public static void SetEnabled(bool enabled, Settings settings)
    {
        using var key = Registry.CurrentUser.OpenSubKey(Key, writable: true);
        if (key is null) return;

        if (enabled) key.SetValue(Name, $"\"{ExecutablePath}\"");
        else key.DeleteValue(Name, throwOnMissingValue: false);

        settings.RunAtStartup = enabled;
        settings.Save();
    }
}
