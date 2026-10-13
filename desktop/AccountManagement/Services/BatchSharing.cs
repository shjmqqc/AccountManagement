using AccountManagement.Models;

namespace AccountManagement.Services;

public sealed record BatchShareResult(string Account, string Login, string Platform, string Recipient, string State, string Detail);

internal static class BatchSharing
{
    internal static async Task<bool> RunAsync(ApiClient api, IReadOnlyList<AccountRecord> accounts,
        IReadOnlyList<RecipientRecord> recipients, Action<BatchShareResult> report)
    {
        string? stopped = null;
        bool expired = false;
        foreach (var account in accounts.DistinctBy(a => a.Id))
        {
            HashSet<string> existing = [];
            ApiException? readError = null;
            if (stopped is null)
            {
                try { existing = (await api.SharesAsync(account.Id)).Select(u => u.Id).ToHashSet(); }
                catch (ApiException error) { readError = error; StopIfNeeded(error); }
            }
            foreach (var recipient in recipients.DistinctBy(u => u.Id))
            {
                if (readError is not null) { Report("失败", readError.Message); continue; }
                if (stopped is not null) { Report("未执行", stopped); continue; }
                if (existing.Contains(recipient.Id)) { Report("已存在", "保留已有授权"); continue; }
                try { await api.GrantAsync(account.Id, recipient.Id); Report("成功", "已授予只读权限"); }
                catch (ApiException error)
                {
                    Report(error.Status == 0 || error.Status >= 500 ? "结果未确认" : "失败", error.Message);
                    StopIfNeeded(error);
                }
                void Report(string state, string detail) => report(new(account.DisplayName, account.Login, account.PlatformName, recipient.Username, state, detail));
            }
        }
        return expired;

        void StopIfNeeded(ApiException error)
        {
            if (error.Status == 401 || error.Code == "password_change_required") { expired = true; stopped = "登录已失效，请重新登录"; }
            else if (error.Status == 0 || error.Status >= 500 || error.Status == 429) stopped = "服务异常，已停止后续提交；请先核对分享管理中的授权";
        }
    }
}
