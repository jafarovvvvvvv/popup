using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Popup.Notch;

/// <summary>
/// Всё, что панель просит у Windows напрямую.
///
/// Панель живёт не по правилам обычного окна: она поверх всех, не берёт фокус,
/// не появляется в Alt-Tab и большую часть времени пропускает клики насквозь.
/// Ни одно из этих свойств WPF не выставляет своими средствами, поэтому окно
/// настраивается по хэндлу сразу после создания.
/// </summary>
internal static class Win32
{
    // MARK: - Стили окна

    public const int GWL_EXSTYLE = -20;

    /// <summary>Клики проходят насквозь — аналог <c>ignoresMouseEvents</c> в AppKit.</summary>
    public const int WS_EX_TRANSPARENT = 0x00000020;
    /// <summary>Окно не всплывает в Alt-Tab и на панели задач.</summary>
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    /// <summary>Клик по окну не забирает фокус у того, с чем работает пользователь.</summary>
    public const int WS_EX_NOACTIVATE = 0x08000000;
    public const int WS_EX_LAYERED = 0x00080000;

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    public static long GetExStyle(IntPtr hWnd) => (long)GetWindowLongPtr64(hWnd, GWL_EXSTYLE);

    public static void SetExStyle(IntPtr hWnd, long value) =>
        SetWindowLongPtr64(hWnd, GWL_EXSTYLE, (IntPtr)value);

    /// <summary>Добавляет или снимает один ex-стиль, не трогая остальные.</summary>
    public static void ToggleExStyle(IntPtr hWnd, int flag, bool on)
    {
        var style = GetExStyle(hWnd);
        // Через uint: int со старшим битом разошёлся бы знаком по всем 64
        // разрядам и снёс бы половину чужих стилей.
        var mask = (long)(uint)flag;
        var next = on ? style | mask : style & ~mask;
        if (next != style) SetExStyle(hWnd, next);
    }

    // MARK: - Положение окна

    public static readonly IntPtr HWND_TOPMOST = new(-1);

    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_SHOWWINDOW = 0x0040;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    // MARK: - Курсор

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    /// <summary>Положение курсора в физических пикселях экрана.</summary>
    public static Point CursorPosition()
    {
        // Единственный источник правды о том, где мышь, который не зависит от
        // маршрутизации событий: панель почти всегда click-through, и её
        // собственные MouseMove до неё просто не доходят. Оригинал пришёл к
        // тому же — см. комментарий в PointerWatcher.swift.
        return GetCursorPos(out var p) ? new Point(p.X, p.Y) : new Point(-1, -1);
    }

    // MARK: - Буфер обмена

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    /// <summary>
    /// Счётчик изменений буфера обмена. Растёт на единицу за одно настоящее
    /// копирование — сколько бы уведомлений оно ни породило.
    /// </summary>
    [DllImport("user32.dll")]
    public static extern uint GetClipboardSequenceNumber();

    public const int WM_CLIPBOARDUPDATE = 0x031D;
    public const int WM_ACTIVATE = 0x0006;
    public const int WM_DPICHANGED = 0x02E0;
    public const int WA_INACTIVE = 0;

    // MARK: - DPI

    private enum MonitorDpiType
    {
        Effective = 0,
    }

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("Shcore.dll")]
    private static extern int GetDpiForMonitor(
        IntPtr hmonitor, MonitorDpiType dpiType, out uint dpiX, out uint dpiY);

    /// <summary>
    /// Масштаб монитора, на котором лежит точка (1.0 при 100%, 1.5 при 150%).
    ///
    /// Нужен потому, что вся геометрия панели считается в физических пикселях —
    /// иначе на мониторе с масштабом, отличным от основного, окно уезжает мимо
    /// верхнего края экрана, — а содержимое верстается в DIP.
    /// </summary>
    public static double DpiScaleAt(int x, int y)
    {
        var monitor = MonitorFromPoint(new POINT { X = x, Y = y }, MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero) return 1.0;
        if (GetDpiForMonitor(monitor, MonitorDpiType.Effective, out var dpiX, out _) != 0) return 1.0;
        return dpiX / 96.0;
    }

    /// <summary>Хэндл окна, или <c>IntPtr.Zero</c> пока оно не показано.</summary>
    public static IntPtr HandleOf(Window window) => new WindowInteropHelper(window).Handle;
}
