using System;
using System.Data;
using System.IO;
using System.Threading.Tasks;
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

        [Test]
        public void ConcurrentSaveOne_FromTwoConnections_BothSucceedViaBusyTimeoutRetry()
        {
            const int iterationsPerThread = 20;

            // Explicit, well-separated Id ranges per thread - deliberately bypassing the
            // auto-incrementing nextCategory counter (which is NOT safe across concurrent writers;
            // see docs/superpowers/plans/2026-09-16-persistence-concurrency-phase2b.md's "What's
            // next": Id allocation across concurrent writers is a known, separate, out-of-scope
            // problem). Without this, both threads would independently start their own nextCategory
            // counter at the same value and collide on a UNIQUE constraint violation - a real
            // failure, but the WRONG one for this test to prove: this test exists to prove
            // busy_timeout absorbs write-lock contention, not to (re-)prove the already-known,
            // already-deferred Id-collision gap.
            void InsertCategories(string prefix, int idBase)
            {
                SqliteDatabase database = new SqliteDatabase { DatabasePath = this.path };
                try
                {
                    for (int i = 0; i < iterationsPerThread; i++)
                    {
                        MyMoney money = new MyMoney();
                        Category category = new Category(money.Categories)
                        {
                            Id = idBase + i,
                            Name = $"{prefix}-{i}",
                            Type = CategoryType.Expense
                        };
                        money.Categories.AddCategory(category);
                        database.SaveOne(category);
                    }
                }
                finally
                {
                    database.Disconnect();
                }
            }

            Task taskA = Task.Run(() => InsertCategories("ThreadA", 100000));
            Task taskB = Task.Run(() => InsertCategories("ThreadB", 200000));

            // Real WAL-mode SQLite write contention across two independent connections to the same
            // file - 40 total inserts racing for the same exclusive write lock across two threads
            // makes at least one real contention event virtually certain (a throughput-based stress
            // design, not a precise-timing race, so it isn't fragile). If busy_timeout weren't wired
            // up (or were too short), at least one of these inserts would surface a raw
            // SQLiteException (SQLITE_BUSY/SQLITE_LOCKED) instead of transparently retrying and
            // succeeding. Task.WaitAll re-throws any task exception, wrapped in AggregateException.
            Assert.DoesNotThrow(() => Task.WaitAll(taskA, taskB));

            MyMoney reloaded = this.Database.Load(null);
            for (int i = 0; i < iterationsPerThread; i++)
            {
                Assert.That(reloaded.Categories.FindCategory($"ThreadA-{i}"), Is.Not.Null,
                    "every ThreadA insert must have actually landed, not just avoided throwing");
                Assert.That(reloaded.Categories.FindCategory($"ThreadB-{i}"), Is.Not.Null,
                    "every ThreadB insert must have actually landed, not just avoided throwing");
            }
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
