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
