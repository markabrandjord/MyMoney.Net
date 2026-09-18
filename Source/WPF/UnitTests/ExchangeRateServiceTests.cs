using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Walkabout.Data;
using Walkabout.StockQuotes;

namespace Walkabout.Tests
{
    /// <summary>
    /// Covers ExchangeRateService's pure/no-network members - one of the four classes the
    /// final whole-branch review of the business-layer subsystem migration found had zero test
    /// coverage (Task 10). UpdateRates and TestApiKeyAsync are explicitly out of scope per the
    /// task brief - they make real HTTP calls to fastforex.io and no mocking seam exists over
    /// that boundary today.
    /// </summary>
    [TestFixture]
    public class ExchangeRateServiceTests
    {
        // CreateOrUpdate only does anything useful once its private `ratesCache` dictionary has
        // an entry for the requested code - and the only production code path that ever adds to
        // that dictionary is GetExchangeRates, which makes a live HTTP call. There is no public
        // or internal seam over that (unlike IBusinessLayerUiCallback/IImportProgressReporter
        // elsewhere in this migration). Rather than skip the success branch entirely or reach
        // for live network access, this seeds that one already-existing private field directly
        // via reflection - not a new mocking abstraction, just exercising real internal state
        // that CreateOrUpdate's own logic already depends on.
        private static void SeedRatesCache(ExchangeRateService service, string code, decimal usdRatio)
        {
            FieldInfo field = typeof(ExchangeRateService).GetField("ratesCache", BindingFlags.NonPublic | BindingFlags.Instance);
            var cache = (Dictionary<string, decimal>)field.GetValue(service);
            cache[code] = usdRatio;
        }

        #region IsMySettings / GetDefaultSettings (static, pure)

        [Test]
        public void GetDefaultSettings_ReturnsFastForexServiceSettings()
        {
            OnlineServiceSettings settings = ExchangeRateService.GetDefaultSettings();

            Assert.AreEqual("fastforex.io", settings.Name);
            Assert.AreEqual("ExchangeRate", settings.ServiceType);
            Assert.AreEqual("", settings.ApiKey);
            Assert.AreEqual(1000000, settings.ApiRequestsPerMonthLimit);
        }

        [Test]
        public void IsMySettings_MatchingName_ReturnsTrue()
        {
            OnlineServiceSettings settings = ExchangeRateService.GetDefaultSettings();

            Assert.IsTrue(ExchangeRateService.IsMySettings(settings));
        }

        [Test]
        public void IsMySettings_DifferentServiceSettings_ReturnsFalse()
        {
            var settings = new OnlineServiceSettings { Name = "some-other-service" };

            Assert.IsFalse(ExchangeRateService.IsMySettings(settings));
        }

        #endregion

        #region CreateOrUpdate (internal, no network)

        [Test]
        public void CreateOrUpdate_CodeNeverFetched_ReturnsNull()
        {
            // Genuine invalid/unavailable input: a fresh ExchangeRateService's ratesCache is
            // always empty until a real fastforex.io download populates it, so any code -
            // recognized currency or not - returns null here. Verified by reading
            // CreateOrUpdate's actual implementation (it short-circuits on
            // ratesCache.TryGetValue failing), not assumed.
            var money = new MyMoney();
            var service = new ExchangeRateService(ExchangeRateService.GetDefaultSettings(), null, null) { MyMoney = money };

            Currency result = service.CreateOrUpdate("EUR");

            Assert.IsNull(result);
            Assert.IsNull(money.Currencies.FindCurrency("EUR"), "CreateOrUpdate must not create a currency it has no rate for");
        }

        [Test]
        public void CreateOrUpdate_CodeInRatesCacheAndNoExistingCurrency_CreatesNewCurrencyWithComputedRatio()
        {
            var money = new MyMoney();
            var service = new ExchangeRateService(ExchangeRateService.GetDefaultSettings(), null, null) { MyMoney = money };
            SeedRatesCache(service, "EUR", 0.9m); // 0.9 EUR per USD

            Currency result = service.CreateOrUpdate("EUR");

            Assert.IsNotNull(result);
            Assert.AreEqual("EUR", result.Symbol);
            Assert.AreEqual(1m / 0.9m, result.Ratio);
            Assert.AreSame(result, money.Currencies.FindCurrency("EUR"));
        }

        [Test]
        public void CreateOrUpdate_CodeInRatesCacheAndCurrencyAlreadyExists_UpdatesExistingInsteadOfDuplicating()
        {
            var money = new MyMoney();
            var existing = new Currency { Symbol = "EUR" };
            money.Currencies.AddCurrency(existing);
            var service = new ExchangeRateService(ExchangeRateService.GetDefaultSettings(), null, null) { MyMoney = money };
            SeedRatesCache(service, "EUR", 0.5m);

            Currency result = service.CreateOrUpdate("EUR");

            Assert.AreSame(existing, result);
            Assert.AreEqual(1m / 0.5m, result.Ratio);
        }

        #endregion
    }
}
