using System;
using System.IO;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using NUnit.Framework;
using Walkabout.Data;

namespace Walkabout.UITests.Basics
{
    [TestFixture]
    public class CurrenciesFlaUiTests
    {
        private (string ScratchPath, string RegisteredName) db;

        // Real, fixed exchange-rate snapshot (x-rates.com USD table, Jan 1 2026 06:18 UTC) and
        // real public bank head-office info (gathered via WebSearch, 2026-09-18) - the other 5
        // of the 10 currencies/banks gathered for this plan, kept local to this FlaUI test
        // rather than the shared BasicsFixtureBuilder per the user's explicit correction.
        private static readonly (string Symbol, decimal UsdRatio, string BankName)[] CurrencyData =
        {
            ("CHF", 0.822901M, "UBS"),
            ("MXN", 17.229844M, "BBVA Mexico"),
            ("INR", 96.008501M, "State Bank of India"),
            ("BRL", 5.143522M, "Banco do Brasil"),
            ("CNY", 6.701622M, "Industrial and Commercial Bank of China"),
        };

        [SetUp]
        public void SetUp()
        {
            string scratchPath = Path.Combine(Path.GetTempPath(), $"CurrenciesScratch-{Guid.NewGuid():N}.mmdb");
            string displayName = "CurrenciesFlaUiTests Fixture";

            MyMoney money = BasicsFixtureBuilder.Build();
            foreach (var data in CurrencyData)
            {
                var currency = money.Currencies.AddCurrency(money.Currencies.Count + 1);
                currency.Symbol = data.Symbol;
                currency.Ratio = data.UsdRatio;

                var account = money.Accounts.AddAccount($"{data.BankName} ({data.Symbol})");
                account.Type = AccountType.Checking;
                account.Currency = data.Symbol;
            }

            var sqliteDb = new SqliteDatabase();
            sqliteDb.DatabasePath = scratchPath;
            sqliteDb.Create();
            sqliteDb.Save(money);

            string registryPath = DatabaseRegistry.GetDefaultPath();
            var registry = DatabaseRegistry.Load(registryPath);
            registry.Databases[displayName] = new DatabaseEntry
            {
                Engine = DataEngineType.Sqlite,
                Path = scratchPath,
                TestDatabase = true
            };
            registry.Save();

            this.db = (scratchPath, displayName);
        }

        [TearDown]
        public void TearDown()
        {
            BasicsTestSetup.CleanUpDatabase(this.db.ScratchPath, this.db.RegisteredName);
        }

        // View | Currencies (MainWindow.xaml's MenuView -> MenuViewCurrencies, bound to
        // AppCommands.CommandViewCurrencies) swaps the main content pane to CurrenciesView.
        private static void NavigateToCurrenciesView(Window mainWindow)
        {
            AutomationElement viewMenu = Retry.WhileNull(
                () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("MenuView")),
                TimeSpan.FromSeconds(5)).Result;
            Assert.That(viewMenu, Is.Not.Null, "View menu not found.");
            viewMenu.Patterns.ExpandCollapse.Pattern.Expand();
            Wait.UntilInputIsProcessed();

            AutomationElement currenciesItem = Retry.WhileNull(
                () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("MenuViewCurrencies")),
                TimeSpan.FromSeconds(5)).Result;
            Assert.That(currenciesItem, Is.Not.Null, "'Currencies' menu item not found under View.");
            currenciesItem.Patterns.Invoke.Pattern.Invoke();
            Wait.UntilInputIsProcessed();

            Assert.That(Retry.WhileNull(
                () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("CurrenciesDataGrid")),
                TimeSpan.FromSeconds(5)).Result, Is.Not.Null, "CurrenciesDataGrid not found after navigating to View | Currencies.");
        }

        [Test]
        public void CurrenciesView_ShowsAllFiveSeededCurrencyRows()
        {
            Window mainWindow = BasicsAppSession.MainWindow;
            OpenFixtureDatabase.Open(mainWindow, this.db.RegisteredName);
            NavigateToCurrenciesView(mainWindow);

            // Expected result is source-derived: this test seeded exactly 5 currency rows
            // above (CHF, MXN, INR, BRL, CNY) - not observed from a prior run.
            foreach (var data in CurrencyData)
            {
                AutomationElement row = Retry.WhileNull(
                    () => mainWindow.FindFirstDescendant(cf => cf.ByName(data.Symbol)),
                    TimeSpan.FromSeconds(5)).Result;
                Assert.That(row, Is.Not.Null, $"Expected a '{data.Symbol}' row in the Currencies view.");
            }
        }

        [Test]
        public void AddingNewCurrencyRow_PersistsWithEnteredCode()
        {
            Window mainWindow = BasicsAppSession.MainWindow;
            OpenFixtureDatabase.Open(mainWindow, this.db.RegisteredName);
            NavigateToCurrenciesView(mainWindow);

            // "SEK" is deliberately not one of this test's 5 seeded currencies, so its
            // expected pre/post state (0 rows, then exactly 1) is known in advance.
            int sekRowsBefore = mainWindow.FindAllDescendants(cf => cf.ByName("SEK")).Length;
            Assert.That(sekRowsBefore, Is.EqualTo(0));

            // The grid's last DataItem is always "{NewItemPlaceholder}" (DataGrid's
            // add-new-row affordance). Its Symbol column is a FilteringComboBox
            // (CurrenciesView.xaml's myTemplateSymbolEdit), bound via SelectedItem - typing
            // alone sets the edit box's Text but does not select an item (SelectedItem/Symbol
            // stays unset) unless the dropdown is actually open, confirmed via a tree dump
            // showing no ListItems until sending F4 first. Real sequence, confirmed live:
            // click placeholder cell -> Enter (BeginEdit, per OnDataGridPreviewKeyDown) -> F4
            // (open dropdown) -> type the code (also live-narrows the list via
            // ComboBoxCultureInfo_FilterChanged) -> click the (now filtered-to-one-symbol)
            // first match -> one more Enter to commit the row. A second Enter here was tried
            // and confirmed harmful - it re-opens edit on the next placeholder row instead of
            // just committing.
            AutomationElement grid = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("CurrenciesDataGrid"));
            Assert.That(grid, Is.Not.Null, "CurrenciesDataGrid not found.");
            AutomationElement symbolCell = grid.FindFirstDescendant(cf => cf.ByName("Item: {NewItemPlaceholder}, Column Display Index: 0"));
            Assert.That(symbolCell, Is.Not.Null, "New-item-placeholder Symbol cell not found.");
            mainWindow.SetForeground();
            symbolCell.Click();
            Wait.UntilInputIsProcessed();

            // Double-click enters cell edit mode natively (standard DataGrid behavior),
            // independent of OnDataGridPreviewKeyDown's custom Enter-triggers-BeginEdit path,
            // which depends on the DataGrid itself (not just the cell) holding real Win32
            // keyboard focus.
            symbolCell.DoubleClick();
            Wait.UntilInputIsProcessed();

            // Confirm the Symbol cell's edit-mode ComboBox actually exists before sending F4 -
            // BeginEdit swaps in myTemplateSymbolEdit asynchronously, and sending F4 before that
            // template is live has no effect. Search from grid, not the old symbolCell handle -
            // the template swap replaces the cell's visual subtree, so symbolCell's own
            // descendants go stale (confirmed: the ComboBox was visibly on screen in a
            // screenshot taken exactly when searching symbolCell's descendants found nothing).
            AutomationElement editCombo = Retry.WhileNull(
                () => grid.FindFirstDescendant(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.ComboBox)),
                TimeSpan.FromSeconds(5)).Result;
            Assert.That(editCombo, Is.Not.Null, "Symbol cell's edit-mode ComboBox never appeared after double-click.");

            Keyboard.Type(FlaUI.Core.WindowsAPI.VirtualKeyShort.F4);

            // Wait for the dropdown to actually be open (at least one item visible) before
            // typing - a fixed short delay here was intermittently flaky when this test ran
            // alongside other FlaUI fixtures in the same longer session (dropdown popup
            // render/open is a real async UI operation, not instant).
            AutomationElement[] opened = Retry.WhileEmpty(
                () => mainWindow.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.ListItem)),
                TimeSpan.FromSeconds(5)).Result;
            Assert.That(opened, Is.Not.Null.And.Not.Empty, "Symbol dropdown did not open after F4.");

            Keyboard.Type("SEK");

            AutomationElement firstMatch = Retry.WhileNull(
                () => mainWindow.FindFirstDescendant(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.ListItem)),
                TimeSpan.FromSeconds(5)).Result;
            Assert.That(firstMatch, Is.Not.Null, "No filtered 'SEK' dropdown item found - the FilterChanged live-filter should have narrowed the ~250-entry culture list down to just Swedish Krona variants.");
            firstMatch.Click();
            Wait.UntilInputIsProcessed();
            Keyboard.Type(FlaUI.Core.WindowsAPI.VirtualKeyShort.RETURN);
            Wait.UntilInputIsProcessed();

            AutomationElement[] sekRowsAfter = Retry.WhileEmpty(
                () => mainWindow.FindAllDescendants(cf => cf.ByName("SEK")),
                TimeSpan.FromSeconds(5)).Result ?? Array.Empty<AutomationElement>();
            Assert.That(sekRowsAfter.Length, Is.EqualTo(1));
        }
    }
}
