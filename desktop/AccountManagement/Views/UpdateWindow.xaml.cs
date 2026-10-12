using System.Windows;
using AccountManagement.ViewModels;
using Wpf.Ui.Controls;

namespace AccountManagement.Views;

public partial class UpdateWindow : FluentWindow
{
    private readonly UpdateViewModel model;
    public UpdateWindow(UpdateViewModel model)
    {
        InitializeComponent(); this.model = model; DataContext = model;
        Loaded += async (_, _) => await model.CheckAsync();
        Closing += (_, e) => { if (model.IsBusy) { model.CancelDownload(); e.Cancel = true; } };
    }
    private async void Check_Click(object sender, RoutedEventArgs e) => await model.CheckAsync();
    private async void Download_Click(object sender, RoutedEventArgs e) => await model.DownloadAsync();
    private void Cancel_Click(object sender, RoutedEventArgs e) => model.CancelDownload();
    private void Restart_Click(object sender, RoutedEventArgs e)
    {
        if (!model.CanRestart) return;
        if (Application.Current.Windows.Cast<Window>().Any(w => w.IsVisible && w != this && w != Owner))
        { model.Status = "请先保存并关闭其他窗口，再更新。"; return; }
        if (Owner is MainWindow main && main.ViewModel.IsBusy) { model.Status = "请等待当前操作完成。"; return; }
        if (System.Windows.MessageBox.Show(this, "现在关闭应用并安装更新？", "重启并更新",
            System.Windows.MessageBoxButton.YesNo, MessageBoxImage.Question, System.Windows.MessageBoxResult.No) != System.Windows.MessageBoxResult.Yes) return;
        if (model.PrepareRestart()) Application.Current.Shutdown();
    }
}
