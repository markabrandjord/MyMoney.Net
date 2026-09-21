// Source/WPF/MyMoney.Shell/Views/DeleteAccountView.xaml.cs
using System.Windows.Controls;
using MyMoney.Shell.ViewModels;

namespace MyMoney.Shell.Views;

public partial class DeleteAccountView : UserControl
{
    public DeleteAccountView(DeleteAccountViewModel viewModel)
    {
        this.Resources.Add("NullToCollapsedConverter", new NullToCollapsedConverter());
        InitializeComponent();
        this.DataContext = viewModel;
    }
}
