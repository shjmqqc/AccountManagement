using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using AccountManagement.Models;

namespace AccountManagement.Services;

public sealed class SessionStore
{
    private readonly string file;
    public static string DefaultEndpoint => DeploymentSettings.Current.ServerUrl;
    private string EndpointFile => Path.Combine(Path.GetDirectoryName(file)!, "server.txt");
    public SessionStore(string? directory = null)
    {
        file = Path.Combine(directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AccountManagement"), "session.bin");
    }
    public SavedSession? Load()
        => ReadSession(false);
    private SavedSession? ReadSession(bool includeExpired)
    {
        if (!File.Exists(file)) return null;
        byte[]? plain = null;
        try
        {
            plain = ProtectedData.Unprotect(File.ReadAllBytes(file), null, DataProtectionScope.CurrentUser);
            var session = JsonSerializer.Deserialize<SavedSession>(plain);
            return session is not null && (includeExpired || session.ExpiresAt > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) ? session : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or CryptographicException or JsonException) { return null; }
        finally { if (plain is not null) CryptographicOperations.ZeroMemory(plain); }
    }
    public string LoadEndpoint()
    {
        try
        {
            if (File.Exists(EndpointFile)) return ApiClient.ValidateEndpoint(File.ReadAllText(EndpointFile));
            var legacy = ReadSession(true);
            if (legacy is not null)
            {
                var endpoint = ApiClient.ValidateEndpoint(legacy.Endpoint);
                try { SaveEndpoint(endpoint); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                return endpoint;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { }
        return DefaultEndpoint;
    }
    public void SaveEndpoint(string endpoint)
    {
        endpoint = ApiClient.ValidateEndpoint(endpoint);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(EndpointFile + ".tmp", endpoint);
        File.Move(EndpointFile + ".tmp", EndpointFile, true);
    }
    public void Save(SavedSession session)
    {
        SaveEndpoint(session.Endpoint);
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
