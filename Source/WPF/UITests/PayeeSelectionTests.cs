using System;
using System.IO;
using System.Linq;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.UIA3;
using NUnit.Framework;

namespace Walkabout.UITests
{
    public class PayeeSelectionTests
    {
        private Application app;
        private UIA3Automation automation;

        [TearDown]
        public void TearDown()
        {
            try
            {
                app?.Close();
            }
            catch
            {
                // best-effort cleanup; a test failure shouldn't be masked by a teardown exception
            }
            app?.Dispose();
            automation?.Dispose();
        }

        [Test]
        public void SelectingAPayee_AfterOpeningFixtureFile_DoesNotCrash()
        {
            string fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "PayeeSmokeTest.mmdb");
            Assert.That(File.Exists(fixturePath), Is.True,
                $"Fixture not found at {fixturePath}. Run Task 2's FixtureGenerator first.");

            // /nosettings prevents the app from auto-loading the developer's real,
            // persisted database on launch and from writing its settings back out
            // on close. Without it, this test would (a) overwrite the developer's
            // actual application settings file with the fixture's path, and (b) on
            // subsequent runs, start with the fixture already loaded from the prior
            // run's saved settings - defeating the point of exercising the File->Open
            // dialog below.
            app = Application.Launch(LaunchSmokeTests.MyMoneyExePath, "/nosettings");
            automation = new UIA3Automation();
            Window mainWindow = app.GetMainWindow(automation, TimeSpan.FromSeconds(10));

            OpenFileViaFileMenu(app, mainWindow, fixturePath);

            // The window title reflects the opened file once loading completes.
            Retry.WhileFalse(
                () => mainWindow.Title.IndexOf("PayeeSmokeTest", StringComparison.OrdinalIgnoreCase) >= 0,
                TimeSpan.FromSeconds(10));
            Assert.That(mainWindow.Title, Does.Contain("PayeeSmokeTest").IgnoreCase);

            AutomationElement payeesHeader = mainWindow.FindFirstDescendant(cf => cf.ByName("PAYEES"));
            Assert.That(payeesHeader, Is.Not.Null, "PAYEES section header not found.");
            AutomationElement expander = payeesHeader.Parent;
            Assert.That(expander, Is.Not.Null,
                "PAYEES header has no parent element; expected an expander/toggle button here.");
            Assert.That(expander.Patterns.Toggle.IsSupported, Is.True,
                "PAYEES header's parent element does not support the Toggle pattern - " +
                "expected it to be the section's expander button.");
            var togglePattern = expander.Patterns.Toggle.Pattern;
            if (togglePattern.ToggleState.Value == ToggleState.Off)
            {
                togglePattern.Toggle();
            }

            AutomationElement payeeRow = Retry.WhileNull(
                () => mainWindow.FindFirstDescendant(cf => cf.ByName("Payee Smoke Test")),
                TimeSpan.FromSeconds(5)).Result;
            Assert.That(payeeRow, Is.Not.Null, "Fixture payee row not found after expanding PAYEES.");

            payeeRow.Click();
            Wait.UntilInputIsProcessed();

            Assert.That(app.HasExited, Is.False, "App exited unexpectedly after selecting the payee.");

            AutomationElement[] topLevelWindows = automation.GetDesktop().FindAllChildren(
                cf => cf.ByProcessId(app.ProcessId));
            bool errorDialogPresent = topLevelWindows.Any(w =>
                w.Name.IndexOf("Unhandled", StringComparison.OrdinalIgnoreCase) >= 0 ||
                w.Name.IndexOf("Exception", StringComparison.OrdinalIgnoreCase) >= 0);
            Assert.That(errorDialogPresent, Is.False, "An unhandled-exception dialog appeared after selecting the payee.");
        }

        private static void OpenFileViaFileMenu(Application app, Window mainWindow, string filePath)
        {
            // FindFirstDescendant right after Expand() races WPF's popup layout/render pass:
            // Wait.UntilInputIsProcessed() only pumps the input queue, it does not wait for the
            // menu's automation tree to actually populate - this was the root cause of issue #26's
            // intermittent NullReferenceException here (openItem null because the dropdown hadn't
            // rendered yet). Retry.WhileNull matches every other "wait for async UI change" lookup
            // already used elsewhere in this file (payeeRow, openDialog, the window-title check).
            AutomationElement fileMenu = Retry.WhileNull(
                () => mainWindow.FindFirstDescendant(cf => cf.ByName("File")),
                TimeSpan.FromSeconds(5)).Result;
            Assert.That(fileMenu, Is.Not.Null, "File menu not found.");
            fileMenu.Patterns.ExpandCollapse.Pattern.Expand();
            Wait.UntilInputIsProcessed();

            AutomationElement openItem = Retry.WhileNull(
                () => mainWindow.FindFirstDescendant(cf => cf.ByName("Open...")),
                TimeSpan.FromSeconds(5)).Result;
            Assert.That(openItem, Is.Not.Null, "'Open...' menu item not found after expanding the File menu.");
            openItem.Patterns.Invoke.Pattern.Invoke();

            // The dialog is a separate top-level window belonging to the same
            // process, not a descendant of mainWindow. The fallback below is
            // scoped to this app's own process ID and the dialog's known
            // AutomationId, so it can never match an unrelated window on the
            // developer's desktop.
            Window openDialog = Retry.WhileNull(() =>
                mainWindow.ModalWindows.FirstOrDefault() ??
                mainWindow.Automation.GetDesktop()
                    .FindFirstChild(cf => cf.ByProcessId(app.ProcessId).And(cf.ByAutomationId("CreateDatabaseDialog")))
                    ?.AsWindow(),
                TimeSpan.FromSeconds(5)).Result;
            Assert.That(openDialog, Is.Not.Null, "Open file dialog did not appear.");
            Assert.That(openDialog.AutomationId, Is.EqualTo("CreateDatabaseDialog"),
                "Expected the app's 'Connect Database' dialog (AutomationId 'CreateDatabaseDialog') to appear, " +
                $"but found a different modal window instead (Name: '{openDialog.Name}', AutomationId: '{openDialog.AutomationId}'). " +
                "This may be an unexpected dialog, e.g. a 'Save Changes?' confirmation.");

            // "Open..." does not launch the native Windows common file dialog -
            // confirmed by dumping the actual automation tree during development.
            // It opens the app's own custom WPF "Connect Database" dialog
            // (AutomationId "CreateDatabaseDialog") with a path text box
            // (AutomationId "TextBoxFile") and an "Open" button
            // (AutomationId "ButtonCreate", Name "Open").
            AutomationElement fileNameBox = Retry.WhileNull(
                () => openDialog.FindFirstDescendant(cf => cf.ByAutomationId("TextBoxFile")),
                TimeSpan.FromSeconds(5)).Result;
            Assert.That(fileNameBox, Is.Not.Null, "File name box not found in Open dialog.");
            fileNameBox.Patterns.Value.Pattern.SetValue(filePath);

            AutomationElement openButton = Retry.WhileNull(
                () => openDialog.FindFirstDescendant(cf => cf.ByAutomationId("ButtonCreate")),
                TimeSpan.FromSeconds(5)).Result;
            Assert.That(openButton, Is.Not.Null, "Open button not found in Open dialog.");
            openButton.Patterns.Invoke.Pattern.Invoke();
        }
    }
}
