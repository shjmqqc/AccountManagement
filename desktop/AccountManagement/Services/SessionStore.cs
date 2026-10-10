using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using AccountManagement.Models;

namespace AccountManagement.Services;

public sealed class SessionStore
{
    private readonly string file;
    public SessionStore(string? directory = null)
    {
        file = Path.Combine(directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AccountManagement"), "session.bin");
    }
    public SavedSession? Load()
    {
        if (!File.Exists(file)) return null;
        byte[]? plain = null;
        try
        {
            plain = ProtectedData.Unprotect(File.ReadAllBytes(file), null, DataProtectionScope.CurrentUser);
            var session = JsonSerializer.Deserialize<SavedSession>(plain);
            return session is not null && session.ExpiresAt > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() ? session : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or CryptographicException or JsonException) { return null; }
        finally { if (plain is not null) CryptographicOperations.ZeroMemory(plain); }
    }
    public void Save(SavedSession session)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(session);
        try
        {
            var encrypted = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(file + ".tmp", encrypted);
            File.Move(file + ".tmp", file, true);
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    public void Clear() { if (File.Exists(file)) File.Delete(file); }
}
