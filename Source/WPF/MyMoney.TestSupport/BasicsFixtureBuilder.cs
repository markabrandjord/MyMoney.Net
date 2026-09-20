using System;
using System.IO;
using Walkabout.Utilities;

namespace Walkabout.Data
{
    /// <summary>
    /// Test Data Builder (Meszaros, xUnit Test Patterns) for the Basics scenario catalog's
    /// shared seed data. Called fresh by every business-layer and FlaUI test that needs it -
    /// see docs/superpowers/specs/2026-09-18-basics-test-implementation-design.md's "Shared
    /// fixture builder" section. Generalizes the one-off pattern in
    /// Source/WPF/UITests/FixtureGenerator.cs into a function called on every test run instead
    /// of a manually-regenerated checked-in binary.
    /// </summary>
    public static class BasicsFixtureBuilder
    {
        public static MyMoney Build()
        {
            var money = new MyMoney();

            // Categories: a parent/child pair with one seeded transaction, for Categories
            // subsection's rename/delete/merge scenarios.
            Category fun = money.Categories.GetOrCreateCategory("Fun", CategoryType.Expense);
            Category movies = money.Categories.GetOrCreateCategory("Fun:Movies", CategoryType.Expense);
            Category videos = money.Categories.GetOrCreateCategory("Fun:Videos", CategoryType.Expense);

            var checking = money.Accounts.AddAccount("Basics Checking");
            checking.Type = AccountType.Checking;

            var moviePayee = money.Payees.AddPayee(1);
            moviePayee.Name = "AMC THEATRES 1234";

            var movieTxn = new Transaction();
            movieTxn.Account = checking;
            movieTxn.Date = new DateTime(2026, 1, 5);
            movieTxn.Amount = -32.50M;
            movieTxn.Payee = moviePayee;
            movieTxn.Category = movies;
            money.Transactions.AddTransaction(movieTxn);

            // Payees & Aliases: a second messy payee plus 3 aliases (1 plain, 2 narrow for the
            // regex-consolidation scenario).
            var alaskaPayee = money.Payees.AddPayee(2);
            alaskaPayee.Name = "Alaska Airlines";

            var plainAlias = money.Aliases.AddAlias(1);
            plainAlias.Pattern = "AMC THEATRES 1234";
            plainAlias.AliasType = AliasType.None;
            plainAlias.Payee = moviePayee;

            var narrowAlias1 = money.Aliases.AddAlias(2);
            narrowAlias1.Pattern = "ALASKA AIR 123";
            narrowAlias1.AliasType = AliasType.None;
            narrowAlias1.Payee = alaskaPayee;

            var narrowAlias2 = money.Aliases.AddAlias(3);
            narrowAlias2.Pattern = "ALASKA  AIRLINES";
            narrowAlias2.AliasType = AliasType.None;
            narrowAlias2.Payee = alaskaPayee;

            // Splits & Transfers: an out-of-balance split, and a linked transfer pair (one
            // side reconciled, one not).
            var splitTxn = new Transaction();
            splitTxn.Account = checking;
            splitTxn.Date = new DateTime(2026, 1, 10);
            splitTxn.Amount = -100.00M;
            splitTxn.Payee = alaskaPayee;
            var split1 = splitTxn.NonNullSplits.AddSplit();
            split1.Category = fun;
            split1.Amount = -40.00M;
            money.Transactions.AddTransaction(splitTxn);

            var savings = money.Accounts.AddAccount("Basics Savings");
            savings.Type = AccountType.Savings;
            var transferSource = new Transaction();
            transferSource.Account = checking;
            transferSource.Date = new DateTime(2026, 1, 15);
            transferSource.Amount = -200.00M;
            money.Transactions.AddTransaction(transferSource);
            money.Transfer(transferSource, savings);

            // Merging Duplicate Transactions: two near-duplicate transactions (same
            // amount/date, different FITID).
            var dupPayee = money.Payees.AddPayee(3);
            dupPayee.Name = "TARGET T-1234";
            var dupA = new Transaction();
            dupA.Account = checking;
            dupA.Date = new DateTime(2026, 1, 20);
            dupA.Amount = -11.73M;
            dupA.Payee = dupPayee;
            dupA.FITID = "FID-A";
            money.Transactions.AddTransaction(dupA);
            var dupB = new Transaction();
            dupB.Account = checking;
            dupB.Date = new DateTime(2026, 1, 20);
            dupB.Amount = -11.73M;
            dupB.Payee = dupPayee;
            dupB.FITID = "FID-B";
            money.Transactions.AddTransaction(dupB);

            // Currencies & Securities: deliberately no currency data here - it lives in
            // Task 6/7's own test routines instead, not the shared builder.

            // Auto-Categorization: payee history at a known amount, for suggestion scenarios.
            var groceryPayee = money.Payees.AddPayee(4);
            groceryPayee.Name = "SAFEWAY #4455";
            var groceryHistory = new Transaction();
            groceryHistory.Account = checking;
            groceryHistory.Date = new DateTime(2026, 1, 3);
            groceryHistory.Amount = -55.00M;
            groceryHistory.Payee = groceryPayee;
            groceryHistory.Category = money.Categories.GetOrCreateCategory("Food:Groceries", CategoryType.Expense);
            money.Transactions.AddTransaction(groceryHistory);

            return money;
        }

        /// <summary>
        /// Pre-creates one attachment file on disk for a transaction, matching
        /// AttachmentManager's real directory/file-naming convention (Attachments/
        /// AttachmentManager.cs's SetupAttachmentDirectory/GetUniqueFileName): a
        /// "&lt;dbname&gt;.Attachments" folder next to the database file, containing one
        /// subfolder per account (name sanitized via NativeMethods.GetValidFileName), holding
        /// files named "&lt;transactionId&gt;&lt;extension&gt;" for a transaction's first
        /// attachment. Not part of Build() itself (which has no database path to work with) -
        /// call this separately once the scratch database's path and the target transaction's
        /// real (post-AddTransaction) Id are both known.
        /// </summary>
        public static void WriteAttachmentFile(string databasePath, Account account, long transactionId, string content)
        {
            string attachmentDirectory = Path.Combine(
                Path.GetDirectoryName(databasePath),
                Path.GetFileNameWithoutExtension(databasePath) + ".Attachments");
            string accountDirectory = Path.Combine(attachmentDirectory, NativeMethods.GetValidFileName(account.Name));
            Directory.CreateDirectory(accountDirectory);
            string fileName = Path.Combine(accountDirectory, transactionId + ".txt");
            File.WriteAllText(fileName, content);
        }
    }
}
