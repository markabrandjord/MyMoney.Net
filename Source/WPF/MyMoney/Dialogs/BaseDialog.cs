using System.Windows;

namespace Walkabout.Dialogs
{
    public class BaseDialog : Window
    {
        public BaseDialog()
        {
            this.SetResourceReference(Window.BackgroundProperty, "DialogBackgroundBrush");
            this.SetResourceReference(Window.ForegroundProperty, "DialogForegroundBrush");
        }
    }
}
