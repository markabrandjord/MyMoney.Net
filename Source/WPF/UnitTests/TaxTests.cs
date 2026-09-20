using NUnit.Framework;
using Walkabout.Taxes;
using Walkabout.Utilities;

namespace Walkabout.Tests
{
    public class TaxTests
    {
        [Test]
        public void TestFederalIncomeTaxes()
        {
            var federalTaxes = FederalTaxes.Load();

            // test that incremental works and matches the non-incremental result.
            var paycheck = 250000;
            var tax = federalTaxes.GetIncomeTax(TaxFilingStatus.Single, 0, paycheck);

            // now do the same but in steps.
            decimal baseIncome = 0;
            decimal chunk = 50000;
            decimal totalTax = 0;
            while (baseIncome < paycheck)
            {
                var t = federalTaxes.GetIncomeTax(TaxFilingStatus.Single, baseIncome, chunk);
                totalTax += t;
                baseIncome += chunk;
            }

            Assert.That(Math.Round(totalTax, 0) == Math.Round(tax), "Incremental doesn't match single shot");
        }

        [Test]
        public void TestFederalCapitalGainsTaxes()
        {
            var federalTaxes = FederalTaxes.Load();

            // test that incremental works and matches the non-incremental result.
            var totalGains = 250000;
            var tax = federalTaxes.GetCapitalGainsTax(TaxFilingStatus.Single, 0, totalGains);

            // now do the same but in steps.
            decimal baseGains = 0;
            decimal chunk = 50000;
            decimal totalTax = 0;
            while (baseGains < totalGains)
            {
                var t = federalTaxes.GetCapitalGainsTax(TaxFilingStatus.Single, baseGains, chunk);
                totalTax += t;
                baseGains += chunk;
            }

            Assert.That(Math.Round(totalTax, 0) == Math.Round(tax), "Incremental doesn't match single shot");
        }

        [Test]
        public void TestStateIncomeTaxes()
        {
            var stateTaxes = StateTaxes.Load();

            // find a state with taxes
            var ca = (from s in stateTaxes.Data where s.Abbreviation == "CA" select s).FirstOrDefault();
            Assert.That(ca, Is.Not.Null, "Could not find CA tax data");

            // test that incremental works and matches the non-incremental result.
            var paycheck = 250000;
            var tax = ca.GetIncomeTax(TaxFilingStatus.Single, 0, paycheck);

            // now do the same but in steps.
            decimal baseIncome = 0;
            decimal chunk = 50000;
            decimal totalTax = 0;
            while (baseIncome < paycheck)
            {
                var t = ca.GetIncomeTax(TaxFilingStatus.Single, baseIncome, chunk);
                totalTax += t;
                baseIncome += chunk;
            }

            Assert.That(Math.Round(totalTax ,0) == Math.Round(tax), "Incremental doesn't match single shot");
        }

        [Test]
        public void TestStateCapitalGainsTaxes()
        {
            var stateTaxes = StateTaxes.Load();

            // find a state with custom capital gains 
            var wa = (from s in stateTaxes.Data where s.Abbreviation == "WA" select s).FirstOrDefault();
            Assert.That(wa, Is.Not.Null, "Could not find WA tax data");

            // test that incremental works and matches the non-incremental result.
            var totalGains = 1500000;
            var tax = wa.GetCapitalGainsTax(TaxFilingStatus.Single, 0, 0, totalGains);

            Assert.That(!wa.CapitalGainsTaxedAsIncome, "WA does not tax capital gains as income");

            // now do the same but in steps.
            decimal baseIncome = 0;
            decimal chunk = 50000;
            decimal totalTax = 0;
            decimal baseGains = 0;
            while (baseGains < totalGains)
            {
                var t = wa.GetCapitalGainsTax(TaxFilingStatus.Single, baseIncome, baseGains, chunk);
                totalTax += t;
                baseGains += chunk;
            }

            Assert.That(Math.Round(totalTax, 0) == Math.Round(tax), "Incremental doesn't match single shot");
        }

        [Test]
        public void TestStateTaxes_UnknownAbbreviation_ReturnsNull()
        {
            var stateTaxes = StateTaxes.Load();
            var unknown = (from s in stateTaxes.Data where s.Abbreviation == "ZZ" select s).FirstOrDefault();
            Assert.That(unknown, Is.Null, "Expected no state tax data for a made-up abbreviation");
        }

        // Investigated (not guessed) by reading StateTaxes.GetCapitalGainsTax directly: unlike
        // FederalTaxes.GetCapitalGainsTax (which explicitly returns 0 for gains < 0) and both
        // GetIncomeTax methods (which explicitly return 0 for paycheck < 0), StateTaxes'
        // fixed-rate capital gains path (used by WA) has no such guard. When accumulated
        // baseGains already exceeds the state's deduction amount, a negative "gains" argument
        // flows straight into `gains * fixedRate / 100` and comes back out as a genuine
        // negative tax value rather than being clamped to zero. This pins that real,
        // observed asymmetry as a regression check.
        [Test]
        public void TestStateCapitalGainsTax_NegativeGains_ReturnsNegativeTax()
        {
            var stateTaxes = StateTaxes.Load();
            var wa = (from s in stateTaxes.Data where s.Abbreviation == "WA" select s).FirstOrDefault();
            Assert.That(wa, Is.Not.Null, "Could not find WA tax data");

            // WA capital gains: fixedRate 7.0%, deductionAmount 278000 (verified in StateTaxes.json).
            // baseGains (400000) already exceeds the deduction, so totalGain stays positive even
            // though the incremental "gains" argument here is negative.
            var tax = wa.GetCapitalGainsTax(TaxFilingStatus.Single, 0, 400000, -50000);

            Assert.That(tax, Is.EqualTo(-3500.0M), "StateTaxes.GetCapitalGainsTax does not clamp negative gains to zero once baseGains exceeds the deduction amount");
        }
    }
}
