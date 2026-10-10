using System.Windows;
using System.Windows.Controls;

namespace AccountManagement.Views;

// Secrets are transient dialog content, never saved to settings or a list model.
public sealed class SecretWindow : Window
{
    public SecretWindow(string title, string secret, string instruction)
    {
        Title = title; Width = 580; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei UI");
        var root = new Grid { Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(247, 249, 252)) }; Content = root;
        var panel = new StackPanel { Margin = new Thickness(26) }; root.Children.Add(panel);
        panel.Children.Add(new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = instruction, Margin = new Thickness(0, 14, 0, 16), TextWrapping = TextWrapping.Wrap });
        var value = new TextBox { Text = secret, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, FontSize = 18, Padding = new Thickness(12) };
        panel.Children.Add(value);
        var status = new TextBlock { Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap }; panel.Children.Add(status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) }; panel.Children.Add(buttons);
        var copy = new Button { Content = "复制", Padding = new Thickness(18, 7, 18, 7) }; buttons.Children.Add(copy);
        copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(value.Text); status.Text = "已复制，请妥善交给本人。"; }
            catch (System.Runtime.InteropServices.ExternalException) { status.Text = "剪贴板暂不可用，请重试或手动选择复制。"; }
        };
        var close = new Button { Content = "关闭", Margin = new Thickness(12, 0, 0, 0), Padding = new Thickness(18, 7, 18, 7) }; buttons.Children.Add(close);
        close.Click += (_, _) => Close();
        Closed += (_, _) => value.Clear();
    }
}
