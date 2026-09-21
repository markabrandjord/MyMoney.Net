// Source/WPF/MyMoney.Shell/Views/AccountsView.xaml.cs
using System.Windows.Controls;
using MyMoney.Shell.ViewModels;

namespace MyMoney.Shell.Views;

public partial class AccountsView : UserControl
{
    public AccountsView(AccountsListViewModel viewModel)
    {
        InitializeComponent();
        this.DataContext = viewModel;
    }
}
