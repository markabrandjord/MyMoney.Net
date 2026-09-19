using System.Linq;
using NUnit.Framework;
using Walkabout.Data;

namespace Walkabout.Tests
{
    [TestFixture]
    public class PayeesAndAliasesTests
    {
        [Test]
        public void ApplyAlias_MatchingTransactions_RenamesPayee()
        {
            var money = new MyMoney();
            var account = money.Accounts.AddAccount("Checking");
            var rawPayee = money.Payees.AddPayee(1);
            rawPayee.Name = "ACH DEBIT XFER 99182";
            var cleanPayee = money.Payees.AddPayee(2);
            cleanPayee.Name = "Alaska Airlines";

            var t = new Transaction(money.Transactions);
            t.Account = account;
            t.Date = System.DateTime.Now;
            t.Amount = -50.00M;
            t.Payee = rawPayee;
            money.Transactions.AddTransaction(t);

            var alias = new Alias();
            alias.Pattern = "ACH DEBIT XFER 99182";
            alias.AliasType = AliasType.None;
            alias.Payee = cleanPayee;

            int applied = money.ApplyAlias(alias, new[] { t });

            Assert.That(applied, Is.EqualTo(1));
            Assert.That(t.Payee, Is.SameAs(cleanPayee));
        }

        [Test]
        public void FindAliasMatches_NoMatchingTransactions_ReturnsEmpty()
        {
            var money = new MyMoney();
            var account = money.Accounts.AddAccount("Checking");
            var payee = money.Payees.AddPayee(1);
            payee.Name = "Something Else Entirely";

            var t = new Transaction(money.Transactions);
            t.Account = account;
            t.Date = System.DateTime.Now;
            t.Amount = -50.00M;
            t.Payee = payee;
            money.Transactions.AddTransaction(t);

            var alias = new Alias();
            alias.Pattern = "PATTERN THAT MATCHES NOTHING";
            alias.AliasType = AliasType.None;

            var matches = money.FindAliasMatches(alias, new[] { t });

            Assert.That(matches.Any(), Is.False);
        }

        [Test]
        public void FindSubsumedAliases_BroaderRegexSubsumesNarrowerAliases()
        {
            var money = new MyMoney();
            var payee = money.Payees.AddPayee(1);
            payee.Name = "Alaska Airlines";

            var narrow1 = money.Aliases.AddAlias(1);
            narrow1.Pattern = "ALASKA AIR 123";
            narrow1.AliasType = AliasType.None;
            narrow1.Payee = payee;

            var narrow2 = money.Aliases.AddAlias(2);
            narrow2.Pattern = "ALASKA  AIRLINES";
            narrow2.AliasType = AliasType.None;
            narrow2.Payee = payee;

            var broaderRegex = new Alias();
            broaderRegex.Pattern = ".*ALASKA[ ]+AIR.*";
            broaderRegex.AliasType = AliasType.Regex;

            var subsumed = money.FindSubsumedAliases(broaderRegex).ToList();

            Assert.That(subsumed.Count, Is.EqualTo(2), "Expected both narrow aliases to be subsumed by the broader regex.");
            Assert.That(subsumed, Does.Contain(narrow1));
            Assert.That(subsumed, Does.Contain(narrow2));
        }
    }
}
