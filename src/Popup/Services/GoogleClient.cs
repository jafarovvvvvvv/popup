using System.Text.RegularExpressions;

namespace Popup.Services;

/// <summary>
/// Разбор и починка идентификатора OAuth-клиента Google.
///
/// Существует ровно из-за одного промаха, который совершают все. В Google Cloud
/// идентификатор показан рядом с подписью «.apps.googleusercontent.com», и при
/// копировании суффикс легко уезжает в буфер дважды. Получается
/// <c>…apps.googleusercontent.com.apps.googleusercontent.com</c> — с виду
/// правильная строка, которую глаз не ловит, а Google отвечает на неё
/// «Ошибка 401: invalid_client» уже в браузере, в отрыве от места, где ошиблись.
///
/// Панель чинит это молча и говорит вслух, если починить нельзя.
/// </summary>
public static partial class GoogleClient
{
    private const string Suffix = ".apps.googleusercontent.com";

    /// <summary>
    /// Приводит идентификатор к правильному виду. Возвращает <c>null</c>, если
    /// строка пустая.
    /// </summary>
    public static string? NormalizeId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var id = value.Trim();
        // Повторов может быть и больше двух: копируют по-разному.
        while (id.EndsWith(Suffix + Suffix, StringComparison.OrdinalIgnoreCase))
        {
            id = id[..^Suffix.Length];
        }
        return id;
    }

    /// <summary>
    /// Похоже ли это на идентификатор вообще. Проверяется до открытия браузера:
    /// отправлять человека к Google, чтобы тот сказал «клиент не найден», —
    /// значит перекладывать свою работу на чужую страницу ошибки.
    /// </summary>
    public static bool IsValidId(string? value)
    {
        var id = NormalizeId(value);
        return id is not null && Shape().IsMatch(id);
    }

    [GeneratedRegex(@"^\d+-[a-z0-9]+\.apps\.googleusercontent\.com$", RegexOptions.IgnoreCase)]
    private static partial Regex Shape();
}
