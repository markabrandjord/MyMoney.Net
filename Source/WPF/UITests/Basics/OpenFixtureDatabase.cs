using System;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using NUnit.Framework;

namespace Walkabout.UITests.Basics
{
    internal static class OpenFixtureDatabase
    {
        internal static void Open(Window mainWindow, string registeredName)
        {
            AutomationElement fileMenu = Retry.WhileNull(
                () => mainWindow.FindFirstDescendant(cf => cf.ByName("File")),
                TimeSpan.FromSeconds(5)).Result;
            Assert.That(fileMenu, Is.Not.Null, "File menu not found.");
            fileMenu.Patterns.ExpandCollapse.Pattern.Expand();
            Wait.UntilInputIsProcessed();

            AutomationElement openItem = Retry.WhileNull(
                () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("MenuFileOpen")),
                TimeSpan.FromSeconds(5)).Result;
            Assert.That(openItem, Is.Not.Null, "'Open...' menu item not found after expanding the File menu.");
            openItem.Patterns.Invoke.Pattern.Invoke();

            Window openDialog = Retry.WhileNull(() =>
                mainWindow.ModalWindows.Length > 0 ? mainWindow.ModalWindows[0] : null,
                TimeSpan.FromSeconds(5)).Result;
            Assert.That(openDialog, Is.Not.Null, "Open Database dialog did not appear.");
            Assert.That(openDialog.Title, Is.EqualTo("Open Database"),
                $"Expected the app's registry-backed 'Open Database' dialog, but found a different modal window instead (Name: '{openDialog.Name}').");

            var listBox = openDialog.FindFirstDescendant(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.List)).AsListBox();
            Assert.That(listBox.Items.Length, Is.GreaterThan(0), $"Open Database dialog's list is empty - '{registeredName}' was not found.");
            listBox.Select(registeredName);

            AutomationElement openButton = Retry.WhileNull(
                () => openDialog.FindFirstDescendant(cf => cf.ByAutomationId("ButtonOk")),
                TimeSpan.FromSeconds(5)).Result;
            Assert.That(openButton, Is.Not.Null, "Open button not found in Open dialog.");
            openButton.Patterns.Invoke.Pattern.Invoke();

            Retry.WhileFalse(
                () => mainWindow.Title.IndexOf(registeredName, StringComparison.OrdinalIgnoreCase) >= 0,
                TimeSpan.FromSeconds(10));
        }
    }
}
