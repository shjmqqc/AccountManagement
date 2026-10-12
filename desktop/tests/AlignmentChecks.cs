using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using AccountManagement.Models;
using AccountManagement.Services;
using AccountManagement.Views;

internal static class AlignmentChecks
{
    internal static async Task Run(Action<bool, string> check, Action<Window, string, int, int> capture)
    {
        using var api = new ApiClient();
        var window = new MainWindow(api, new UserRecord("preview", "preview", "user"), false);
        for (int i = 0; i < 24; i++) window.ViewModel.Accounts.Add(new AccountRecord("preview-" + i, "p", "测试帐号 " + i, "admin", "", PlatformName: "测试平台"));
        window.Show();
        try
        {
            await Task.Delay(350);
            foreach (bool details in new[] { true, false })
            {
                window.SetDetailsVisible(details);
                capture(window, details ? "alignment" : "alignment-wide", 1320, 820);
                var table = (DataGrid)window.FindName("AccountList");
                var rows = Enumerable.Range(0, 4).Select(i => (DataGridRow)table.ItemContainerGenerator.ContainerFromIndex(i)).ToArray();
                var headers = Children<DataGridColumnHeader>(table).Where(h => h.Column is not null).OrderBy(h => h.Column.DisplayIndex).ToArray();
                for (int column = 0; column < 3; column++)
                {
                    var positions = rows.Select(row => table.Columns[column].GetCellContent(row).TranslatePoint(new Point(), table).X).ToArray();
                    check(positions.Max() - positions.Min() < 0.5, $"Adjacent rows align in column {column}: {string.Join(",", positions)}");
                    double headerX = headers[column].TranslatePoint(new Point(headers[column].Padding.Left, 0), table).X;
                    check(Math.Abs(positions[0] - headerX) < 0.5, $"Header aligns in column {column}");
                }
                table.SelectedIndex = 1;
                table.UpdateLayout();
                check(Math.Abs(table.Columns[0].GetCellContent(rows[0]).TranslatePoint(new Point(), table).X - table.Columns[0].GetCellContent(rows[1]).TranslatePoint(new Point(), table).X) < 0.5, "Selection does not indent rows");
            }
        }
        finally { window.Close(); }
    }

    private static IEnumerable<T> Children<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var descendant in Children<T>(child)) yield return descendant;
        }
    }
}
