using System;
using System.Data;
using System.IO;
using NUnit.Framework;
using Walkabout.Data;

namespace Walkabout.TestSupport
{
    [TestFixture]
    public class SqliteDatabaseContractTests : DatabaseContractTests
    {
        private string path;

        protected override IDatabase CreateDatabase()
        {
            this.path = Path.Combine(Path.GetTempPath(), $"ContractTest_{Guid.NewGuid():N}.mmdb");
            return new SqliteDatabase { DatabasePath = this.path };
        }

        public override void TearDown()
        {
            base.TearDown();
            if (this.path != null && File.Exists(this.path))
            {
                File.Delete(this.path);
            }
        }

        [Test]
        public void Connect_EnablesWalModeAndBusyTimeout()
        {
            DataSet journalModeResult = this.Database.QueryDataSet("PRAGMA journal_mode;");
            Assert.That(journalModeResult.Tables[0].Rows[0][0].ToString().ToLowerInvariant(), Is.EqualTo("wal"));

            DataSet busyTimeoutResult = this.Database.QueryDataSet("PRAGMA busy_timeout;");
            Assert.That(Convert.ToInt32(busyTimeoutResult.Tables[0].Rows[0][0]), Is.EqualTo(5000));
        }

        private static MyMoney BuildOneCategoryMoney(out Category category)
        {
            MyMoney money = new MyMoney();
            category = money.Categories.GetOrCreateCategory("Groceries", CategoryType.Expense);
            return money;
        }

        [Test]
        public void SaveOne_NewCategory_PersistsAndSetsRowVersionToOne()
        {
            BuildOneCategoryMoney(out Category category);

            this.Database.SaveOne(category);

            Assert.That(category.RowVersion, Is.EqualTo(1));
            Assert.That(category.IsInserted, Is.False);
            Assert.That(category.IsChanged, Is.False);

            MyMoney reloaded = this.Database.Load(null);
            Category found = reloaded.Categories.FindCategory("Groceries");
            Assert.That(found, Is.Not.Null);
            Assert.That(found.RowVersion, Is.EqualTo(1));
        }

        [Test]
        public void SaveOne_UpdateAfterReload_IncrementsRowVersion()
        {
            BuildOneCategoryMoney(out Category category);
            this.Database.SaveOne(category);

            MyMoney reloaded = this.Database.Load(null);
            Category found = reloaded.Categories.FindCategory("Groceries");
            found.Description = "Updated";
            this.Database.SaveOne(found);

            Assert.That(found.RowVersion, Is.EqualTo(2));

            MyMoney reloadedAgain = this.Database.Load(null);
            Category foundAgain = reloadedAgain.Categories.FindCategory("Groceries");
            Assert.That(foundAgain.Description, Is.EqualTo("Updated"));
            Assert.That(foundAgain.RowVersion, Is.EqualTo(2));
        }

        [Test]
        public void SaveOne_StaleRowVersion_ThrowsConcurrencyConflictException()
        {
            BuildOneCategoryMoney(out Category category);
            this.Database.SaveOne(category);

            MyMoney readerA = this.Database.Load(null);
            MyMoney readerB = this.Database.Load(null);

            Category categoryA = readerA.Categories.FindCategory("Groceries");
            categoryA.Description = "From A";
            this.Database.SaveOne(categoryA);

            Category categoryB = readerB.Categories.FindCategory("Groceries");
            categoryB.Description = "From B";
            var ex = Assert.Throws<ConcurrencyConflictException>(() => this.Database.SaveOne(categoryB));
            Assert.That(ex.StoredRowVersion, Is.EqualTo(2));
            Assert.That(ex.CallerRowVersion, Is.EqualTo(1));

            MyMoney reloaded = this.Database.Load(null);
            Assert.That(reloaded.Categories.FindCategory("Groceries").Description, Is.EqualTo("From A"));
        }

        [Test]
        public void SaveOne_Delete_RemovesRowFromDatabaseAndContainer()
        {
            BuildOneCategoryMoney(out Category category);
            this.Database.SaveOne(category);

            MyMoney reloaded = this.Database.Load(null);
            Category toDelete = reloaded.Categories.FindCategory("Groceries");
            int toDeleteId = toDelete.Id;
            reloaded.Categories.RemoveCategory(toDelete);
            Assert.That(toDelete.IsDeleted, Is.True);

            // RemoveCategory(c, forceRemoveAfterSave: false) (its default here) always clears the
            // name-based categoryIndex immediately, regardless of forceRemoveAfterSave - so
            // FindCategory("Groceries") would already be null at this point even if SaveOne's
            // postCommit RemoveChild(c, true) call never fired. The id-based `categories`
            // dictionary backing FindCategoryById is different: it's only cleared when
            // c.IsInserted || forceRemoveAfterSave is true, which isn't the case for this
            // freshly-reloaded, non-inserted category - so it's untouched by RemoveCategory here
            // and only genuinely proves SaveOne's deferred RemoveChild(c, true) ran.
            Assert.That(reloaded.Categories.FindCategoryById(toDeleteId), Is.Not.Null,
                "sanity check: id-based lookup must still find the category before SaveOne runs its postCommit RemoveChild");

            this.Database.SaveOne(toDelete);

            Assert.That(reloaded.Categories.FindCategoryById(toDeleteId), Is.Null);

            MyMoney reloadedAgain = this.Database.Load(null);
            Assert.That(reloadedAgain.Categories.FindCategory("Groceries"), Is.Null);
        }

        [Test]
        public void SaveBatch_OneStaleRootAmongMany_RollsBackTransactionAndPreservesInMemoryState()
        {
            MyMoney money = new MyMoney();
            Category a = money.Categories.GetOrCreateCategory("A", CategoryType.Expense);
            Category b = money.Categories.GetOrCreateCategory("B", CategoryType.Expense);
            this.Database.SaveBatch(new PersistentObject[] { a, b });

            MyMoney reader = this.Database.Load(null);
            Category staleA = reader.Categories.FindCategory("A");
            Category freshB = reader.Categories.FindCategory("B");

            // Someone else updates A first, so staleA's RowVersion (1) is now behind.
            MyMoney otherWriter = this.Database.Load(null);
            Category otherA = otherWriter.Categories.FindCategory("A");
            otherA.Description = "Changed elsewhere";
            this.Database.SaveOne(otherA);

            staleA.Description = "Attempted A";
            freshB.Description = "Attempted B";
            long freshBOriginalRowVersion = freshB.RowVersion;

            // freshB is listed FIRST deliberately: SaveBatch's loop processes roots in order, so
            // freshB's own UPDATE executes and "succeeds" (within the still-open transaction) on
            // iteration 1, and staleA's conflict is only detected and thrown on iteration 2 - by
            // which point freshB's SQL has already run. This is what actually exercises the
            // postCommitActions deferral: if B were listed second (i.e. never reached because A's
            // conflict throws first), this assertion would pass trivially under ANY
            // implementation, buggy or not, since B's code path would never execute at all.
            Assert.Throws<ConcurrencyConflictException>(
                () => this.Database.SaveBatch(new PersistentObject[] { freshB, staleA }));

            // B must NOT have been committed, even though only A conflicted - proves the real
            // SQLiteTransaction actually rolled back both writes, not just A's, including the one
            // that already "succeeded" earlier in the same loop.
            MyMoney reloaded = this.Database.Load(null);
            Assert.That(reloaded.Categories.FindCategory("B").Description, Is.Not.EqualTo("Attempted B"));

            // B's in-memory state must ALSO be unchanged. This is the regression this test exists
            // for: an earlier draft of SaveOneCategory applied RowVersion/OnUpdated immediately
            // per-root inside the loop, so B's in-memory RowVersion got bumped and its dirty flag
            // cleared as soon as its own UPDATE ran - even though the transaction that "committed"
            // it was rolled back moments later by A's conflict on the very next iteration -
            // postCommitActions exists specifically to prevent this.
            Assert.That(freshB.RowVersion, Is.EqualTo(freshBOriginalRowVersion));
            Assert.That(freshB.IsChanged, Is.True);
        }

        private static MyMoney BuildOneCurrencyMoney(out Currency currency)
        {
            MyMoney money = new MyMoney();
            currency = new Currency(money.Currencies) { Symbol = "EUR", Name = "Euro", Ratio = 1.2m, LastRatio = 1.1m, CultureCode = "de-DE" };
            money.Currencies.AddCurrency(currency);
            return money;
        }

        [Test]
        public void SaveOne_NewCurrency_PersistsAndSetsRowVersionToOne()
        {
            BuildOneCurrencyMoney(out Currency currency);

            this.Database.SaveOne(currency);

            Assert.That(currency.RowVersion, Is.EqualTo(1));
            Assert.That(currency.IsInserted, Is.False);
            Assert.That(currency.IsChanged, Is.False);

            MyMoney reloaded = this.Database.Load(null);
            Currency found = reloaded.Currencies.FindCurrency("EUR");
            Assert.That(found, Is.Not.Null);
            Assert.That(found.RowVersion, Is.EqualTo(1));
        }

        [Test]
        public void SaveOne_UpdateCurrencyAfterReload_IncrementsRowVersion()
        {
            BuildOneCurrencyMoney(out Currency currency);
            this.Database.SaveOne(currency);

            MyMoney reloaded = this.Database.Load(null);
            Currency found = reloaded.Currencies.FindCurrency("EUR");
            found.Ratio = 1.3m;
            this.Database.SaveOne(found);

            Assert.That(found.RowVersion, Is.EqualTo(2));

            MyMoney reloadedAgain = this.Database.Load(null);
            Currency foundAgain = reloadedAgain.Currencies.FindCurrency("EUR");
            Assert.That(foundAgain.Ratio, Is.EqualTo(1.3m));
            Assert.That(foundAgain.RowVersion, Is.EqualTo(2));
        }

        [Test]
        public void SaveOne_StaleCurrencyRowVersion_ThrowsConcurrencyConflictException()
        {
            BuildOneCurrencyMoney(out Currency currency);
            this.Database.SaveOne(currency);

            MyMoney readerA = this.Database.Load(null);
            MyMoney readerB = this.Database.Load(null);

            Currency currencyA = readerA.Currencies.FindCurrency("EUR");
            currencyA.Ratio = 1.3m;
            this.Database.SaveOne(currencyA);

            Currency currencyB = readerB.Currencies.FindCurrency("EUR");
            currencyB.Ratio = 1.4m;
            var ex = Assert.Throws<ConcurrencyConflictException>(() => this.Database.SaveOne(currencyB));
            Assert.That(ex.StoredRowVersion, Is.EqualTo(2));
            Assert.That(ex.CallerRowVersion, Is.EqualTo(1));

            MyMoney reloaded = this.Database.Load(null);
            Assert.That(reloaded.Currencies.FindCurrency("EUR").Ratio, Is.EqualTo(1.3m));
        }

        [Test]
        public void SaveOne_DeleteCurrency_RemovesRowFromDatabaseAndContainer()
        {
            BuildOneCurrencyMoney(out Currency currency);
            this.Database.SaveOne(currency);

            MyMoney reloaded = this.Database.Load(null);
            Currency toDelete = reloaded.Currencies.FindCurrency("EUR");
            reloaded.Currencies.RemoveCurrency(toDelete);
            Assert.That(toDelete.IsDeleted, Is.True);

            // RemoveCurrency(item, forceRemoveAfterSave: false) (its default here) calls
            // ResetCache() unconditionally, which would make a symbol-based FindCurrency("EUR")
            // return null immediately (the deleted item is filtered out of the rebuilt quickLookup
            // cache) regardless of whether SaveOne's postCommit RemoveChild(c, true) ever fires.
            // Contains() checks the underlying Id-keyed dictionary directly, unaffected by that
            // cache, so it's the only way to genuinely prove SaveOne's deferred removal ran.
            Assert.That(reloaded.Currencies.Contains(toDelete), Is.True,
                "sanity check: the currency must still be in the underlying dictionary before SaveOne runs its postCommit RemoveChild");

            this.Database.SaveOne(toDelete);

            Assert.That(reloaded.Currencies.Contains(toDelete), Is.False);

            MyMoney reloadedAgain = this.Database.Load(null);
            Assert.That(reloadedAgain.Currencies.FindCurrency("EUR"), Is.Null);
        }

        private static MyMoney BuildOneOnlineAccountMoney(out OnlineAccount onlineAccount)
        {
            MyMoney money = new MyMoney();
            onlineAccount = money.OnlineAccounts.AddOnlineAccount("Chase");
            onlineAccount.Institution = "Chase Bank";
            return money;
        }

        [Test]
        public void SaveOne_NewOnlineAccount_PersistsAndSetsRowVersionToOne()
        {
            BuildOneOnlineAccountMoney(out OnlineAccount onlineAccount);

            this.Database.SaveOne(onlineAccount);

            Assert.That(onlineAccount.RowVersion, Is.EqualTo(1));
            Assert.That(onlineAccount.IsInserted, Is.False);
            Assert.That(onlineAccount.IsChanged, Is.False);

            MyMoney reloaded = this.Database.Load(null);
            OnlineAccount found = reloaded.OnlineAccounts.FindOnlineAccount("Chase");
            Assert.That(found, Is.Not.Null);
            Assert.That(found.RowVersion, Is.EqualTo(1));
        }

        [Test]
        public void SaveOne_UpdateOnlineAccountAfterReload_IncrementsRowVersion()
        {
            BuildOneOnlineAccountMoney(out OnlineAccount onlineAccount);
            this.Database.SaveOne(onlineAccount);

            MyMoney reloaded = this.Database.Load(null);
            OnlineAccount found = reloaded.OnlineAccounts.FindOnlineAccount("Chase");
            found.Institution = "Updated Bank";
            this.Database.SaveOne(found);

            Assert.That(found.RowVersion, Is.EqualTo(2));

            MyMoney reloadedAgain = this.Database.Load(null);
            OnlineAccount foundAgain = reloadedAgain.OnlineAccounts.FindOnlineAccount("Chase");
            Assert.That(foundAgain.Institution, Is.EqualTo("Updated Bank"));
            Assert.That(foundAgain.RowVersion, Is.EqualTo(2));
        }

        [Test]
        public void SaveOne_StaleOnlineAccountRowVersion_ThrowsConcurrencyConflictException()
        {
            BuildOneOnlineAccountMoney(out OnlineAccount onlineAccount);
            this.Database.SaveOne(onlineAccount);

            MyMoney readerA = this.Database.Load(null);
            MyMoney readerB = this.Database.Load(null);

            OnlineAccount accountA = readerA.OnlineAccounts.FindOnlineAccount("Chase");
            accountA.Institution = "From A";
            this.Database.SaveOne(accountA);

            OnlineAccount accountB = readerB.OnlineAccounts.FindOnlineAccount("Chase");
            accountB.Institution = "From B";
            var ex = Assert.Throws<ConcurrencyConflictException>(() => this.Database.SaveOne(accountB));
            Assert.That(ex.StoredRowVersion, Is.EqualTo(2));
            Assert.That(ex.CallerRowVersion, Is.EqualTo(1));

            MyMoney reloaded = this.Database.Load(null);
            Assert.That(reloaded.OnlineAccounts.FindOnlineAccount("Chase").Institution, Is.EqualTo("From A"));
        }

        [Test]
        public void SaveOne_DeleteOnlineAccount_RemovesRowFromDatabaseAndContainer()
        {
            BuildOneOnlineAccountMoney(out OnlineAccount onlineAccount);
            this.Database.SaveOne(onlineAccount);

            MyMoney reloaded = this.Database.Load(null);
            OnlineAccount toDelete = reloaded.OnlineAccounts.FindOnlineAccount("Chase");
            reloaded.OnlineAccounts.RemoveOnlineAccount(toDelete);
            Assert.That(toDelete.IsDeleted, Is.True);

            this.Database.SaveOne(toDelete);

            Assert.That(reloaded.OnlineAccounts.FindOnlineAccount("Chase"), Is.Null);

            MyMoney reloadedAgain = this.Database.Load(null);
            Assert.That(reloadedAgain.OnlineAccounts.FindOnlineAccount("Chase"), Is.Null);
        }

        private static MyMoney BuildOneAccountMoney(out Account account)
        {
            MyMoney money = new MyMoney();
            account = money.Accounts.AddAccount("Checking");
            account.OpeningBalance = 100m;
            return money;
        }

        [Test]
        public void SaveOne_NewAccount_PersistsAndSetsRowVersionToOne()
        {
            BuildOneAccountMoney(out Account account);

            this.Database.SaveOne(account);

            Assert.That(account.RowVersion, Is.EqualTo(1));
            Assert.That(account.IsInserted, Is.False);
            Assert.That(account.IsChanged, Is.False);

            MyMoney reloaded = this.Database.Load(null);
            Account found = reloaded.Accounts.FindAccount("Checking");
            Assert.That(found, Is.Not.Null);
            Assert.That(found.RowVersion, Is.EqualTo(1));
        }

        [Test]
        public void SaveOne_UpdateAccountAfterReload_IncrementsRowVersion()
        {
            BuildOneAccountMoney(out Account account);
            this.Database.SaveOne(account);

            MyMoney reloaded = this.Database.Load(null);
            Account found = reloaded.Accounts.FindAccount("Checking");
            found.Description = "Updated";
            this.Database.SaveOne(found);

            Assert.That(found.RowVersion, Is.EqualTo(2));

            MyMoney reloadedAgain = this.Database.Load(null);
            Account foundAgain = reloadedAgain.Accounts.FindAccount("Checking");
            Assert.That(foundAgain.Description, Is.EqualTo("Updated"));
            Assert.That(foundAgain.RowVersion, Is.EqualTo(2));
        }

        [Test]
        public void SaveOne_StaleAccountRowVersion_ThrowsConcurrencyConflictException()
        {
            BuildOneAccountMoney(out Account account);
            this.Database.SaveOne(account);

            MyMoney readerA = this.Database.Load(null);
            MyMoney readerB = this.Database.Load(null);

            Account accountA = readerA.Accounts.FindAccount("Checking");
            accountA.Description = "From A";
            this.Database.SaveOne(accountA);

            Account accountB = readerB.Accounts.FindAccount("Checking");
            accountB.Description = "From B";
            var ex = Assert.Throws<ConcurrencyConflictException>(() => this.Database.SaveOne(accountB));
            Assert.That(ex.StoredRowVersion, Is.EqualTo(2));
            Assert.That(ex.CallerRowVersion, Is.EqualTo(1));

            MyMoney reloaded = this.Database.Load(null);
            Assert.That(reloaded.Accounts.FindAccount("Checking").Description, Is.EqualTo("From A"));
        }

        [Test]
        public void SaveOne_DeleteAccount_RemovesRowFromDatabaseAndContainer()
        {
            BuildOneAccountMoney(out Account account);
            this.Database.SaveOne(account);

            MyMoney reloaded = this.Database.Load(null);
            Account toDelete = reloaded.Accounts.FindAccount("Checking");
            reloaded.Accounts.RemoveAccount(toDelete);
            Assert.That(toDelete.IsDeleted, Is.True);

            this.Database.SaveOne(toDelete);

            Assert.That(reloaded.Accounts.FindAccount("Checking"), Is.Null);

            MyMoney reloadedAgain = this.Database.Load(null);
            Assert.That(reloadedAgain.Accounts.FindAccount("Checking"), Is.Null);
        }

        private static MyMoney BuildOnePayeeMoney(out Payee payee)
        {
            MyMoney money = new MyMoney();
            payee = money.Payees.FindPayee("Kroger", true);
            return money;
        }

        [Test]
        public void SaveOne_NewPayee_PersistsAndSetsRowVersionToOne()
        {
            BuildOnePayeeMoney(out Payee payee);

            this.Database.SaveOne(payee);

            Assert.That(payee.RowVersion, Is.EqualTo(1));
            Assert.That(payee.IsInserted, Is.False);
            Assert.That(payee.IsChanged, Is.False);

            MyMoney reloaded = this.Database.Load(null);
            Payee found = reloaded.Payees.FindPayee("Kroger", false);
            Assert.That(found, Is.Not.Null);
            Assert.That(found.RowVersion, Is.EqualTo(1));
        }

        [Test]
        public void SaveOne_UpdatePayeeAfterReload_IncrementsRowVersion()
        {
            BuildOnePayeeMoney(out Payee payee);
            this.Database.SaveOne(payee);

            MyMoney reloaded = this.Database.Load(null);
            Payee found = reloaded.Payees.FindPayee("Kroger", false);
            found.Name = "Kroger Updated";
            this.Database.SaveOne(found);

            Assert.That(found.RowVersion, Is.EqualTo(2));

            MyMoney reloadedAgain = this.Database.Load(null);
            Payee foundAgain = reloadedAgain.Payees.FindPayee("Kroger Updated", false);
            Assert.That(foundAgain, Is.Not.Null);
            Assert.That(foundAgain.RowVersion, Is.EqualTo(2));
        }

        [Test]
        public void SaveOne_StalePayeeRowVersion_ThrowsConcurrencyConflictException()
        {
            BuildOnePayeeMoney(out Payee payee);
            this.Database.SaveOne(payee);

            MyMoney readerA = this.Database.Load(null);
            MyMoney readerB = this.Database.Load(null);

            Payee payeeA = readerA.Payees.FindPayee("Kroger", false);
            payeeA.Name = "From A";
            this.Database.SaveOne(payeeA);

            Payee payeeB = readerB.Payees.FindPayee("Kroger", false);
            payeeB.Name = "From B";
            var ex = Assert.Throws<ConcurrencyConflictException>(() => this.Database.SaveOne(payeeB));
            Assert.That(ex.StoredRowVersion, Is.EqualTo(2));
            Assert.That(ex.CallerRowVersion, Is.EqualTo(1));

            MyMoney reloaded = this.Database.Load(null);
            Assert.That(reloaded.Payees.FindPayee("From A", false), Is.Not.Null);
        }

        [Test]
        public void SaveOne_DeletePayee_RemovesRowFromDatabaseAndContainer()
        {
            BuildOnePayeeMoney(out Payee payee);
            this.Database.SaveOne(payee);

            MyMoney reloaded = this.Database.Load(null);
            Payee toDelete = reloaded.Payees.FindPayee("Kroger", false);
            int toDeleteId = toDelete.Id;
            reloaded.Payees.RemovePayee(toDelete);
            Assert.That(toDelete.IsDeleted, Is.True);

            // RemovePayee(p, forceRemoveAfterSave: false) (its default here) removes the name-based
            // payeeIndex entry unconditionally, so FindPayee("Kroger", false) would already be null
            // here even if SaveOne's postCommit RemoveChild(p, true) never fires. FindPayeeAt(id)
            // indexes the id-keyed `payees` dictionary directly, only cleared when IsInserted ||
            // forceRemoveAfterSave, so it's the genuine proof.
            Assert.That(reloaded.Payees.FindPayeeAt(toDeleteId), Is.Not.Null,
                "sanity check: id-based lookup must still find the payee before SaveOne runs its postCommit RemoveChild");

            this.Database.SaveOne(toDelete);

            Assert.That(reloaded.Payees.FindPayeeAt(toDeleteId), Is.Null);

            MyMoney reloadedAgain = this.Database.Load(null);
            Assert.That(reloadedAgain.Payees.FindPayee("Kroger", false), Is.Null);
        }

        private static MyMoney BuildOneAliasMoney(out Payee payee, out Alias alias)
        {
            MyMoney money = new MyMoney();
            payee = money.Payees.FindPayee("Kroger", true);
            alias = new Alias(money.Aliases) { Payee = payee, Pattern = "KROGER", AliasType = AliasType.None };
            money.Aliases.AddAlias(alias);
            return money;
        }

        [Test]
        public void SaveOne_NewAlias_PersistsAndSetsRowVersionToOne()
        {
            BuildOneAliasMoney(out Payee payee, out Alias alias);
            this.Database.SaveOne(payee);

            this.Database.SaveOne(alias);

            Assert.That(alias.RowVersion, Is.EqualTo(1));
            Assert.That(alias.IsInserted, Is.False);
            Assert.That(alias.IsChanged, Is.False);

            MyMoney reloaded = this.Database.Load(null);
            Alias found = reloaded.Aliases.FindAlias("KROGER");
            Assert.That(found, Is.Not.Null);
            Assert.That(found.RowVersion, Is.EqualTo(1));
        }

        [Test]
        public void SaveOne_UpdateAliasAfterReload_IncrementsRowVersion()
        {
            BuildOneAliasMoney(out Payee payee, out Alias alias);
            this.Database.SaveOne(payee);
            this.Database.SaveOne(alias);

            MyMoney reloaded = this.Database.Load(null);
            Alias found = reloaded.Aliases.FindAlias("KROGER");
            found.Pattern = "KROGER UPDATED";
            this.Database.SaveOne(found);

            Assert.That(found.RowVersion, Is.EqualTo(2));

            MyMoney reloadedAgain = this.Database.Load(null);
            Alias foundAgain = reloadedAgain.Aliases.FindAlias("KROGER UPDATED");
            Assert.That(foundAgain, Is.Not.Null);
            Assert.That(foundAgain.RowVersion, Is.EqualTo(2));
        }

        [Test]
        public void SaveOne_StaleAliasRowVersion_ThrowsConcurrencyConflictException()
        {
            BuildOneAliasMoney(out Payee payee, out Alias alias);
            this.Database.SaveOne(payee);
            this.Database.SaveOne(alias);

            MyMoney readerA = this.Database.Load(null);
            MyMoney readerB = this.Database.Load(null);

            Alias aliasA = readerA.Aliases.FindAlias("KROGER");
            aliasA.Pattern = "From A";
            this.Database.SaveOne(aliasA);

            Alias aliasB = readerB.Aliases.FindAlias("KROGER");
            aliasB.Pattern = "From B";
            var ex = Assert.Throws<ConcurrencyConflictException>(() => this.Database.SaveOne(aliasB));
            Assert.That(ex.StoredRowVersion, Is.EqualTo(2));
            Assert.That(ex.CallerRowVersion, Is.EqualTo(1));

            MyMoney reloaded = this.Database.Load(null);
            Assert.That(reloaded.Aliases.FindAlias("From A"), Is.Not.Null);
        }

        [Test]
        public void SaveOne_DeleteAlias_RemovesRowFromDatabaseAndContainer()
        {
            BuildOneAliasMoney(out Payee payee, out Alias alias);
            this.Database.SaveOne(payee);
            this.Database.SaveOne(alias);

            MyMoney reloaded = this.Database.Load(null);
            Alias toDelete = reloaded.Aliases.FindAlias("KROGER");
            reloaded.Aliases.RemoveAlias(toDelete);
            Assert.That(toDelete.IsDeleted, Is.True);

            this.Database.SaveOne(toDelete);

            Assert.That(reloaded.Aliases.FindAlias("KROGER"), Is.Null);

            MyMoney reloadedAgain = this.Database.Load(null);
            Assert.That(reloadedAgain.Aliases.FindAlias("KROGER"), Is.Null);
        }

        private static MyMoney BuildOneSecurityMoney(out Security security)
        {
            MyMoney money = new MyMoney();
            security = money.Securities.FindSymbol("MSFT", true);
            security.Price = 100m;
            return money;
        }

        [Test]
        public void SaveOne_NewSecurity_PersistsAndSetsRowVersionToOne()
        {
            BuildOneSecurityMoney(out Security security);

            this.Database.SaveOne(security);

            Assert.That(security.RowVersion, Is.EqualTo(1));
            Assert.That(security.IsInserted, Is.False);
            Assert.That(security.IsChanged, Is.False);

            MyMoney reloaded = this.Database.Load(null);
            Security found = reloaded.Securities.FindSecurity("MSFT", false);
            Assert.That(found, Is.Not.Null);
            Assert.That(found.RowVersion, Is.EqualTo(1));
        }

        [Test]
        public void SaveOne_UpdateSecurityAfterReload_IncrementsRowVersion()
        {
            BuildOneSecurityMoney(out Security security);
            this.Database.SaveOne(security);

            MyMoney reloaded = this.Database.Load(null);
            Security found = reloaded.Securities.FindSecurity("MSFT", false);
            found.Price = 150m;
            this.Database.SaveOne(found);

            Assert.That(found.RowVersion, Is.EqualTo(2));

            MyMoney reloadedAgain = this.Database.Load(null);
            Security foundAgain = reloadedAgain.Securities.FindSecurity("MSFT", false);
            Assert.That(foundAgain.Price, Is.EqualTo(150m));
            Assert.That(foundAgain.RowVersion, Is.EqualTo(2));
        }

        [Test]
        public void SaveOne_StaleSecurityRowVersion_ThrowsConcurrencyConflictException()
        {
            BuildOneSecurityMoney(out Security security);
            this.Database.SaveOne(security);

            MyMoney readerA = this.Database.Load(null);
            MyMoney readerB = this.Database.Load(null);

            Security securityA = readerA.Securities.FindSecurity("MSFT", false);
            securityA.Price = 150m;
            this.Database.SaveOne(securityA);

            Security securityB = readerB.Securities.FindSecurity("MSFT", false);
            securityB.Price = 200m;
            var ex = Assert.Throws<ConcurrencyConflictException>(() => this.Database.SaveOne(securityB));
            Assert.That(ex.StoredRowVersion, Is.EqualTo(2));
            Assert.That(ex.CallerRowVersion, Is.EqualTo(1));

            MyMoney reloaded = this.Database.Load(null);
            Assert.That(reloaded.Securities.FindSecurity("MSFT", false).Price, Is.EqualTo(150m));
        }

        [Test]
        public void SaveOne_DeleteSecurity_RemovesRowFromDatabaseAndContainer()
        {
            BuildOneSecurityMoney(out Security security);
            this.Database.SaveOne(security);

            MyMoney reloaded = this.Database.Load(null);
            Security toDelete = reloaded.Securities.FindSecurity("MSFT", false);
            int toDeleteId = toDelete.Id;
            reloaded.Securities.RemoveSecurity(toDelete);
            Assert.That(toDelete.IsDeleted, Is.True);

            // RemoveSecurity(s, forceRemoveAfterSave: false) (its default here) removes the
            // name-based securityIndex entry unconditionally, so FindSecurity("MSFT", false) would
            // already be null here even if SaveOne's postCommit RemoveChild(s, true) never fires.
            // FindSecurityAt(id) indexes the id-keyed `securities` dictionary directly, only
            // cleared when IsInserted || forceRemoveAfterSave, so it's the genuine proof.
            Assert.That(reloaded.Securities.FindSecurityAt(toDeleteId), Is.Not.Null,
                "sanity check: id-based lookup must still find the security before SaveOne runs its postCommit RemoveChild");

            this.Database.SaveOne(toDelete);

            Assert.That(reloaded.Securities.FindSecurityAt(toDeleteId), Is.Null);

            MyMoney reloadedAgain = this.Database.Load(null);
            Assert.That(reloadedAgain.Securities.FindSecurity("MSFT", false), Is.Null);
        }

        private static MyMoney BuildOneStockSplitMoney(out Security security, out StockSplit stockSplit)
        {
            MyMoney money = new MyMoney();
            security = money.Securities.FindSymbol("MSFT", true);
            stockSplit = money.StockSplits.NewStockSplit();
            stockSplit.Security = security;
            stockSplit.Date = new DateTime(2020, 1, 1);
            stockSplit.Numerator = 2;
            stockSplit.Denominator = 1;
            return money;
        }

        [Test]
        public void SaveOne_NewStockSplit_PersistsAndSetsRowVersionToOne()
        {
            BuildOneStockSplitMoney(out Security security, out StockSplit stockSplit);
            this.Database.SaveOne(security);

            this.Database.SaveOne(stockSplit);

            Assert.That(stockSplit.RowVersion, Is.EqualTo(1));
            Assert.That(stockSplit.IsInserted, Is.False);
            Assert.That(stockSplit.IsChanged, Is.False);

            MyMoney reloaded = this.Database.Load(null);
            StockSplit found = reloaded.StockSplits.FindStockSplitById(stockSplit.Id);
            Assert.That(found, Is.Not.Null);
            Assert.That(found.RowVersion, Is.EqualTo(1));
        }

        [Test]
        public void SaveOne_UpdateStockSplitAfterReload_IncrementsRowVersion()
        {
            BuildOneStockSplitMoney(out Security security, out StockSplit stockSplit);
            this.Database.SaveOne(security);
            this.Database.SaveOne(stockSplit);
            long id = stockSplit.Id;

            MyMoney reloaded = this.Database.Load(null);
            StockSplit found = reloaded.StockSplits.FindStockSplitById(id);
            found.Numerator = 3;
            this.Database.SaveOne(found);

            Assert.That(found.RowVersion, Is.EqualTo(2));

            MyMoney reloadedAgain = this.Database.Load(null);
            StockSplit foundAgain = reloadedAgain.StockSplits.FindStockSplitById(id);
            Assert.That(foundAgain.Numerator, Is.EqualTo(3));
            Assert.That(foundAgain.RowVersion, Is.EqualTo(2));
        }

        [Test]
        public void SaveOne_StaleStockSplitRowVersion_ThrowsConcurrencyConflictException()
        {
            BuildOneStockSplitMoney(out Security security, out StockSplit stockSplit);
            this.Database.SaveOne(security);
            this.Database.SaveOne(stockSplit);
            long id = stockSplit.Id;

            MyMoney readerA = this.Database.Load(null);
            MyMoney readerB = this.Database.Load(null);

            StockSplit splitA = readerA.StockSplits.FindStockSplitById(id);
            splitA.Numerator = 3;
            this.Database.SaveOne(splitA);

            StockSplit splitB = readerB.StockSplits.FindStockSplitById(id);
            splitB.Numerator = 4;
            var ex = Assert.Throws<ConcurrencyConflictException>(() => this.Database.SaveOne(splitB));
            Assert.That(ex.StoredRowVersion, Is.EqualTo(2));
            Assert.That(ex.CallerRowVersion, Is.EqualTo(1));

            MyMoney reloaded = this.Database.Load(null);
            Assert.That(reloaded.StockSplits.FindStockSplitById(id).Numerator, Is.EqualTo(3));
        }

        [Test]
        public void SaveOne_DeleteStockSplit_RemovesRowFromDatabaseAndContainer()
        {
            BuildOneStockSplitMoney(out Security security, out StockSplit stockSplit);
            this.Database.SaveOne(security);
            this.Database.SaveOne(stockSplit);
            long id = stockSplit.Id;

            MyMoney reloaded = this.Database.Load(null);
            StockSplit toDelete = reloaded.StockSplits.FindStockSplitById(id);
            reloaded.StockSplits.RemoveStockSplit(toDelete);
            Assert.That(toDelete.IsDeleted, Is.True);

            this.Database.SaveOne(toDelete);

            Assert.That(reloaded.StockSplits.FindStockSplitById(id), Is.Null);

            MyMoney reloadedAgain = this.Database.Load(null);
            Assert.That(reloadedAgain.StockSplits.FindStockSplitById(id), Is.Null);
        }

        private static MyMoney BuildOneLoanPaymentMoney(out Account account, out LoanPayment loanPayment)
        {
            MyMoney money = new MyMoney();
            account = money.Accounts.AddAccount("Mortgage");
            loanPayment = new LoanPayment(money.LoanPayments)
            {
                AccountId = account.Id,
                Date = new DateTime(2020, 1, 1),
                Principal = 500m,
                Interest = 100m,
                Memo = "First payment"
            };
            money.LoanPayments.AddLoan(loanPayment);
            return money;
        }

        private static LoanPayment FindLoanPaymentById(MyMoney money, int id)
        {
            foreach (LoanPayment l in money.LoanPayments.GetList())
            {
                if (l.Id == id)
                {
                    return l;
                }
            }
            return null;
        }

        [Test]
        public void SaveOne_NewLoanPayment_PersistsAndSetsRowVersionToOne()
        {
            BuildOneLoanPaymentMoney(out Account account, out LoanPayment loanPayment);
            this.Database.SaveOne(account);

            this.Database.SaveOne(loanPayment);

            Assert.That(loanPayment.RowVersion, Is.EqualTo(1));
            Assert.That(loanPayment.IsInserted, Is.False);
            Assert.That(loanPayment.IsChanged, Is.False);

            MyMoney reloaded = this.Database.Load(null);
            LoanPayment found = FindLoanPaymentById(reloaded, loanPayment.Id);
            Assert.That(found, Is.Not.Null);
            Assert.That(found.RowVersion, Is.EqualTo(1));
        }

        [Test]
        public void SaveOne_UpdateLoanPaymentAfterReload_IncrementsRowVersion()
        {
            BuildOneLoanPaymentMoney(out Account account, out LoanPayment loanPayment);
            this.Database.SaveOne(account);
            this.Database.SaveOne(loanPayment);
            int id = loanPayment.Id;

            MyMoney reloaded = this.Database.Load(null);
            LoanPayment found = FindLoanPaymentById(reloaded, id);
            found.Memo = "Updated";
            this.Database.SaveOne(found);

            Assert.That(found.RowVersion, Is.EqualTo(2));

            MyMoney reloadedAgain = this.Database.Load(null);
            LoanPayment foundAgain = FindLoanPaymentById(reloadedAgain, id);
            Assert.That(foundAgain.Memo, Is.EqualTo("Updated"));
            Assert.That(foundAgain.RowVersion, Is.EqualTo(2));
        }

        [Test]
        public void SaveOne_StaleLoanPaymentRowVersion_ThrowsConcurrencyConflictException()
        {
            BuildOneLoanPaymentMoney(out Account account, out LoanPayment loanPayment);
            this.Database.SaveOne(account);
            this.Database.SaveOne(loanPayment);
            int id = loanPayment.Id;

            MyMoney readerA = this.Database.Load(null);
            MyMoney readerB = this.Database.Load(null);

            LoanPayment paymentA = FindLoanPaymentById(readerA, id);
            paymentA.Memo = "From A";
            this.Database.SaveOne(paymentA);

            LoanPayment paymentB = FindLoanPaymentById(readerB, id);
            paymentB.Memo = "From B";
            var ex = Assert.Throws<ConcurrencyConflictException>(() => this.Database.SaveOne(paymentB));
            Assert.That(ex.StoredRowVersion, Is.EqualTo(2));
            Assert.That(ex.CallerRowVersion, Is.EqualTo(1));

            MyMoney reloaded = this.Database.Load(null);
            Assert.That(FindLoanPaymentById(reloaded, id).Memo, Is.EqualTo("From A"));
        }

        [Test]
        public void SaveOne_DeleteLoanPayment_RemovesRowFromDatabaseAndContainer()
        {
            BuildOneLoanPaymentMoney(out Account account, out LoanPayment loanPayment);
            this.Database.SaveOne(account);
            this.Database.SaveOne(loanPayment);
            int id = loanPayment.Id;

            MyMoney reloaded = this.Database.Load(null);
            LoanPayment toDelete = FindLoanPaymentById(reloaded, id);
            reloaded.LoanPayments.Remove(toDelete);
            Assert.That(toDelete.IsDeleted, Is.True);

            // LoanPayments has no secondary name-style index (unlike Category/Payee/Security), so
            // Contains(item) - which checks the id-keyed dictionary directly - is unaffected by any
            // vacuous-clearing concern; it's cleared only when IsInserted || forceRemoveAfterSave.
            Assert.That(reloaded.LoanPayments.Contains(toDelete), Is.True,
                "sanity check: the loan payment must still be in the underlying dictionary before SaveOne runs its postCommit RemoveChild");

            this.Database.SaveOne(toDelete);

            Assert.That(reloaded.LoanPayments.Contains(toDelete), Is.False);

            MyMoney reloadedAgain = this.Database.Load(null);
            Assert.That(FindLoanPaymentById(reloadedAgain, id), Is.Null);
        }

        private static MyMoney BuildOneRentBuildingMoney(out RentBuilding building)
        {
            MyMoney money = new MyMoney();
            building = new RentBuilding(money.Buildings) { Name = "123 Main St" };
            money.Buildings.AddRentBuilding(building);
            return money;
        }

        [Test]
        public void SaveOne_NewRentBuilding_PersistsAndSetsRowVersionToOne()
        {
            BuildOneRentBuildingMoney(out RentBuilding building);

            this.Database.SaveOne(building);

            Assert.That(building.RowVersion, Is.EqualTo(1));
            Assert.That(building.IsInserted, Is.False);
            Assert.That(building.IsChanged, Is.False);

            MyMoney reloaded = this.Database.Load(null);
            RentBuilding found = reloaded.Buildings.FindByName("123 Main St");
            Assert.That(found, Is.Not.Null);
            Assert.That(found.RowVersion, Is.EqualTo(1));
        }

        [Test]
        public void SaveOne_UpdateRentBuildingAfterReload_IncrementsRowVersion()
        {
            BuildOneRentBuildingMoney(out RentBuilding building);
            this.Database.SaveOne(building);

            MyMoney reloaded = this.Database.Load(null);
            RentBuilding found = reloaded.Buildings.FindByName("123 Main St");
            found.Address = "Updated Address";
            this.Database.SaveOne(found);

            Assert.That(found.RowVersion, Is.EqualTo(2));

            MyMoney reloadedAgain = this.Database.Load(null);
            RentBuilding foundAgain = reloadedAgain.Buildings.FindByName("123 Main St");
            Assert.That(foundAgain.Address, Is.EqualTo("Updated Address"));
            Assert.That(foundAgain.RowVersion, Is.EqualTo(2));
        }

        [Test]
        public void SaveOne_StaleRentBuildingRowVersion_ThrowsConcurrencyConflictException()
        {
            BuildOneRentBuildingMoney(out RentBuilding building);
            this.Database.SaveOne(building);

            MyMoney readerA = this.Database.Load(null);
            MyMoney readerB = this.Database.Load(null);

            RentBuilding buildingA = readerA.Buildings.FindByName("123 Main St");
            buildingA.Address = "From A";
            this.Database.SaveOne(buildingA);

            RentBuilding buildingB = readerB.Buildings.FindByName("123 Main St");
            buildingB.Address = "From B";
            var ex = Assert.Throws<ConcurrencyConflictException>(() => this.Database.SaveOne(buildingB));
            Assert.That(ex.StoredRowVersion, Is.EqualTo(2));
            Assert.That(ex.CallerRowVersion, Is.EqualTo(1));

            MyMoney reloaded = this.Database.Load(null);
            Assert.That(reloaded.Buildings.FindByName("123 Main St").Address, Is.EqualTo("From A"));
        }

        [Test]
        public void SaveOne_DeleteRentBuilding_RemovesRowFromDatabaseAndContainer()
        {
            BuildOneRentBuildingMoney(out RentBuilding building);
            this.Database.SaveOne(building);

            MyMoney reloaded = this.Database.Load(null);
            RentBuilding toDelete = reloaded.Buildings.FindByName("123 Main St");
            reloaded.Buildings.RemoveBuilding(toDelete);
            Assert.That(toDelete.IsDeleted, Is.True);

            // RentBuildings.Contains checks the underlying dictionary directly (keyed by
            // GetUniqueKey(), i.e. the Id) with no separate name-based cache to worry about, unlike
            // Payee/Security/Currency - so it's a genuine proof either before or after SaveOne.
            Assert.That(reloaded.Buildings.Contains(toDelete), Is.True,
                "sanity check: the building must still be in the underlying dictionary before SaveOne runs its postCommit RemoveChild");

            this.Database.SaveOne(toDelete);

            Assert.That(reloaded.Buildings.Contains(toDelete), Is.False);

            MyMoney reloadedAgain = this.Database.Load(null);
            Assert.That(reloadedAgain.Buildings.FindByName("123 Main St"), Is.Null);
        }

        [Test]
        public void SaveOne_RentBuildingWithNewUnit_PersistsUnitAndSetsItClean()
        {
            BuildOneRentBuildingMoney(out RentBuilding building);
            this.Database.SaveOne(building);

            RentUnit unit = new RentUnit(((RentBuildings)building.Parent).Units) { Building = building.Id, Name = "Unit A", Renter = "Alice" };
            ((RentBuildings)building.Parent).Units.AddRentUnit(unit);

            this.Database.SaveOne(building);

            Assert.That(unit.IsInserted, Is.False);
            Assert.That(unit.IsChanged, Is.False);

            MyMoney reloaded = this.Database.Load(null);
            RentBuilding foundBuilding = reloaded.Buildings.FindByName("123 Main St");
            RentUnit foundUnit = reloaded.Buildings.Units.Get(unit.Id);
            Assert.That(foundUnit, Is.Not.Null);
            Assert.That(foundUnit.Name, Is.EqualTo("Unit A"));
            Assert.That(foundUnit.Renter, Is.EqualTo("Alice"));
            Assert.That(foundBuilding.Units, Has.One.Matches<RentUnit>(u => u.Id == foundUnit.Id));
        }

        [Test]
        public void SaveOne_RentBuildingWithUnitOnlyEdit_PersistsUnitEvenThoughBuildingItselfIsUnchanged()
        {
            BuildOneRentBuildingMoney(out RentBuilding building);
            this.Database.SaveOne(building);

            RentUnit unit = new RentUnit(((RentBuildings)building.Parent).Units) { Building = building.Id, Name = "Unit A" };
            ((RentBuildings)building.Parent).Units.AddRentUnit(unit);
            this.Database.SaveOne(building);

            MyMoney reloaded = this.Database.Load(null);
            RentBuilding reloadedBuilding = reloaded.Buildings.FindByName("123 Main St");
            RentUnit reloadedUnit = reloaded.Buildings.Units.Get(unit.Id);
            reloadedUnit.Renter = "Bob";
            Assert.That(reloadedBuilding.IsChanged, Is.False,
                "precondition: only the unit changed, not the building itself - this is what exercises " +
                "the 'process units regardless of the building's own change state' path.");

            this.Database.SaveOne(reloadedBuilding);

            MyMoney reloadedAgain = this.Database.Load(null);
            Assert.That(reloadedAgain.Buildings.Units.Get(unit.Id).Renter, Is.EqualTo("Bob"));
        }

        [Test]
        public void SaveOne_RentBuildingWithDeletedUnit_RemovesUnitRow()
        {
            BuildOneRentBuildingMoney(out RentBuilding building);
            this.Database.SaveOne(building);

            RentUnit unit = new RentUnit(((RentBuildings)building.Parent).Units) { Building = building.Id, Name = "Unit A" };
            ((RentBuildings)building.Parent).Units.AddRentUnit(unit);
            this.Database.SaveOne(building);
            int unitId = unit.Id;

            MyMoney reloaded = this.Database.Load(null);
            RentBuilding reloadedBuilding = reloaded.Buildings.FindByName("123 Main St");
            RentUnit toDelete = reloaded.Buildings.Units.Get(unitId);
            reloaded.Buildings.Units.RemoveRentUnit(toDelete);
            Assert.That(toDelete.IsDeleted, Is.True);
            Assert.That(reloaded.Buildings.Units.Contains(toDelete), Is.True,
                "sanity check: RemoveRentUnit(forceRemoveAfterSave: false) must not have removed it from the dictionary yet");

            this.Database.SaveOne(reloadedBuilding);

            Assert.That(reloaded.Buildings.Units.Contains(toDelete), Is.False);

            MyMoney reloadedAgain = this.Database.Load(null);
            Assert.That(reloadedAgain.Buildings.Units.Get(unitId), Is.Null);
        }

        private static MyMoney BuildOneTransactionMoney(out Account account, out Transaction transaction)
        {
            MyMoney money = new MyMoney();
            account = money.Accounts.AddAccount("Checking");
            transaction = money.Transactions.NewTransaction(account);
            transaction.Date = new DateTime(2020, 1, 1);
            transaction.Amount = -42.50m;
            transaction.Memo = "Test transaction";
            money.Transactions.AddTransaction(transaction);
            return money;
        }

        [Test]
        public void SaveOne_NewTransaction_PersistsAndSetsRowVersionToOne()
        {
            BuildOneTransactionMoney(out Account account, out Transaction transaction);
            this.Database.SaveOne(account);

            this.Database.SaveOne(transaction);

            Assert.That(transaction.RowVersion, Is.EqualTo(1));
            Assert.That(transaction.IsInserted, Is.False);
            Assert.That(transaction.IsChanged, Is.False);

            MyMoney reloaded = this.Database.Load(null);
            Account reloadedAccount = reloaded.Accounts.FindAccount("Checking");
            Transaction found = reloaded.Transactions.FindTransactionById(transaction.Id);
            Assert.That(found, Is.Not.Null);
            Assert.That(found.Amount, Is.EqualTo(-42.50m));
            Assert.That(found.RowVersion, Is.EqualTo(1));
        }

        [Test]
        public void SaveOne_UpdateTransactionAfterReload_IncrementsRowVersion()
        {
            BuildOneTransactionMoney(out Account account, out Transaction transaction);
            this.Database.SaveOne(account);
            this.Database.SaveOne(transaction);
            long id = transaction.Id;

            MyMoney reloaded = this.Database.Load(null);
            Transaction found = reloaded.Transactions.FindTransactionById(id);
            found.Memo = "Updated";
            this.Database.SaveOne(found);

            Assert.That(found.RowVersion, Is.EqualTo(2));

            MyMoney reloadedAgain = this.Database.Load(null);
            Transaction foundAgain = reloadedAgain.Transactions.FindTransactionById(id);
            Assert.That(foundAgain.Memo, Is.EqualTo("Updated"));
            Assert.That(foundAgain.RowVersion, Is.EqualTo(2));
        }

        [Test]
        public void SaveOne_StaleTransactionRowVersion_ThrowsConcurrencyConflictException()
        {
            BuildOneTransactionMoney(out Account account, out Transaction transaction);
            this.Database.SaveOne(account);
            this.Database.SaveOne(transaction);
            long id = transaction.Id;

            MyMoney readerA = this.Database.Load(null);
            MyMoney readerB = this.Database.Load(null);

            Transaction transactionA = readerA.Transactions.FindTransactionById(id);
            transactionA.Memo = "From A";
            this.Database.SaveOne(transactionA);

            Transaction transactionB = readerB.Transactions.FindTransactionById(id);
            transactionB.Memo = "From B";
            var ex = Assert.Throws<ConcurrencyConflictException>(() => this.Database.SaveOne(transactionB));
            Assert.That(ex.StoredRowVersion, Is.EqualTo(2));
            Assert.That(ex.CallerRowVersion, Is.EqualTo(1));

            MyMoney reloaded = this.Database.Load(null);
            Assert.That(reloaded.Transactions.FindTransactionById(id).Memo, Is.EqualTo("From A"));
        }

        [Test]
        public void SaveOne_DeleteTransaction_RemovesRowFromDatabaseAndContainer()
        {
            BuildOneTransactionMoney(out Account account, out Transaction transaction);
            this.Database.SaveOne(account);
            this.Database.SaveOne(transaction);
            long id = transaction.Id;

            MyMoney reloaded = this.Database.Load(null);
            Transaction toDelete = reloaded.Transactions.FindTransactionById(id);
            reloaded.Transactions.RemoveTransaction(toDelete);
            Assert.That(toDelete.IsDeleted, Is.True);

            this.Database.SaveOne(toDelete);

            Assert.That(reloaded.Transactions.FindTransactionById(id), Is.Null);

            MyMoney reloadedAgain = this.Database.Load(null);
            Assert.That(reloadedAgain.Transactions.FindTransactionById(id), Is.Null);
        }

        [Test]
        public void SaveOne_TransactionWithNewSplit_PersistsSplitAndSetsItClean()
        {
            BuildOneTransactionMoney(out Account account, out Transaction transaction);
            this.Database.SaveOne(account);

            Category category = transaction.MyMoney.Categories.GetOrCreateCategory("SplitCategory", CategoryType.Expense);
            this.Database.SaveOne(category);
            Split split = transaction.NonNullSplits.AddSplit();
            split.Amount = -20m;
            split.Category = category;
            split.Memo = "Split A";

            this.Database.SaveOne(transaction);

            Assert.That(split.IsInserted, Is.False);
            Assert.That(split.IsChanged, Is.False);

            MyMoney reloaded = this.Database.Load(null);
            Transaction foundTransaction = reloaded.Transactions.FindTransactionById(transaction.Id);
            Assert.That(foundTransaction.IsSplit, Is.True);
            Split foundSplit = foundTransaction.Splits.GetSplits()[0];
            Assert.That(foundSplit.Amount, Is.EqualTo(-20m));
            Assert.That(foundSplit.Memo, Is.EqualTo("Split A"));
        }

        [Test]
        public void SaveOne_TransactionWithSplitOnlyEdit_PersistsSplitEvenThoughTransactionItselfIsUnchanged()
        {
            BuildOneTransactionMoney(out Account account, out Transaction transaction);
            this.Database.SaveOne(account);
            Split split = transaction.NonNullSplits.AddSplit();
            split.Amount = -42.50m;
            split.Memo = "Original";
            this.Database.SaveOne(transaction);
            long id = transaction.Id;

            MyMoney reloaded = this.Database.Load(null);
            Transaction reloadedTransaction = reloaded.Transactions.FindTransactionById(id);
            Split reloadedSplit = reloadedTransaction.Splits.GetSplits()[0];
            reloadedSplit.Memo = "Edited";
            Assert.That(reloadedTransaction.IsChanged, Is.False,
                "precondition: only the split changed, not the transaction itself - this is what exercises " +
                "the 'process owned children regardless of the transaction's own change state' path.");

            this.Database.SaveOne(reloadedTransaction);

            MyMoney reloadedAgain = this.Database.Load(null);
            Assert.That(reloadedAgain.Transactions.FindTransactionById(id).Splits.GetSplits()[0].Memo, Is.EqualTo("Edited"));
        }

        [Test]
        public void SaveOne_DeleteTransactionWithSplits_RemovesSplitsBeforeTransactionRow()
        {
            BuildOneTransactionMoney(out Account account, out Transaction transaction);
            this.Database.SaveOne(account);
            Split split = transaction.NonNullSplits.AddSplit();
            split.Amount = -42.50m;
            this.Database.SaveOne(transaction);
            long id = transaction.Id;

            MyMoney reloaded = this.Database.Load(null);
            Transaction toDelete = reloaded.Transactions.FindTransactionById(id);
            reloaded.Transactions.RemoveTransaction(toDelete);
            Assert.That(toDelete.IsDeleted, Is.True);

            // This must not throw a foreign-key violation: Splits.Transaction is a real
            // [ColumnObjectMapping] FK to Transactions.Id (confirmed during design), so deleting
            // the parent row before its Splits would fail exactly like the SqlServerStoredProcDatabase
            // bug fixed earlier this session (FK_Splits_Transaction).
            Assert.DoesNotThrow(() => this.Database.SaveOne(toDelete));

            MyMoney reloadedAgain = this.Database.Load(null);
            Assert.That(reloadedAgain.Transactions.FindTransactionById(id), Is.Null);
        }

        [Test]
        public void SaveOne_TransactionWithInvestment_PersistsInvestment()
        {
            BuildOneTransactionMoney(out Account account, out Transaction transaction);
            this.Database.SaveOne(account);
            Security security = transaction.MyMoney.Securities.FindSymbol("MSFT", true);
            this.Database.SaveOne(security);

            Investment investment = transaction.GetOrCreateInvestment();
            investment.Security = security;
            investment.UnitPrice = 100m;
            investment.Units = 10m;
            investment.Type = InvestmentType.Buy;

            this.Database.SaveOne(transaction);

            MyMoney reloaded = this.Database.Load(null);
            Transaction found = reloaded.Transactions.FindTransactionById(transaction.Id);
            Assert.That(found.Investment, Is.Not.Null);
            Assert.That(found.Investment.Units, Is.EqualTo(10m));
            Assert.That(found.Investment.UnitPrice, Is.EqualTo(100m));
        }

        [Test]
        public void SaveTransfer_CommitsBothTransactionsTogether()
        {
            MyMoney money = new MyMoney();
            Account checking = money.Accounts.AddAccount("Checking");
            Account savings = money.Accounts.AddAccount("Savings");
            this.Database.SaveOne(checking);
            this.Database.SaveOne(savings);

            Transaction from = money.Transactions.NewTransaction(checking);
            from.Amount = -100m;
            money.Transactions.AddTransaction(from);
            Transaction to = money.Transactions.NewTransaction(savings);
            to.Amount = 100m;
            money.Transactions.AddTransaction(to);
            from.Transfer = new Transfer(from.Id, from, to);
            to.Transfer = new Transfer(to.Id, to, from);

            // Setting .Transfer auto-assigns .Payee to the well-known "Transfer" sentinel payee
            // (Transaction.Transfer's setter) - it must be persisted first, same as any other FK
            // dependency (Account, Category, etc.) every other test in this file saves before the
            // entity that references it.
            this.Database.SaveOne(money.Payees.Transfer);

            this.Database.SaveTransfer(from, to);

            Assert.That(from.RowVersion, Is.EqualTo(1));
            Assert.That(to.RowVersion, Is.EqualTo(1));

            MyMoney reloaded = this.Database.Load(null);
            Transaction reloadedFrom = reloaded.Transactions.FindTransactionById(from.Id);
            Transaction reloadedTo = reloaded.Transactions.FindTransactionById(to.Id);
            Assert.That(reloadedFrom.Transfer, Is.Not.Null);
            Assert.That(reloadedFrom.Transfer.Transaction.Id, Is.EqualTo(to.Id));
            Assert.That(reloadedTo.Transfer, Is.Not.Null);
            Assert.That(reloadedTo.Transfer.Transaction.Id, Is.EqualTo(from.Id));
        }

        [Test]
        public void SaveTransfer_ConflictOnOneSideRollsBackBoth()
        {
            MyMoney money = new MyMoney();
            Account checking = money.Accounts.AddAccount("Checking");
            Account savings = money.Accounts.AddAccount("Savings");
            this.Database.SaveOne(checking);
            this.Database.SaveOne(savings);
            Transaction from = money.Transactions.NewTransaction(checking);
            from.Amount = -100m;
            money.Transactions.AddTransaction(from);
            this.Database.SaveOne(from);

            MyMoney reader = this.Database.Load(null);
            Transaction staleFrom = reader.Transactions.FindTransactionById(from.Id);

            MyMoney otherWriter = this.Database.Load(null);
            Transaction otherFrom = otherWriter.Transactions.FindTransactionById(from.Id);
            otherFrom.Memo = "Changed elsewhere";
            this.Database.SaveOne(otherFrom);

            staleFrom.Memo = "Attempted";
            Transaction newTo = reader.Transactions.NewTransaction(reader.Accounts.FindAccount("Savings"));
            newTo.Amount = 100m;
            reader.Transactions.AddTransaction(newTo);

            // newTo is listed FIRST deliberately - same reasoning as SaveBatch_OneStaleRootAmongMany:
            // this only genuinely proves rollback if newTo's own write already "succeeded" within the
            // transaction before staleFrom's conflict is detected.
            Assert.Throws<ConcurrencyConflictException>(() => this.Database.SaveTransfer(newTo, staleFrom));

            MyMoney reloaded = this.Database.Load(null);
            Assert.That(reloaded.Transactions.FindTransactionById(newTo.Id), Is.Null);
            Assert.That(reloaded.Transactions.FindTransactionById(from.Id).Memo, Is.EqualTo("Changed elsewhere"));
        }

        [Test]
        public void Backup_ChecksPointsWalBeforeCopying_BackupContainsMostRecentCommit()
        {
            string backupPath = Path.Combine(Path.GetTempPath(), $"ContractTest_Backup_{Guid.NewGuid():N}.mmdb");
            string backupWalPath = backupPath + "-wal";
            string backupShmPath = backupPath + "-shm";
            try
            {
                BuildOneCategoryMoney(out Category category);
                this.Database.SaveOne(category);

                // Deliberately do NOT Disconnect() before Backup(): under WAL mode the just-committed
                // row lives only in the "-wal" sidecar until something checkpoints it, and SQLite's
                // default wal_autocheckpoint threshold (1000 pages) is nowhere near reached by this
                // tiny amount of data - so nothing else would have flushed it. A raw File.Copy of the
                // main .mmdb file at this point (i.e. Backup() without the checkpoint fix) would copy
                // a file that's missing this row (or even the schema itself) - only Backup()'s own
                // "PRAGMA wal_checkpoint(TRUNCATE);" makes the main file self-contained before the copy.
                this.Database.Backup(backupPath);

                Assert.That(File.Exists(backupPath), Is.True);

                var backupDatabase = new SqliteDatabase { DatabasePath = backupPath };
                try
                {
                    MyMoney fromBackup = backupDatabase.Load(null);
                    Category found = fromBackup.Categories.FindCategory("Groceries");
                    Assert.That(found, Is.Not.Null,
                        "Backup() must checkpoint the WAL before copying, or the just-committed row " +
                        "would be missing from the backup file entirely.");
                    Assert.That(found.RowVersion, Is.EqualTo(1));
                }
                finally
                {
                    backupDatabase.Disconnect();
                }
            }
            finally
            {
                if (File.Exists(backupPath))
                {
                    File.Delete(backupPath);
                }
                if (File.Exists(backupWalPath))
                {
                    File.Delete(backupWalPath);
                }
                if (File.Exists(backupShmPath))
                {
                    File.Delete(backupShmPath);
                }
            }
        }
    }
}
