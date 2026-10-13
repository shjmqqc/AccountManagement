namespace AccountManagement.Models;

public sealed record PlatformRecord(string Id, string Name, string Url, long CreatedAt = 0, long UpdatedAt = 0, int AccountCount = 0)
{
    public static PlatformRecord All { get; } = new("", "全部帐号", "");
}

public sealed record AccountRecord(string Id, string PlatformId, string Name, string Login, string Notes,
    long CreatedAt = 0, long UpdatedAt = 0, string PlatformName = "", string PlatformUrl = "",
    string OwnerId = "", string OwnerUsername = "")
{
    public List<TagRecord> Tags { get; init; } = [];
    public int? ShareCount { get; init; }
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Login : Name;
    public IEnumerable<TagRecord> CompactTags => Tags.Take(2);
    public string ExtraTags => Tags.Count > 2 ? $"+{Tags.Count - 2}" : "";
    public string TagsSummary => string.Join("、", Tags.Select(t => t.Name));
    public string PlatformDisplay => PlatformName + (OwnerUsername.Length > 0 ? $" · {OwnerUsername}" : "");
    public string Subtitle => $"{PlatformName} · {Login}" + (OwnerUsername.Length > 0 ? $" · 来自 {OwnerUsername}" : "");
    public string UpdatedText => UpdatedAt == 0 ? "—" : DateTimeOffset.FromUnixTimeMilliseconds(UpdatedAt).ToLocalTime().ToString("yyyy-MM-dd HH:mm");
}

public sealed record UserRecord(string Id, string Username, string Role);
public sealed record LoginResult(string Token, long ExpiresAt, bool MustChangePassword);
public sealed record SavedSession(string Endpoint, string Username, string Token, long ExpiresAt);
public sealed record PasswordResult(string Password);
public sealed record IdResult(string Id);
public sealed record RecipientRecord(string Id, string Username, int Active = 1)
{
    public string Display => Username + (Active == 0 ? "（已停用）" : "");
}
public sealed record TagRecord(string Id, string Name, string Color)
{
    public int AccountCount { get; init; }
    public string Background => Color switch {
        "green" => "#DCF3E5", "orange" => "#FFF0D6", "red" => "#FCE0E0",
        "purple" => "#EEE3FA", "gray" => "#E9EDF2", _ => "#DCEEFF" };
    public string Foreground => Color switch {
        "green" => "#14613A", "orange" => "#854B00", "red" => "#9A2929",
        "purple" => "#683396", "gray" => "#455366", _ => "#125A96" };
    public static TagRecord All { get; } = new("", "全部标签", "gray");
}

public sealed record ManagedUser(string Id, string Username, string Role, int Active, int MustChangePassword, long CreatedAt, int AccountCount)
{
    public string RoleText => Role == "admin" ? "管理员" : "普通用户";
    public string StateText => Active == 0 ? "已停用" : MustChangePassword != 0 ? "待修改临时密码" : "正常";
}
public sealed record InvitationRecord(string Id, long CreatedAt, long ExpiresAt, string Status, string? UsedBy)
{
    public string StateText => Status switch { "available" => "可使用", "used" => "已使用", "revoked" => "已撤销", _ => "已过期" };
    public string CreatedText => DateTimeOffset.FromUnixTimeMilliseconds(CreatedAt).ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    public string ExpiresText => DateTimeOffset.FromUnixTimeMilliseconds(ExpiresAt).ToLocalTime().ToString("yyyy-MM-dd HH:mm");
}
public sealed record InvitationResult(string Id, string InviteCode, long ExpiresAt);
public sealed record ResetResult(string TemporaryPassword, bool MustChangePassword);
public sealed record AdminData<T>(UserRecord User, T Data);
