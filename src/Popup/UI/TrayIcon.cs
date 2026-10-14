using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Popup.Model;
using Popup.Notch;
using Popup.Services;
// Единственный файл, которому нужен WinForms, — и единственный, где имя
// Application приходится разводить руками.
using Application = System.Windows.Application;

namespace Popup.UI;

/// <summary>
/// Иконка в трее — то, чем на Windows заменяется пункт в строке меню macOS.
///
/// Здесь живёт всё, что не помещается в саму панель: переключатели
/// приватности, папки, автозапуск и выход. В панели этого нет намеренно —
/// она открывается наведением и закрывается уходом курсора, и настройкам,
/// которые надо спокойно перещёлкать, там не место.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Settings _settings;
    private readonly NotchController _controller;

    public TrayIcon(Settings settings, NotchController controller)
    {
        _settings = settings;
        _controller = controller;

        _icon = new NotifyIcon
        {
            Icon = BuildIcon(),
            Text = "Popup",
            Visible = true,
            ContextMenuStrip = BuildMenu(),
        };
        _icon.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) _controller.Toggle();
        };
    }

    /// <summary>
    /// Иконка рисуется здесь же, а не лежит файлом.
    ///
    /// Ей ровно один силуэт — тот же, что у свёрнутой панели, — и держать ради
    /// него бинарный ресурс, который не открыть и не поправить в редакторе
    /// кода, ни к чему.
    /// </summary>
    private static Icon BuildIcon()
    {
        const int size = 32;
        using var bitmap = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            using var path = new GraphicsPath();
            // Полоска с закруглённым низом, прижатая к верхнему краю —
            // свёрнутая панель в миниатюре.
            const float x = 4, y = 6, w = size - 8, h = 13, r = 5;
            path.AddLine(x, y, x + w - r, y);
            path.AddArc(x + w - 2 * r, y + h - 2 * r, 2 * r, 2 * r, 0, 90);
            path.AddLine(x + r, y + h, x + r, y + h);
            path.AddArc(x, y + h - 2 * r, 2 * r, 2 * r, 90, 90);
            path.CloseFigure();

            using var brush = new SolidBrush(Color.White);
            g.FillPath(brush, path);
        }

        var handle = bitmap.GetHicon();
        try
        {
            // Клонируем: иконка, сделанная из HICON, владеет чужим хэндлом и
            // перестаёт быть годной, как только его освободят.
            using var temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(handle);
        }
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip { ShowImageMargin = false };

        menu.Items.Add(new ToolStripMenuItem(Strings.L("Show panel"), null,
            (_, _) => _controller.Toggle()));

        menu.Items.Add(new ToolStripSeparator());

        // Приватность. Первым — «Всё», потому что это ответ в большинстве
        // случаев и о нём не надо думать.
        var privacy = new ToolStripMenuItem(Strings.L("Privacy"));
        var all = new ToolStripMenuItem(Strings.L("All")) { CheckOnClick = true };
        all.Click += (_, _) => _controller.Privacy?.SetCoveringAll(all.Checked);
        privacy.DropDownItems.Add(all);
        privacy.DropDownItems.Add(new ToolStripSeparator());

        var sectionItems = new Dictionary<PrivacyMode.Section, ToolStripMenuItem>();
        foreach (var section in PrivacyMode.AllSections)
        {
            var item = new ToolStripMenuItem(Strings.L(section.ToString())) { CheckOnClick = true };
            var captured = section;
            item.Click += (_, _) => _controller.Privacy?.SetCovering(captured, item.Checked);
            sectionItems[section] = item;
            privacy.DropDownItems.Add(item);
        }
        // Галки читаются в момент открытия: панель могла переключить их сама.
        privacy.DropDownOpening += (_, _) =>
        {
            var mode = _controller.Privacy;
            if (mode is null) return;
            all.Checked = mode.CoversAll;
            foreach (var (section, item) in sectionItems) item.Checked = mode.Covers(section);
        };
        menu.Items.Add(privacy);

        var images = new ToolStripMenuItem(Strings.L("Save clipboard images")) { CheckOnClick = true };
        images.Click += (_, _) =>
        {
            _settings.SaveClipboardImages = images.Checked;
            _settings.Save();
        };
        menu.Items.Add(images);

        var startup = new ToolStripMenuItem(Strings.L("Run at startup")) { CheckOnClick = true };
        startup.Click += (_, _) => Startup.SetEnabled(startup.Checked, _settings);
        menu.Items.Add(startup);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem(Strings.L("Open screenshots folder"), null,
            (_, _) => Shell.OpenFolder(AppPaths.Screenshots)));
        menu.Items.Add(new ToolStripMenuItem(Strings.L("Open plugins folder"), null,
            (_, _) => Shell.OpenFolder(AppPaths.Plugins)));
        menu.Items.Add(new ToolStripMenuItem(Strings.L("Settings file"), null,
            (_, _) =>
            {
                // Файл настроек открывают, чтобы его править. Показать вместо
                // него папку — значит попросить сделать ещё один клик там, где
                // его можно не делать.
                if (!File.Exists(AppPaths.Settings)) _settings.Save();
                Shell.OpenFile(AppPaths.Settings);
            }));

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem(Strings.L("Quit"), null,
            (_, _) => Application.Current.Shutdown()));

        menu.Opening += (_, _) =>
        {
            images.Checked = _settings.SaveClipboardImages;
            startup.Checked = Startup.IsEnabled;
        };

        return menu;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }

    private static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr handle);
    }
}
