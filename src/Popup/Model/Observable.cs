using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Popup.Model;

/// <summary>
/// Общая база для всего, за чем следит интерфейс. Замена
/// <c>ObservableObject</c> из Combine — с тем же смыслом: «что-то изменилось,
/// перерисуйся».
/// </summary>
public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>
    /// Сообщить, что изменилось всё сразу. Аналог <c>objectWillChange.send()</c>:
    /// нужен, когда меняется содержимое коллекции или состояние, которое проще
    /// перечитать целиком, чем перечислять по свойствам.
    /// </summary>
    public void RaiseAll() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }
}
