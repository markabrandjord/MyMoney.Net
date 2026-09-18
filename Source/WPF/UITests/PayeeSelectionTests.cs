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
using Walkabout.Data;

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

            // Issue #32 made File | Open a picker over the DatabaseRegistry,
            // not a free-text file browser (see docs/superpowers/specs/
            // 2026-09-17-database-registry-and-engine-aware-dialogs-design.md:
            // "File | Open -> a list of registry entries, not a file
            // browser"), so the fixture must be registered before it can be
            // opened this way. Registered/removed against the real default
            // registry path (there is no per-test override), cleaned up in
            // a finally block so this test doesn't leave a stray entry in
            // the developer's actual registry file.
            const string fixtureDisplayName = "PayeeSelectionTests Fixture";
            string registryPath = DatabaseRegistry.GetDefaultPath();
            var registry = DatabaseRegistry.Load(registryPath);
            registry.Databases[fixtureDisplayName] = new DatabaseEntry
            {
                Engine = DataEngineType.Sqlite,
                Path = fixturePath,
                TestDatabase = true
            };
            registry.Save();

            try
            {
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

                OpenFileViaFileMenu(app, mainWindow, fixtureDisplayName);
                RunAssertions(mainWindow);
            }
            finally
            {
                var cleanup = DatabaseRegistry.Load(registryPath);
                cleanup.Databases.Remove(fixtureDisplayName);
                cleanup.Save();
            }
        }

        private void RunAssertions(Window mainWindow)
        {

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

        private static void OpenFileViaFileMenu(Application app, Window mainWindow, string displayName)
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
                () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("MenuFileOpen")),
                TimeSpan.FromSeconds(5)).Result;
            Assert.That(openItem, Is.Not.Null, "'Open...' menu item not found after expanding the File menu.");
            openItem.Patterns.Invoke.Pattern.Invoke();

            // The dialog is a separate top-level window belonging to the same
            // process, not a descendant of mainWindow.
            Window openDialog = Retry.WhileNull(() =>
                mainWindow.ModalWindows.FirstOrDefault(),
                TimeSpan.FromSeconds(5)).Result;
            Assert.That(openDialog, Is.Not.Null, "Open Database dialog did not appear.");
            Assert.That(openDialog.Title, Is.EqualTo("Open Database"),
                $"Expected the app's registry-backed 'Open Database' dialog (issue #32), but found a different modal window instead " +
                $"(Name: '{openDialog.Name}'). This may be an unexpected dialog, e.g. a 'Save Changes?' confirmation.");

            // Issue #32's Open Database dialog is a picker over the registered
            // databases (a ListBox), not a free-text file path box -- see the
            // comment at this method's call site.
            var listBox = openDialog.FindFirstDescendant(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.List)).AsListBox();
            Assert.That(listBox.Items.Any(i => i.Text == displayName), Is.True, $"'{displayName}' not listed in Open Database dialog.");
            listBox.Select(displayName);

            AutomationElement openButton = Retry.WhileNull(
                () => openDialog.FindFirstDescendant(cf => cf.ByAutomationId("ButtonOk")),
                TimeSpan.FromSeconds(5)).Result;
            Assert.That(openButton, Is.Not.Null, "Open button not found in Open dialog.");
            openButton.Patterns.Invoke.Pattern.Invoke();
        }
    }
}
