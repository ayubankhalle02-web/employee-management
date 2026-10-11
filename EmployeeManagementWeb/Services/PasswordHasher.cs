using System.Security.Cryptography;
using System.Text;

namespace EmployeeManagementWeb.Services;

/// <summary>
/// Salted, iterated SHA-256. (PBKDF2 is not available in browser WebAssembly.)
/// Note: this app has no server, so this is demo-grade protection only.
/// </summary>
public static class PasswordHasher
{
    public static string NewSalt() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));

    public static string Hash(string password, string salt)
    {
        var data = Encoding.UTF8.GetBytes(salt + ":" + password);
        var h = SHA256.HashData(data);
        for (int i = 0; i < 5000; i++)
            h = SHA256.HashData(h.Concat(data).ToArray());
        return Convert.ToBase64String(h);
    }
}
