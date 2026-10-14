using System.Windows;
using Popup.Notch;
using Popup.Services;
using Popup.UI;

namespace Popup;

public partial class App : Application
{
    private Mutex? _instanceLock;
    private NotchController? _controller;
    private TrayIcon? _tray;
    private Settings _settings = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Двух панелей на одном верхнем крае быть не может: они дрались бы за
        // одно и то же наведение и мигали бы по очереди.
        _instanceLock = new Mutex(initiallyOwned: true, "Popup.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            Shutdown();
            return;
        }

        AppPaths.EnsureCreated();
        _settings = Settings.Load();
        // Файл настроек создаётся при первом запуске со значениями по
        // умолчанию. Иначе первое знакомство с настройками — это пустое место,
        // где надо угадать и имена ключей, и их написание.
        if (!File.Exists(AppPaths.Settings)) _settings.Save();
        Strings.Configure(_settings.Language);

        _controller = new NotchController(_settings);
        _controller.PaneFactory = PaneFactory.Create;
        _controller.HeaderTrailingFactory = PaneFactory.CreateHeaderTrailing;
        _controller.Install();

        _tray = new TrayIcon(_settings, _controller);

        // Знакомство после того, как панель уже стоит на месте: пока оно на
        // экране, за ним видно то, о чём оно рассказывает.
        if (!_settings.OnboardingShown)
        {
            _settings.OnboardingShown = true;
            _settings.Save();
            new Onboarding().Show();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Teardown();
        _tray?.Dispose();
        _instanceLock?.Dispose();
        base.OnExit(e);
    }
}
