namespace AccountManagement.Models;

public sealed record PlatformRecord(string Id, string Name, string Url, long CreatedAt = 0, long UpdatedAt = 0, int AccountCount = 0)
{
    public static PlatformRecord All { get; } = new("", "全部帐号", "");
}

public sealed record AccountRecord(string Id, string PlatformId, string Name, string Login, string Notes,
    long CreatedAt = 0, long UpdatedAt = 0, string PlatformName = "")
{
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Login : Name;
    public string Subtitle => $"{PlatformName} · {Login}";
    public string UpdatedText => UpdatedAt == 0 ? "—" : DateTimeOffset.FromUnixTimeMilliseconds(UpdatedAt).ToLocalTime().ToString("yyyy-MM-dd HH:mm");
}

public sealed record UserRecord(string Id, string Username, string Role);
public sealed record LoginResult(string Token, long ExpiresAt, bool MustChangePassword);
public sealed record SavedSession(string Endpoint, string Username, string Token, long ExpiresAt);
public sealed record PasswordResult(string Password);
public sealed record IdResult(string Id);
