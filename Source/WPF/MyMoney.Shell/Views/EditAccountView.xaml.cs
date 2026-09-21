// Source/WPF/MyMoney.Shell/Views/EditAccountView.xaml.cs
using System;
using System.Windows.Controls;
using MyMoney.Shell.ViewModels;
using Walkabout.Data;

namespace MyMoney.Shell.Views;

public partial class EditAccountView : UserControl
{
    public EditAccountView(EditAccountViewModel viewModel)
    {
        this.Resources.Add("NullToCollapsedConverter", new NullToCollapsedConverter());
        InitializeComponent();
        // Same ordering reason as AddAccountView: ItemsSource before DataContext, so
        // SelectedItem="{Binding Type}" has something to select from the moment the binding
        // first evaluates.
        this.TypeComboBox.ItemsSource = Enum.GetValues<AccountType>();
        this.DataContext = viewModel;
    }
}
