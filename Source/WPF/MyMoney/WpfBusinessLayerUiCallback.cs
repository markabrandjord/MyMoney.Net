using System.Windows;
using Walkabout.Data;
using Walkabout.Utilities;

namespace Walkabout
{
    public class WpfBusinessLayerUiCallback : IBusinessLayerUiCallback
    {
        public void ShowError(string message, string title)
        {
            MessageBoxEx.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        public bool Confirm(string message, string title)
        {
            return MessageBoxEx.Show(message, title, MessageBoxButton.YesNo) == MessageBoxResult.Yes;
        }

        public bool ConfirmOkCancel(string message, string title)
        {
            return MessageBoxEx.Show(message, title, MessageBoxButton.OKCancel, MessageBoxImage.Exclamation) == MessageBoxResult.OK;
        }
    }
}
