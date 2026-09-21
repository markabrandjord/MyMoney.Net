// Source/WPF/MyMoney.Shell/Views/AddAccountView.xaml.cs
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using MyMoney.Shell.ViewModels;
using Walkabout.Data;

namespace MyMoney.Shell.Views;

public sealed class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public partial class AddAccountView : UserControl
{
    public AddAccountView(AddAccountViewModel viewModel)
    {
        this.Resources.Add("NullToCollapsedConverter", new NullToCollapsedConverter());
        InitializeComponent();
        this.DataContext = viewModel;
        this.TypeComboBox.ItemsSource = Enum.GetValues<AccountType>();
    }
}
