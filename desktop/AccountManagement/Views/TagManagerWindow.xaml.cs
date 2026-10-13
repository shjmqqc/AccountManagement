using System.Windows;
using System.Windows.Controls;
using AccountManagement.Models;
using AccountManagement.Services;
using Wpf.Ui.Controls;

namespace AccountManagement.Views;

public partial class TagManagerWindow : FluentWindow
{
    private readonly ApiClient api;
    private bool busy;
    public bool SessionExpired { get; private set; }
    public TagManagerWindow(ApiClient api, bool loadOnOpen = true)
    {
        InitializeComponent(); this.api = api;
        ColorPicker.ItemsSource = new[] { new TagRecord("", "蓝色", "blue"), new TagRecord("", "绿色", "green"), new TagRecord("", "橙色", "orange"), new TagRecord("", "红色", "red"), new TagRecord("", "紫色", "purple"), new TagRecord("", "灰色", "gray") };
        ColorPicker.SelectedIndex = 0;
        if (loadOnOpen) Loaded += async (_, _) => await RefreshAsync();
        Closing += (_, e) => e.Cancel = busy;
    }
    private async Task RunAsync(Func<Task> action)
    {
        if (busy) return;
        busy = true; Fields.IsEnabled = RefreshButton.IsEnabled = DeleteButton.IsEnabled = CloseButton.IsEnabled = false;
        try { await action(); }
        catch (ApiException error) { Notice.Text = error.Message; SessionExpired = error.Status == 401 || error.Code == "password_change_required"; }
        finally { busy = false; Fields.IsEnabled = RefreshButton.IsEnabled = DeleteButton.IsEnabled = CloseButton.IsEnabled = true; }
        if (SessionExpired && IsVisible) Close();
    }
    private async Task ReloadAsync(string? selected = null)
    {
        var tags = await api.TagsAsync(); TagList.ItemsSource = tags;
        TagList.SelectedItem = tags.FirstOrDefault(t => t.Id == selected);
        if (tags.Count == 0) Notice.Text = "暂无标签，可在编辑帐号或 Excel 导入时创建。";
    }
    internal Task RefreshAsync() => RunAsync(async () => { Notice.Text = "选择一个标签修改或删除。"; await ReloadAsync((TagList.SelectedItem as TagRecord)?.Id); });
    private void Selection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (NameBox is null || ColorPicker is null) return;
        var tag = TagList.SelectedItem as TagRecord; NameBox.Text = tag?.Name ?? ""; ColorPicker.SelectedValue = tag?.Color ?? "blue";
    }
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (busy || TagList.SelectedItem is not TagRecord tag) { Notice.Text = "请先选择标签。"; return; }
        var name = NameBox.Text.Trim();
        if (name.Length == 0 || name.EnumerateRunes().Count() > 40) { Notice.Text = "标签名称需为 1～40 个字符。"; return; }
        await RunAsync(async () => { await api.UpdateTagAsync(tag.Id, name, (string)ColorPicker.SelectedValue); await ReloadAsync(tag.Id); Notice.Text = "标签已更新，所有关联帐号同步使用新名称和颜色。"; });
    }
    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (busy || TagList.SelectedItem is not TagRecord tag) { Notice.Text = "请先选择标签。"; return; }
        // Refresh usage count immediately before confirmation.
        TagRecord? latest = null;
        await RunAsync(async () => { latest = (await api.TagsAsync()).FirstOrDefault(t => t.Id == tag.Id); });
        if (SessionExpired || latest is null) return;
        if (System.Windows.MessageBox.Show(this, $"删除标签“{latest.Name}”？\n当前有 {latest.AccountCount} 个帐号使用此标签。\n仅移除标签关联，帐号和分享授权不会删除。", "确认删除标签", System.Windows.MessageBoxButton.YesNo, MessageBoxImage.Question, System.Windows.MessageBoxResult.No) != System.Windows.MessageBoxResult.Yes) return;
        await RunAsync(async () => { await api.DeleteTagAsync(tag.Id); await ReloadAsync(); Notice.Text = "标签已删除，帐号和分享授权保留。"; });
    }
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();
    private void Close_Click(object sender, RoutedEventArgs e) { if (!busy) Close(); }
}
