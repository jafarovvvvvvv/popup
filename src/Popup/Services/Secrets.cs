using System.Security.Cryptography;
using System.Text;

namespace Popup.Services;

/// <summary>
/// Шифрование секретов ключом текущей учётной записи Windows (DPAPI).
///
/// Тот же приём, что в <c>~/.localwhisper/config.json</c> с полем
/// <c>openai_api_key_enc</c>: ключ переводчика и refresh-токен Google лежат на
/// диске в открытом виде ровно столько же, сколько и любой другой файл в
/// профиле, — то есть не лежат. Прочитать их может только этот пользователь на
/// этой машине.
/// </summary>
public static class Secrets
{
    public static string? Protect(string? plain)
    {
        if (string.IsNullOrEmpty(plain)) return null;
        var bytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(bytes);
    }

    public static string? Unprotect(string? encoded)
    {
        if (string.IsNullOrEmpty(encoded)) return null;
        try
        {
            var bytes = ProtectedData.Unprotect(
                Convert.FromBase64String(encoded), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            // Файл перенесли с другой машины или профиль пересоздали. Секрет
            // потерян — но это не повод не запуститься: пользователь просто
            // введёт ключ заново.
            return null;
        }
    }
}
