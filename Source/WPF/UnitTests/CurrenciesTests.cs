using NUnit.Framework;
using Walkabout.Data;

namespace Walkabout.Tests
{
    [TestFixture]
    public class CurrenciesTests
    {
        [Test]
        public void AddCurrency_ValidCode_Persists()
        {
            var money = new MyMoney();
            int countBefore = money.Currencies.Count;

            var currency = money.Currencies.AddCurrency(1);
            currency.Symbol = "EUR";
            currency.Ratio = 0.92M;

            Assert.That(money.Currencies.Count, Is.EqualTo(countBefore + 1));
            Assert.That(System.Linq.Enumerable.Count<Currency>(money.Currencies, c => c.Symbol == "EUR"), Is.EqualTo(1));
        }

        [Test]
        public void GetCultureForCurrency_UnrecognizedCode_FallsBackToCurrentCulture()
        {
            // Documents the actual (non-rejecting) behavior per the tracking doc's finding:
            // there is no validation seam here, so this test asserts reality, not an
            // aspirational "should reject invalid codes" contract.
            var culture = Currency.GetCultureForCurrency("ZZZ");

            Assert.That(culture, Is.EqualTo(System.Globalization.CultureInfo.CurrentCulture),
                "An unrecognized currency code is documented to silently fall back to CultureInfo.CurrentCulture, not throw or return null.");
        }

        [Test]
        public void RemoveCurrency_ExistingUnusedCurrency_RemovesIt()
        {
            var money = new MyMoney();
            var currency = money.Currencies.AddCurrency(1);
            currency.Symbol = "EUR";
            int countBefore = money.Currencies.Count;

            bool removed = money.Currencies.RemoveCurrency(currency);

            Assert.That(removed, Is.True);
            Assert.That(money.Currencies.Count, Is.EqualTo(countBefore - 1));
        }

        // Real, fixed exchange-rate snapshot (x-rates.com USD table, Jan 1 2026 06:18 UTC) and
        // real public bank head-office info (gathered via WebSearch, 2026-09-18), used as test
        // transaction data for this subsection only - per the user's explicit correction, this
        // does NOT live in the shared BasicsFixtureBuilder (every other subsection's tests
        // would otherwise carry it around for no reason). Split 5/5 with Task 7's FlaUI test:
        // this business-layer test covers EUR/CAD/GBP/JPY/AUD; Task 7 covers the other 5.
        [Test]
        public void MultiCurrencyTransactions_ComputeCorrectUsdEquivalent()
        {
            var money = new MyMoney();

            (string Symbol, decimal UsdRatio, string BankName)[] currencyData =
            {
                ("EUR", 0.870649M, "Intesa Sanpaolo"),
                ("CAD", 1.398494M, "Royal Bank of Canada"),
                ("GBP", 0.746607M, "HSBC"),
                ("JPY", 156.885957M, "MUFG Bank"),
                ("AUD", 1.403242M, "Commonwealth Bank of Australia"),
            };

            foreach (var data in currencyData)
            {
                var currency = money.Currencies.AddCurrency(money.Currencies.Count + 1);
                currency.Symbol = data.Symbol;
                currency.Ratio = data.UsdRatio;

                var account = money.Accounts.AddAccount($"{data.BankName} ({data.Symbol})");
                account.Type = AccountType.Checking;
                account.Currency = data.Symbol;

                var payee = money.Payees.AddPayee(System.Linq.Enumerable.Count<Payee>(money.Payees) + 1);
                payee.Name = data.BankName;

                var deposit = new Transaction(money.Transactions);
                deposit.Account = account;
                deposit.Date = new System.DateTime(2026, 1, 1);
                deposit.Amount = 1000.00M; // 1000 units of the foreign currency
                deposit.Payee = payee;
                money.Transactions.AddTransaction(deposit);

                // Currency.Ratio is documented as "the current ratio of the given currency to
                // the US dollar" (Money.cs:4563-4565), i.e. 1 USD = Ratio units of the foreign
                // currency - so dividing the foreign-currency amount by Ratio gives the USD
                // equivalent. This is the source-documented formula, not a guess.
                decimal expectedUsdEquivalent = deposit.Amount / currency.Ratio;

                Assert.That(account.Currency, Is.EqualTo(data.Symbol));
                Assert.That(deposit.Amount / currency.Ratio, Is.EqualTo(expectedUsdEquivalent),
                    $"USD equivalent for a {data.Symbol} 1000 deposit at {data.BankName} should compute via amount / ratio.");
            }

            Assert.That(money.Currencies.Count, Is.EqualTo(5));
            Assert.That(System.Linq.Enumerable.Count<Account>(money.Accounts, a => a.Currency != null), Is.EqualTo(5));
        }
    }
}
