using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AccountManagement.Models;

namespace AccountManagement.Services;

public sealed class ApiException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}

public sealed class ApiClient : IDisposable
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    private readonly HttpClient http;
    private string? token;
    public string Endpoint { get; private set; } = "http://127.0.0.1:18082/";

    public ApiClient(HttpMessageHandler? handler = null)
    {
        http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };
    }

    public static string ValidateEndpoint(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            || !(uri.Scheme == "https" || uri.Scheme == "http")
            || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.AbsolutePath != "/")
            throw new ArgumentException("服务地址须为 HTTP 或 HTTPS 根地址，例如 http://服务器IP:18082 或 https://你的域名。");
        return uri.AbsoluteUri;
    }

    public void Configure(string endpoint, string? accessToken = null)
    {
        Endpoint = ValidateEndpoint(endpoint);
        token = accessToken;
    }

    public void ClearSession() => token = null;

    public async Task<LoginResult> LoginAsync(string username, string password)
    {
        var result = await SendAsync<LoginResult>(HttpMethod.Post, "api/login", new { username, password });
        token = result.Token;
        return result;
    }

    public Task<UserRecord> MeAsync() => SendAsync<UserRecord>(HttpMethod.Get, "api/me");
    public Task<List<TagRecord>> TagsAsync() => SendAsync<List<TagRecord>>(HttpMethod.Get, "api/tags");
    public Task<TagRecord> CreateTagAsync(string name, string color) => SendAsync<TagRecord>(HttpMethod.Post, "api/tags", new { name, color });
    public Task UpdateTagAsync(string id, string name, string color) => SendAsync<JsonElement>(HttpMethod.Put, "api/tags/" + id, new { name, color });
    public Task DeleteTagAsync(string id) => SendAsync<JsonElement>(HttpMethod.Delete, "api/tags/" + id);
    public Task<ImportResponse> ImportAccountAsync(ImportRow row) => SendAsync<ImportResponse>(HttpMethod.Post, "api/accounts/import", new {
        platform_name = row.Platform, platform_url = row.Url, name = row.Name, login = row.Login,
        password = row.Password, notes = row.Notes, tag_names = row.TagNames });
    public Task<List<RecipientRecord>> FindRecipientsAsync(string username) => SendAsync<List<RecipientRecord>>(HttpMethod.Get, "api/share-recipients?q=" + Uri.EscapeDataString(username.Trim()));
    public Task<List<RecipientRecord>> SharesAsync(string accountId) => SendAsync<List<RecipientRecord>>(HttpMethod.Get, $"api/accounts/{accountId}/shares");
    public Task GrantAsync(string accountId, string recipientId) => SendAsync<JsonElement>(HttpMethod.Post, $"api/accounts/{accountId}/shares", new { recipientId });
    public Task RevokeShareAsync(string accountId, string recipientId) => SendAsync<JsonElement>(HttpMethod.Delete, $"api/accounts/{accountId}/shares/{recipientId}");
    public Task<List<AccountRecord>> SharedAccountsAsync() => SendAsync<List<AccountRecord>>(HttpMethod.Get, "api/shared/accounts");
    public Task<AccountRecord> SharedAccountAsync(string id) => SendAsync<AccountRecord>(HttpMethod.Get, $"api/shared/accounts/{id}");
    public async Task<string> SharedPasswordAsync(string id, string action) => (await SendAsync<PasswordResult>(HttpMethod.Post, $"api/shared/accounts/{id}/password", new { action })).Password;
    public Task RegisterAsync(string inviteCode, string username, string password) => SendAsync<IdResult>(HttpMethod.Post, "api/register", new { inviteCode, username, password });
    public Task<List<ManagedUser>> UsersAsync() => SendAsync<List<ManagedUser>>(HttpMethod.Get, "api/admin/users");
    public Task<List<InvitationRecord>> InvitationsAsync(string? before = null) => SendAsync<List<InvitationRecord>>(HttpMethod.Get,
        "api/admin/invitations?limit=100" + (before is null ? "" : "&before_id=" + Uri.EscapeDataString(before)));
    public Task<InvitationResult> CreateInvitationAsync() => SendAsync<InvitationResult>(HttpMethod.Post, "api/admin/invitations");
    public Task RevokeInvitationAsync(string id) => SendAsync<JsonElement>(HttpMethod.Post, $"api/admin/invitations/{id}/revoke");
    public Task SetUserActiveAsync(string id, bool active) => SendAsync<JsonElement>(HttpMethod.Post, $"api/admin/users/{id}/status", new { active });
    public Task<ResetResult> ResetPasswordAsync(string id) => SendAsync<ResetResult>(HttpMethod.Post, $"api/admin/users/{id}/reset-password");
    public async Task<List<PlatformRecord>> UserPlatformsAsync(string id) =>
        (await SendAsync<AdminData<List<PlatformRecord>>>(HttpMethod.Get, $"api/admin/users/{id}/platforms")).Data;
    public async Task<List<AccountRecord>> UserAccountsAsync(string id, string search, string? platformId)
    {
        var query = !string.IsNullOrWhiteSpace(search) ? "?q=" + Uri.EscapeDataString(search.Trim())
            : !string.IsNullOrEmpty(platformId) ? "?platform_id=" + Uri.EscapeDataString(platformId) : "";
        return (await SendAsync<AdminData<List<AccountRecord>>>(HttpMethod.Get, $"api/admin/users/{id}/accounts" + query)).Data;
    }
    public async Task<string> UserPasswordAsync(string userId, string accountId, string action) =>
        (await SendAsync<AdminData<PasswordResult>>(HttpMethod.Post, $"api/admin/users/{userId}/accounts/{accountId}/password", new { action })).Data.Password;
    public Task LogoutAsync() => SendAsync<JsonElement>(HttpMethod.Post, "api/logout");
    public Task ChangePasswordAsync(string oldPassword, string newPassword) => SendAsync<JsonElement>(HttpMethod.Post, "api/password", new { oldPassword, newPassword });
    public Task<List<PlatformRecord>> PlatformsAsync() => SendAsync<List<PlatformRecord>>(HttpMethod.Get, "api/platforms");
    public Task<List<AccountRecord>> AccountsAsync(string search, string? platformId)
    {
        var query = !string.IsNullOrWhiteSpace(search) ? "?q=" + Uri.EscapeDataString(search.Trim())
            : !string.IsNullOrEmpty(platformId) ? "?platform_id=" + Uri.EscapeDataString(platformId) : "";
        return SendAsync<List<AccountRecord>>(HttpMethod.Get, "api/accounts" + query);
    }

    public async Task<string> SavePlatformAsync(PlatformRecord? existing, string name, string url)
    {
        if (existing is null) return (await SendAsync<IdResult>(HttpMethod.Post, "api/platforms", new { name, url })).Id;
        await SendAsync<JsonElement>(HttpMethod.Put, "api/platforms/" + existing.Id, new { name, url });
        return existing.Id;
    }
    public Task DeletePlatformAsync(string id) => SendAsync<JsonElement>(HttpMethod.Delete, "api/platforms/" + id);
    public async Task<string> SaveAccountAsync(AccountRecord? existing, string platformId, string name, string login, string notes, string? password, IEnumerable<string>? tagIds = null)
    {
        var body = new Dictionary<string, object> { ["platform_id"] = platformId, ["name"] = name, ["login"] = login, ["notes"] = notes };
        if (password is not null) body["password"] = password;
        if (tagIds is not null) body["tag_ids"] = tagIds.ToArray();
        if (existing is null) return (await SendAsync<IdResult>(HttpMethod.Post, "api/accounts", body)).Id;
        await SendAsync<JsonElement>(HttpMethod.Put, "api/accounts/" + existing.Id, body);
        return existing.Id;
    }
    public Task DeleteAccountAsync(string id) => SendAsync<JsonElement>(HttpMethod.Delete, "api/accounts/" + id);
    public async Task<string> PasswordAsync(string id) => (await SendAsync<PasswordResult>(HttpMethod.Post, "api/accounts/" + id + "/password")).Password;

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, new Uri(new Uri(Endpoint), path));
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body, options: Json);
        HttpResponseMessage response;
        try { response = await http.SendAsync(request); }
        catch (TaskCanceledException) { throw new ApiException(0, "timeout", "连接超时，请检查网络或 SSH 隧道。保存操作请先刷新确认结果，再决定是否重试。"); }
        catch (HttpRequestException) { throw new ApiException(0, "network", "无法连接服务，请检查地址、网络或 SSH 隧道。"); }
        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var code = "unknown";
                try { var error = await response.Content.ReadFromJsonAsync<JsonElement>(); code = error.GetProperty("error").GetString() ?? code; }
                catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException) { }
                throw new ApiException((int)response.StatusCode, code, FriendlyError((int)response.StatusCode, code));
            }
            try { return (await response.Content.ReadFromJsonAsync<T>(Json)) ?? throw new JsonException(); }
            catch (JsonException) { throw new ApiException(0, "invalid_response", "服务响应格式不正确，请确认连接的是帐号管理服务。"); }
        }
    }

    private static string FriendlyError(int status, string code) => code switch
    {
        "invalid_credentials" => "用户名或密码不正确；连续多次失败后，请等待 15 分钟再试。",
        "recipient_username_required" => "请输入接收人的完整用户名。",
        "invalid_recipient" => "接收人不可用，不能分享给自己或已停用的用户。",
        "invalid_tag_ids" => "标签不可用或超过 20 个，请刷新后重新选择。",
        "invalid_tag_color" => "请选择预设标签颜色。",
        "tag_name_taken" => "已有同名标签，请更换名称。",
        "concurrent_change_retry" => "同时有其他操作正在修改数据，请刷新后重试。",
        "login_required" => "登录已失效，请重新登录。",
        "password_change_required" => "请先修改临时登录密码。",
        "incorrect_password" => "原登录密码不正确。",
        "invalid_invitation" => "邀请码无效、已使用、已撤销或已过期，请联系管理员。",
        "username_taken" => "该用户名已被使用，请换一个用户名；邀请码仍可继续使用。",
        "invitation_used" => "该邀请已被使用，不能撤销。请刷新列表。",
        "admin_required" => "此功能仅限管理员使用。",
        "admin_account_protected" => "不能停用或重置管理员帐号，请使用本人修改密码功能。",
        "new_password_must_differ" => "新密码不能与临时密码相同。",
        "duplicate_record" => "已存在相同的平台名称，或该平台下已有相同名称和登录帐号的记录。",
        "platform_not_empty" => "平台内还有帐号，请先删除其中的帐号。",
        "not_found" => "记录不存在或无权访问，请刷新后重试。",
        "invalid_url" => "平台网址须为完整的 http:// 或 https:// 地址。",
        "too_many_login_attempts" => "登录或注册尝试过于频繁，请稍后再试。",
        _ when status == 401 => "登录已失效，请重新登录。",
        _ when status == 400 => "填写内容不符合要求，请检查长度及必填项。",
        _ when status == 413 => "填写内容过长，请缩短后重试。",
        _ when status >= 500 => "服务暂时不可用，请稍后重试。",
        _ => $"请求未完成（HTTP {status}），请检查服务状态。"
    };

    public void Dispose() => http.Dispose();
}
