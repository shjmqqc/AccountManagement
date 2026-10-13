using System.Globalization;
using System.Windows.Data;

namespace AccountManagement.Views;

public sealed class AccountCheckedConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length == 2 && values[0] is string id && values[1] is HashSet<string> ids && ids.Contains(id);
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
