using System.Security.Cryptography;
using System.Text;

namespace Lighthouse.Shared.Auth;

/// <summary>PKCE (RFC 7636): одноразовий код-верифікатор і його SHA-256-образ, що захищають обмін коду на токен.</summary>
public static class Pkce
{
    /// <summary>43 символи base64url (256 біт випадкових даних).</summary>
    public static string CreateVerifier() => Base64Url(RandomNumberGenerator.GetBytes(32));

    public static string CreateChallenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    /// <summary>Випадковий параметр state проти підміни відповіді.</summary>
    public static string CreateState() => Base64Url(RandomNumberGenerator.GetBytes(16));

    private static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}