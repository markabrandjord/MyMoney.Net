using System.Windows;
using Walkabout.Utilities;

namespace Walkabout.Data
{
    public class WpfDataLayerUiCallback : IDataLayerUiCallback
    {
        public void ShowWarning(string message, string title)
        {
            MessageBoxEx.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
