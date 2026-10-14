using System.Runtime.InteropServices;
using System.Windows.Interop;
using Popup.Services;

namespace Popup.Notch;

/// <summary>
/// Одна глобальная клавиша, показывающая панель.
///
/// Панель открывается наведением, и это правильный способ. Но у наведения есть
/// цена: чтобы что-то посмотреть, надо оторвать руку от клавиатуры и довести
/// курсор до верха экрана. Клавиша нужна ровно для того случая, когда руки уже
/// на клавиатуре, — и ни для чего больше, поэтому она здесь одна.
/// </summary>
public sealed class Hotkey : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int HotkeyId = 0xB0B;

    [Flags]
    private enum Modifiers : uint
    {
        Alt = 0x0001,
        Control = 0x0002,
        Shift = 0x0004,
        Win = 0x0008,
        /// <summary>Не повторять, пока клавишу держат.</summary>
        NoRepeat = 0x4000,
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private IntPtr _hwnd;
    private HwndSource? _source;
    private readonly Action _action;

    public Hotkey(Action action) => _action = action;

    /// <summary>
    /// Вешает клавишу на окно панели. Возвращает <c>false</c>, если комбинацию
    /// уже занял кто-то другой, — это не повод падать, панель по-прежнему
    /// открывается мышью.
    /// </summary>
    public bool Register(IntPtr hwnd, string? combination)
    {
        Dispose();
        if (string.IsNullOrWhiteSpace(combination)) return false;
        if (!TryParse(combination, out var modifiers, out var key))
        {
            Log.Warn("hotkey", $"не разобрать сочетание: {combination}");
            return false;
        }

        _hwnd = hwnd;
        if (!RegisterHotKey(hwnd, HotkeyId, (uint)(modifiers | Modifiers.NoRepeat), key))
        {
            Log.Warn("hotkey", $"сочетание занято другим приложением: {combination}");
            _hwnd = IntPtr.Zero;
            return false;
        }

        _source = HwndSource.FromHwnd(hwnd);
        _source?.AddHook(WndProc);
        Log.Info("hotkey", $"зарегистрировано: {combination}");
        return true;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_HOTKEY || wParam.ToInt32() != HotkeyId) return IntPtr.Zero;
        _action();
        handled = true;
        return IntPtr.Zero;
    }

    /// <summary>
    /// Разбирает запись вида <c>ctrl+alt+space</c> — ту же, что в настройках
    /// LocalWhisper, чтобы не заводить в одном хозяйстве два синтаксиса.
    /// </summary>
    private static bool TryParse(string combination, out Modifiers modifiers, out uint key)
    {
        modifiers = 0;
        key = 0;

        foreach (var raw in combination.Split('+', StringSplitOptions.RemoveEmptyEntries))
        {
            var part = raw.Trim().ToLowerInvariant();
            switch (part)
            {
                case "ctrl" or "control": modifiers |= Modifiers.Control; continue;
                case "alt": modifiers |= Modifiers.Alt; continue;
                case "shift": modifiers |= Modifiers.Shift; continue;
                case "win" or "meta": modifiers |= Modifiers.Win; continue;
            }

            key = part switch
            {
                "space" => 0x20,
                "enter" or "return" => 0x0D,
                "tab" => 0x09,
                "esc" or "escape" => 0x1B,
                "up" => 0x26,
                "down" => 0x28,
                "left" => 0x25,
                "right" => 0x27,
                "`" or "tilde" or "backquote" => 0xC0,
                { Length: 1 } single when char.IsAsciiLetterOrDigit(single[0]) =>
                    char.ToUpperInvariant(single[0]),
                _ when part.StartsWith('f') && int.TryParse(part[1..], out var n) && n is >= 1 and <= 24 =>
                    (uint)(0x6F + n),
                _ => 0,
            };
        }

        return key != 0 && modifiers != 0;
    }

    public void Dispose()
    {
        if (_hwnd != IntPtr.Zero)
        {
            UnregisterHotKey(_hwnd, HotkeyId);
            _hwnd = IntPtr.Zero;
        }
        _source?.RemoveHook(WndProc);
        _source = null;
    }
}
