using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Popup.Model;

namespace Popup.Services;

/// <summary>
/// Последние сорок вещей, побывавших в буфере обмена.
///
/// Держится только в памяти и на диск не пишется. У Windows есть своя история
/// буфера (Win+V), и вторая, лежащая файлом в профиле, — это уже не удобство, а
/// копия всего, что человек копировал, включая то, что копируют из менеджера
/// паролей. Панель показывает то, что было в этом сеансе, и забывает при выходе.
///
/// Слушает <c>WM_CLIPBOARDUPDATE</c>, а не опрашивает по таймеру: событие
/// приходит ровно в момент копирования, и в покое эта вкладка не стоит системе
/// ничего.
/// </summary>
public sealed class ClipboardStore : Observable
{
    private const int Capacity = 40;

    public sealed record Entry(string Id, string? Text, BitmapSource? Image, DateTime At)
    {
        public bool IsImage => Image is not null;

        /// <summary>Одна строка для списка: перевод строки в карточке не нужен.</summary>
        public string Preview => Text is null
            ? $"{Image!.PixelWidth}×{Image.PixelHeight}"
            : string.Join(' ', Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    public ObservableCollection<Entry> Items { get; } = [];

    /// <summary>
    /// Спрашивается до того, как картинка вообще будет прочитана. Выключенная
    /// настройка не должна означать «раскодировать PNG целиком и выбросить» —
    /// это чистый нагрев ровно тех машин, чьи владельцы её и выключили.
    /// </summary>
    public Func<bool> WantsImages { get; set; } = () => true;

    /// <summary>Позвать с готовым PNG, когда в буфере оказалась картинка.</summary>
    public Action<BitmapSource>? OnImage { get; set; }

    /// <summary>
    /// Буфер обмена в Windows — один на всех, и открыт он может быть только у
    /// одного приложения за раз. Сообщение о смене приходит всем сразу, поэтому
    /// в этот самый момент буфер почти наверняка ещё держит тот, кто в него
    /// писал. Отказ здесь — не ошибка, а очередь: несколько коротких попыток
    /// разбирают её полностью.
    /// </summary>
    private static T? Retry<T>(Func<T?> read, string what) where T : class
    {
        const int Attempts = 8;
        for (var i = 0; i < Attempts; i++)
        {
            try
            {
                return read();
            }
            catch (Exception e) when (i < Attempts - 1)
            {
                _ = e;
                Thread.Sleep(25);
            }
            catch (Exception e)
            {
                Log.Warn("clipboard", $"не прочитать {what}: {e.Message}");
            }
        }
        return null;
    }

    /// <summary>
    /// Номер последнего обработанного изменения буфера.
    ///
    /// Одно копирование порождает несколько <c>WM_CLIPBOARDUPDATE</c>: тот, кто
    /// копирует, выкладывает данные в нескольких форматах, и каждая выкладка —
    /// отдельное уведомление. Именно из-за этого один снимок экрана
    /// превращался в четыре карточки на полке и четыре файла на диске.
    ///
    /// Системный счётчик растёт ровно на единицу за настоящее копирование, и
    /// сравнение с ним отсекает повторы надёжнее любых догадок по содержимому:
    /// две одинаковые вырезки подряд — это всё-таки два разных снимка.
    /// </summary>
    private uint _lastSequence;

    /// <summary>
    /// Склейка уведомлений.
    ///
    /// Одного счётчика мало. Копирование картинки — это не одна операция, а
    /// несколько: буфер очищают, потом кладут данные, потом добавляют форматы,
    /// и каждый шаг бьёт системный счётчик и рассылает уведомление. Отсюда и
    /// брались четыре карточки на один снимок экрана.
    ///
    /// Четверть секунды — это дольше любой такой серии и заведомо короче
    /// промежутка между двумя копированиями, сделанными человеком.
    /// </summary>
    private readonly DispatcherTimer _settle = new(DispatcherPriority.Background)
    {
        Interval = TimeSpan.FromMilliseconds(250),
    };

    public ClipboardStore()
    {
        _settle.Tick += (_, _) =>
        {
            _settle.Stop();
            Read();
        };
    }

    /// <summary>Позвать из обработчика <c>WM_CLIPBOARDUPDATE</c>.</summary>
    public void Refresh()
    {
        // Перезапуск отсчёта: читаем один раз, когда серия закончилась.
        _settle.Stop();
        _settle.Start();
    }

    private void Read()
    {
        var sequence = Notch.Win32.GetClipboardSequenceNumber();
        if (sequence != 0 && sequence == _lastSequence) return;
        _lastSequence = sequence;

        var text = Retry(() => Clipboard.ContainsText() ? Clipboard.GetText() : null, "текст");
        if (text is not null)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            Push(new Entry(text, text, null, DateTime.Now));
            return;
        }

        if (!WantsImages()) return;
        var image = Retry(() => Clipboard.ContainsImage() ? Clipboard.GetImage() : null, "картинку");
        if (image is null) return;

        image.Freeze();
        Push(new Entry(Guid.NewGuid().ToString("N"), null, image, DateTime.Now));
        OnImage?.Invoke(image);
    }

    private void Push(Entry entry)
    {
        // Повтор того же текста поднимается наверх, а не ложится вторым: список
        // отвечает на вопрос «что я копировал», а не «сколько раз».
        var existing = Items.FirstOrDefault(i => i.Id == entry.Id);
        if (existing is not null) Items.Remove(existing);

        Items.Insert(0, entry);
        while (Items.Count > Capacity) Items.RemoveAt(Items.Count - 1);
        RaiseAll();
    }

    /// <summary>Положить запись обратно в буфер.</summary>
    public void Copy(Entry entry)
    {
        if (entry.Text is { } text) Write(() => Clipboard.SetText(text));
        else if (entry.Image is { } image) Write(() => Clipboard.SetImage(image));
    }

    /// <summary>Положить в буфер произвольный текст — этим пользуются сниппеты и заметки.</summary>
    public void CopyText(string text) => Write(() => Clipboard.SetText(text));

    /// <summary>
    /// Записать в буфер и сразу отметить это изменение как своё.
    ///
    /// Иначе клик по строке истории возвращал бы её же обратно в список: панель
    /// пишет в буфер, система рассылает уведомление, панель его слышит и
    /// считает новым копированием.
    /// </summary>
    private void Write(Action write)
    {
        try
        {
            write();
            _lastSequence = Notch.Win32.GetClipboardSequenceNumber();
        }
        catch (Exception e)
        {
            Log.Warn("clipboard", $"не записать в буфер: {e.Message}");
        }
    }

    public void Remove(Entry entry)
    {
        Items.Remove(entry);
        RaiseAll();
    }

    public void Clear()
    {
        Items.Clear();
        RaiseAll();
    }
}
