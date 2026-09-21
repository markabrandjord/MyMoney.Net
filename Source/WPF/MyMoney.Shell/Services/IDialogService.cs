using System.Threading.Tasks;
using System.Windows;

namespace MyMoney.Shell.Services;

public interface IDialogService
{
    Task<DialogOutcome> ShowAsync(FrameworkElement content, string title, string primaryButtonText);
}
