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

            // If a database is already open in this (shared, cross-test) app session and
            // MainWindow.dirty is true, File|Open shows a YesNoCancel "Save Changes" prompt
            // (MainWindow.xaml.cs's SaveIfDirty) BEFORE the real "Open Database" dialog. Observed
            // in practice even when no test explicitly edited anything - e.g. a Categories panel
            // being shown/populated for the first time in a session was enough to flip dirty.
            // Discard rather than save, since these are throwaway scratch databases.
            if (openDialog.Title == "Save Changes")
            {
                AutomationElement noButton = Retry.WhileNull(
                    () => openDialog.FindFirstDescendant(cf => cf.ByAutomationId("ButtonNo")),
                    TimeSpan.FromSeconds(5)).Result;
                Assert.That(noButton, Is.Not.Null, "'Save Changes' prompt's No button not found.");
                noButton.Patterns.Invoke.Pattern.Invoke();

                openDialog = Retry.WhileNull(() =>
                    mainWindow.ModalWindows.Length > 0 ? mainWindow.ModalWindows[0] : null,
                    TimeSpan.FromSeconds(5)).Result;
                Assert.That(openDialog, Is.Not.Null, "Open Database dialog did not appear after dismissing 'Save Changes'.");
            }

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

        /// <summary>
        /// Expands one of the left-hand toolbox sections (MainWindow.xaml.cs's
        /// toolBox.Add("...", sectionAutomationId, ...) - "AccountsSelector",
        /// "CategoriesSelector", "PayeesSelector", "SecuritiesSelector") so its content is in the
        /// UIA tree at all. Confirmed by dumping the automation tree right after Open() with
        /// nothing selected: none of these sections' contents (e.g. the Accounts panel's account
        /// ListItems) are even present until the section is expanded. Each section is a real WPF
        /// Expander (Controls/Accordion.xaml.cs's Add() builds one per section) - its automation
        /// peer is exposed as ControlType.Group and natively supports the ExpandCollapse pattern;
        /// the nested "HeaderSite" element is just the Expander's default-template toggle button,
        /// and clicking it directly proved unreliable (in one run it never actually expanded
        /// Categories, while Accounts happened to work) - call ExpandCollapse.Pattern.Expand() on
        /// the section itself instead. The BasicsAppSession app/window is reused across every
        /// test in a fixture (one launch per [SetUpFixture]), so a section expanded by an earlier
        /// test stays expanded - probeForContent lets each caller check whether its own content
        /// is already visible before expanding, since Accordion.Selected suggests only one
        /// section may be expanded at a time (re-expanding an already-expanded one is harmless
        /// either way, but this avoids the redundant call and its settle delay).
        /// </summary>
        internal static void EnsureToolboxSectionExpanded(Window mainWindow, string sectionAutomationId, Func<AutomationElement, AutomationElement> probeForContent)
        {
            AutomationElement section = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId(sectionAutomationId));
            Assert.That(section, Is.Not.Null, $"'{sectionAutomationId}' section not found.");

            if (Retry.WhileNull(() => probeForContent(section), TimeSpan.FromSeconds(1)).Result != null)
            {
                return;
            }

            section.Patterns.ExpandCollapse.Pattern.Expand();
            Wait.UntilInputIsProcessed();

            Retry.WhileNull(() => probeForContent(section), TimeSpan.FromSeconds(5));
        }

        /// <summary>
        /// Selects an account in the left Accounts panel so its transaction register renders in
        /// the grid. Opening a database does NOT auto-select an account or show any register.
        /// </summary>
        internal static void SelectAccount(Window mainWindow, string accountName)
        {
            EnsureToolboxSectionExpanded(mainWindow, "AccountsSelector",
                section => section.FindFirstDescendant(cf => cf.ByAutomationId(accountName)));

            AutomationElement accountItem = Retry.WhileNull(
                () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId(accountName)),
                TimeSpan.FromSeconds(5)).Result;
            Assert.That(accountItem, Is.Not.Null, $"Account '{accountName}' not found in the Accounts panel.");
            accountItem.Patterns.SelectionItem.Pattern.Select();
            Wait.UntilInputIsProcessed();
        }

        /// <summary>
        /// Expands the left Categories panel so its TreeView (x:Name="treeView" in
        /// CategoriesControl.xaml) is populated in the UIA tree. Category tree items have no
        /// explicit AutomationId (HierarchicalDataTemplate bound to Category with no x:Name) -
        /// their automation Name falls back to the bound TextBlock's Text, i.e. Category.Label
        /// (the leaf segment, e.g. "Movies" for "Fun:Movies" - see the Category.Name-vs-Label
        /// gotcha in this repo's CLAUDE.md), found via ByControlType(TreeItem) + ByName(label).
        /// </summary>
        internal static void ExpandCategoriesPanel(Window mainWindow)
        {
            EnsureToolboxSectionExpanded(mainWindow, "CategoriesSelector",
                section => section.FindFirstDescendant(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.TreeItem)));
        }
    }
}
