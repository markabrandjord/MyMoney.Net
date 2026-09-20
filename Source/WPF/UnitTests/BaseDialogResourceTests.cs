using System;
using System.Windows;
using System.Windows.Media;
using NUnit.Framework;
using Walkabout.Dialogs;

namespace Walkabout.Tests
{
    [TestFixture]
    public class BaseDialogResourceTests
    {
        [Test, Apartment(System.Threading.ApartmentState.STA)]
        public void BaseDialog_BackgroundAndForeground_ResolveToRealBrushes()
        {
            var app = Application.Current ?? new Application();
            app.Resources.MergedDictionaries.Clear();
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/Wpf.Ui;component/Resources/Theme/Light.xaml")
            });
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("/MyMoney;component/Themes/DialogResources.xaml", UriKind.Relative)
            });

            var dialog = new BaseDialog();

            Assert.That(dialog.Background, Is.Not.Null, "BaseDialog.Background resolved to null - DialogBackgroundBrush is missing.");
            Assert.That(dialog.Background, Is.InstanceOf<SolidColorBrush>());
            Assert.That(dialog.Foreground, Is.Not.Null, "BaseDialog.Foreground resolved to null - DialogForegroundBrush is missing.");
            Assert.That(dialog.Foreground, Is.InstanceOf<SolidColorBrush>());
        }
    }
}
