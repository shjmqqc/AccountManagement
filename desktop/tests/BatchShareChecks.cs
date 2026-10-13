using System.Net;
using System.Net.Http;
using System.Text;
using System.Windows;
using AccountManagement.Models;
using AccountManagement.Services;
using AccountManagement.Views;

internal static class BatchShareChecks
{
    internal static async Task Integration(ApiClient owner, ApiClient receiver, MainWindow main, AccountRecord account,
        RecipientRecord recipient, Action<bool, string> check, Action<Window, string, int, int> capture)
    {
        var vm = main.ViewModel;
        vm.CheckAll(); check(vm.CheckedCount == 1, "Select all respects tag filter");
        vm.SearchText = "new search"; check(vm.CheckedCount == 0, "Editing search clears checks");
        vm.SearchText = ""; vm.CheckAll(); vm.SelectedTag = TagRecord.All;
        check(vm.CheckedCount == 0, "Changing tags clears checks");
        vm.CheckAll(); vm.SelectedPlatform = vm.Platforms.First(p => p.Id.Length > 0);
        check(vm.CheckedCount == 0, "Changing platform clears checks");
        if (vm.SearchCommand.ExecutionTask is { } pending) await pending;
        await vm.SwitchUserAsync(null); vm.CheckAll();
        check(vm.CheckedCount == vm.Accounts.Count, "Select all includes full visible result set");
        await vm.SwitchSharedAsync(); vm.CheckAll(); check(vm.CheckedCount == 0 && !vm.CanBatchShare, "Received accounts cannot be batch shared");
        await vm.SwitchUserAsync(null);

        using var extra = new ApiClient(); extra.Configure(owner.Endpoint);
        var invite = await owner.CreateInvitationAsync();
        await extra.RegisterAsync(invite.InviteCode, "batch-receiver", "test123456");
        await extra.LoginAsync("batch-receiver", "test123456");
        var extraUser = await extra.MeAsync();
        var second = vm.Accounts.First(a => a.Id != account.Id);
        var dialog = new BatchShareWindow(owner, [account, second]);
        dialog.Recipients.Add(recipient); dialog.Recipients.Add(new RecipientRecord(extraUser.Id, extraUser.Username));
        capture(dialog, "batch-share-confirm", 820, 760);
        await dialog.SubmitAsync();
        check(dialog.Results.Count == 4 && dialog.Results.Count(r => r.State == "成功") == 3 && dialog.Results.Count(r => r.State == "已存在") == 1, "Batch adds missing grants and skips existing");
        check((await receiver.SharedAccountsAsync()).Count == 2 && (await extra.SharedAccountsAsync()).Count == 2, "Both recipients see both accounts");
        await dialog.SubmitAsync(); check(dialog.Results.Count == 4, "Completed batch cannot be submitted twice");
        capture(dialog, "batch-share-results", 820, 760); dialog.Close();
        var denied = new List<BatchShareResult>();
        await BatchSharing.RunAsync(receiver, [account], [new RecipientRecord(extraUser.Id, extraUser.Username)], denied.Add);
        check(denied.Single().State == "失败", "Receiver cannot batch reshare another owner's account");
        await owner.RevokeShareAsync(second.Id, recipient.Id);
        await owner.RevokeShareAsync(second.Id, extraUser.Id);
        await owner.RevokeShareAsync(account.Id, extraUser.Id);
        check((await owner.SharesAsync(account.Id)).Single().Id == recipient.Id, "Original grant preserved after batch cleanup");
        await vm.SwitchUserAsync(null);
    }

    internal static async Task Failures(Action<bool, string> check)
    {
        AccountRecord[] accounts = [new("a", "p", "A", "login-a", ""), new("b", "p", "B", "login-b", "")];
        RecipientRecord[] users = [new("u", "User"), new("v", "Other")];
        foreach (var status in new[] { 400, 401, 503, 0 })
        {
            var handler = new FailingHandler(status);
            using var api = new ApiClient(handler); api.Configure("http://127.0.0.1:18083");
            var results = new List<BatchShareResult>();
            bool expired = await BatchSharing.RunAsync(api, accounts, users, results.Add);
            check(results.Count == 4, "Every account/recipient pair has a result");
            check(expired == (status == 401), "Session expiry propagated");
            if (status == 400) check(handler.Writes == 4 && results.All(r => r.State == "失败"), "Independent validation failures continue");
            else check(handler.Writes == 1 && results.Skip(1).All(r => r.State == "未执行"), "Service/session failure stops further writes");
            if (status is 0 or 503) check(results[0].State == "结果未确认", "Ambiguous writes are not reported as definite failure or retried");
        }
    }

    private sealed class FailingHandler(int status) : HttpMessageHandler
    {
        public int Writes { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.Method == HttpMethod.Get) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]", Encoding.UTF8, "application/json") });
            Writes++;
            if (status == 0) throw new HttpRequestException("Simulated network loss");
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("{\"error\":\"invalid_recipient\"}", Encoding.UTF8, "application/json") });
        }
    }
}
