using System.ComponentModel;
using System.Text;
using System.Windows;
using AccountManagement.Models;
using AccountManagement.Services;
using Wpf.Ui.Controls;

namespace AccountManagement.Views;

public enum EditorKind { Platform, Account, ChangePassword }

public partial class EditorWindow : FluentWindow
{
    private readonly ApiClient api;
    private readonly EditorKind kind;
    private readonly PlatformRecord? platform;
    private readonly AccountRecord? account;
    private bool saving;
    private bool tagsLoaded;
    private readonly System.Collections.ObjectModel.ObservableCollection<TagRecord> tags = [];
    public bool Saved { get; private set; }
    public bool SessionExpired { get; private set; }
    public string? SavedId { get; private set; }

    public EditorWindow(ApiClient api, EditorKind kind, IEnumerable<PlatformRecord>? platforms = null,
        PlatformRecord? platform = null, AccountRecord? account = null)
    {
        InitializeComponent(); this.api = api; this.kind = kind; this.platform = platform; this.account = account;
        Title = kind switch { EditorKind.Platform => platform is null ? "新增平台" : "编辑平台", EditorKind.Account => account is null ? "新增帐号" : "编辑帐号", _ => "修改登录密码" };
        Heading.Text = Title; WindowTitle.Title = Title;
        if (kind == EditorKind.Platform)
        {
            PlatformPanel.Visibility = Visibility.Visible; Height = 400;
            PlatformNameBox.Text = platform?.Name ?? ""; UrlBox.Text = platform?.Url ?? "";
        }
        else if (kind == EditorKind.Account)
        {
            AccountPanel.Visibility = Visibility.Visible;
            PlatformPicker.ItemsSource = platforms?.Where(p => p.Id.Length > 0).ToList();
            PlatformPicker.SelectedValue = account?.PlatformId ?? platform?.Id;
            if (PlatformPicker.SelectedIndex < 0) PlatformPicker.SelectedIndex = 0;
            AccountNameBox.Text = account?.Name ?? ""; LoginBox.Text = account?.Login ?? ""; NotesBox.Text = account?.Notes ?? "";
            ReplacePassword.Visibility = account is null ? Visibility.Collapsed : Visibility.Visible;
            AccountPassword.IsEnabled = account is null;
            if (account is not null) PasswordHint.Text = "未勾选时保留原密码，不会读取或回填原密码。";
            TagPicker.ItemsSource = tags;
            TagColorPicker.ItemsSource = new[] {
                new TagRecord("", "蓝色", "blue"), new TagRecord("", "绿色", "green"),
                new TagRecord("", "橙色", "orange"), new TagRecord("", "红色", "red"),
                new TagRecord("", "紫色", "purple"), new TagRecord("", "灰色", "gray") };
            TagColorPicker.SelectedIndex = 0;
            Loaded += async (_, _) => await LoadTagsAsync();
        }
        else { ChangePasswordPanel.Visibility = Visibility.Visible; Height = 540; }
        Closing += OnClosing;
        Closed += (_, _) => { AccountPassword.Clear(); OldPassword.Clear(); NewPassword.Clear(); ConfirmPassword.Clear(); };
    }

    private void ReplacePassword_Changed(object sender, RoutedEventArgs e)
    {
        if (AccountPassword is null) return;
        AccountPassword.IsEnabled = ReplacePassword.IsChecked == true;
        if (!AccountPassword.IsEnabled) AccountPassword.Clear();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
    public async Task LoadTagsAsync()
    {
        if (saving || tagsLoaded) return;
        saving = true; Fields.IsEnabled = false; SaveButton.IsEnabled = false;
        try
        {
            var records = await api.TagsAsync();
            tags.Clear(); foreach (var tag in records) tags.Add(tag);
            foreach (var tag in tags.Where(t => account?.Tags.Any(a => a.Id == t.Id) == true)) TagPicker.SelectedItems.Add(tag);
            tagsLoaded = true;
        }
        catch (ApiException error) { ErrorText.Text = error.Message; SessionExpired = error.Status == 401 || error.Code == "password_change_required"; }
        finally { saving = false; Fields.IsEnabled = true; SaveButton.IsEnabled = true; }
        if (SessionExpired && IsVisible) Close();
    }
    private async void CreateTag_Click(object sender, RoutedEventArgs e)
    {
        if (saving) return;
        if (string.IsNullOrWhiteSpace(TagNameBox.Text)) { ErrorText.Text = "请输入标签名称。"; return; }
        if (!tagsLoaded) { await LoadTagsAsync(); if (!tagsLoaded) return; }
        saving = true; Fields.IsEnabled = false; SaveButton.IsEnabled = false;
        try
        {
            var tag = await api.CreateTagAsync(TagNameBox.Text.Trim(), (string)TagColorPicker.SelectedValue);
            var existing = tags.FirstOrDefault(t => t.Id == tag.Id);
            if (existing is null) { tags.Add(tag); existing = tag; }
            if (!TagPicker.SelectedItems.Contains(existing)) TagPicker.SelectedItems.Add(existing);
            TagNameBox.Clear(); ErrorText.Text = "标签已创建或复用；点击保存应用到帐号。取消表单仍保留标签库中的标签。";
        }
        catch (ApiException error) { ErrorText.Text = error.Message; SessionExpired = error.Status == 401 || error.Code == "password_change_required"; }
        finally { saving = false; Fields.IsEnabled = true; SaveButton.IsEnabled = true; }
        if (SessionExpired) Close();
    }
    private void OnClosing(object? sender, CancelEventArgs e) { if (saving) e.Cancel = true; }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (saving) return;
        try
        {
            if (kind == EditorKind.Platform && string.IsNullOrWhiteSpace(PlatformNameBox.Text)) throw new ArgumentException("请填写平台名称。");
            if (kind == EditorKind.Account && (PlatformPicker.SelectedItem is null || string.IsNullOrWhiteSpace(LoginBox.Text))) throw new ArgumentException("请选择平台并填写登录帐号。");
            if (kind == EditorKind.Account && !tagsLoaded) { await LoadTagsAsync(); if (!tagsLoaded) return; }
            if (kind == EditorKind.Account && TagPicker.SelectedItems.Count > 20) throw new ArgumentException("每条帐号最多选择 20 个标签。");
            if (kind == EditorKind.ChangePassword)
            {
                if (OldPassword.Password.Length == 0) throw new ArgumentException("请填写原登录密码。");
                if (NewPassword.Password.EnumerateRunes().Count() < 6) throw new ArgumentException("新登录密码至少需要 6 位。");
                if (NewPassword.Password != ConfirmPassword.Password) throw new ArgumentException("两次输入的新密码不一致。");
            }
            saving = true; Fields.IsEnabled = false; SaveButton.IsEnabled = false; CancelButton.IsEnabled = false; ErrorText.Text = "正在保存…";
            if (kind == EditorKind.Platform) SavedId = await api.SavePlatformAsync(platform, PlatformNameBox.Text.Trim(), UrlBox.Text.Trim());
            else if (kind == EditorKind.Account)
                SavedId = await api.SaveAccountAsync(account, ((PlatformRecord)PlatformPicker.SelectedItem).Id, AccountNameBox.Text,
                    LoginBox.Text.Trim(), NotesBox.Text, account is null || ReplacePassword.IsChecked == true ? AccountPassword.Password : null,
                    TagPicker.SelectedItems.Cast<TagRecord>().Select(t => t.Id));
            else await api.ChangePasswordAsync(OldPassword.Password, NewPassword.Password);
            Saved = true; saving = false; DialogResult = true;
        }
        catch (ApiException error)
        {
            ErrorText.Text = error.Message;
            if (error.Status == 401 || error.Code == "password_change_required" && kind != EditorKind.ChangePassword)
            { SessionExpired = true; saving = false; Close(); }
        }
        catch (ArgumentException error) { ErrorText.Text = error.Message; }
        finally { saving = false; Fields.IsEnabled = true; SaveButton.IsEnabled = true; CancelButton.IsEnabled = true; }
    }
}
