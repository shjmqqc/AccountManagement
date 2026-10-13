using AccountManagement.Models;
using System.IO;
using System.Net.Http;

namespace AccountManagement.Services;

public static class AccountImport
{
    public static async Task<bool> RunAsync(ApiClient api, IReadOnlyList<ImportRow> rows, Action progress, Func<bool> stop)
    {
        bool expired = false, interrupted = false;
        foreach (var row in rows)
        {
            if (!row.Valid) { row.Password = ""; continue; }
            if (interrupted || stop()) { row.State = "未导入"; row.Detail = "已停止，可核对结果后重新选择文件导入。"; row.Password = ""; continue; }
            try
            {
                var result = await api.ImportAccountAsync(row);
                if (result.Status is not ("created" or "skipped")) throw new ApiException(0, "invalid_response", "未能确认服务器处理结果。");
                row.State = result.Status == "created" ? "成功" : "重复跳过";
                row.Detail = result.Status == "created" ? "已导入本人帐号库。" : "已有相同平台、登录帐号和帐号名称，未修改原记录。";
            }
            catch (ApiException error)
            {
                expired = error.Status == 401 || error.Code == "password_change_required";
                interrupted = expired || error.Status == 0 || error.Status >= 500 || error.Status is 404 or 405;
                row.State = error.Status == 0 || error.Status >= 500 ? "结果待确认" : "失败";
                row.Detail = error.Message + (interrupted ? " 已停止后续导入，请先核对帐号库。" : "");
            }
            catch (Exception error) when (error is HttpRequestException or IOException or TaskCanceledException)
            {
                interrupted = true; row.State = "结果待确认";
                row.Detail = "读取导入结果时连接中断，已停止后续导入，请先核对帐号库。";
            }
            finally { row.Password = ""; progress(); }
        }
        return expired;
    }
}
