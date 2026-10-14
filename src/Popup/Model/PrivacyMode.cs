using Popup.Services;

namespace Popup.Model;

/// <summary>
/// Прячет содержимое панели за пеленой, когда в экран смотрит кто-то ещё.
/// Порт <c>PrivacyMode.swift</c>.
///
/// Выбирается по разделам, а не одним выключателем на всё: во вкладках лежат
/// разные вещи, и человеку, стримящему свой стол, может быть важен буфер и
/// безразличен календарь — или наоборот. В трее первым всё равно стоит «Всё»,
/// потому что это ответ в большинстве случаев.
///
/// Выбор переживает перезапуск: забыть включить укрытие стоит дорого, поэтому
/// оно не должно зависеть от того, вспомнил ли о нём человек.
///
/// Ничего здесь не решает, что именно является секретом. Угадывать адреса и
/// номера карт регулярным выражением — значит ошибаться молча и узнавать об
/// этом уже на записи; закрыть выбранный раздел целиком предсказуемо, а
/// предсказуемость тут и есть смысл.
/// </summary>
public sealed class PrivacyMode : Observable
{
    public enum Section
    {
        Clipboard,
        Snippets,
        Calendar,
        Notes,
    }

    public static readonly Section[] AllSections = Enum.GetValues<Section>();

    private readonly Settings _settings;
    private readonly HashSet<Section> _sections;

    /// <summary>
    /// Что пользователь открыл вручную, по идентификатору строки. Сбрасывается
    /// всякий раз, когда панель схлопывается: открытая один раз строка не должна
    /// оставаться открытой к следующему разу — а это ровно тот момент, когда на
    /// неё никто не смотрит, кроме камеры.
    /// </summary>
    private readonly HashSet<string> _revealed = [];

    public PrivacyMode(Settings settings)
    {
        _settings = settings;
        _sections = [];
        foreach (var name in settings.PrivacySections)
        {
            if (Enum.TryParse<Section>(name, ignoreCase: true, out var section))
                _sections.Add(section);
        }
    }

    // MARK: - Разделы

    /// <summary>Закрывает ли раздел своё содержимое вообще — это же решает, предлагать ли глаз на строках.</summary>
    public bool Covers(Section section) => _sections.Contains(section);

    public bool CoversAll => _sections.Count == AllSections.Length;
    public bool CoversAny => _sections.Count > 0;

    public void SetCovering(Section section, bool on)
    {
        if (on) _sections.Add(section); else _sections.Remove(section);
        Persist();
    }

    public void SetCoveringAll(bool on)
    {
        _sections.Clear();
        if (on) foreach (var s in AllSections) _sections.Add(s);
        Persist();
    }

    private void Persist()
    {
        _settings.PrivacySections = _sections.Select(s => s.ToString()).Order().ToList();
        _settings.Save();
        if (!CoversAny) _revealed.Clear();
        RaiseAll();
    }

    // MARK: - Строки

    /// <summary>True, когда именно эту строку прямо сейчас надо закрыть.</summary>
    public bool Hides(Section section, string id) => Covers(section) && !_revealed.Contains(id);

    public void Toggle(string id)
    {
        if (!_revealed.Remove(id)) _revealed.Add(id);
        RaiseAll();
    }

    /// <summary>Обратно под укрытие — вызывается, когда панель схлопывается.</summary>
    public void CoverEverything()
    {
        if (_revealed.Count == 0) return;
        _revealed.Clear();
        RaiseAll();
    }
}
