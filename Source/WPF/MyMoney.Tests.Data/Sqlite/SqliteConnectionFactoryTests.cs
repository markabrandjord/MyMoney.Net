using System;
using System.Data.SQLite;
using System.IO;
using NUnit.Framework;
using Walkabout.Data.Sqlite.Provisioning;

namespace Walkabout.Tests.Data.Sqlite
{
    [TestFixture]
    public class SqliteConnectionFactoryTests
    {
        private static string Scalar(SQLiteConnection c, string sql)
        {
            using (var cmd = new SQLiteCommand(sql, c))
            {
                return Convert.ToString(cmd.ExecuteScalar());
            }
        }

        [Test]
        public void Open_OnAFile_AppliesWalForeignKeysAndBusyTimeout()
        {
            string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid() + ".mmdb");
            try
            {
                using (SQLiteConnection c = SqliteConnectionFactory.Open(path))
                {
                    Assert.That(Scalar(c, "PRAGMA journal_mode;"), Is.EqualTo("wal").IgnoreCase);
                    Assert.That(Scalar(c, "PRAGMA foreign_keys;"), Is.EqualTo("1"));
                    Assert.That(Scalar(c, "PRAGMA busy_timeout;"), Is.EqualTo("5000"));
                }
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void Open_OnAnInMemoryDatabase_SucceedsAndDoesNotDemandWal()
        {
            // PRAGMA journal_mode=WAL is not applicable to an in-memory database - it reports
            // "memory". T-1 makes :memory: the default business-test store, so a factory that
            // treated a non-wal result as failure would surface as a confusing first-run error.
            // Spec section 2.4, "Known gotcha, from reading Connect()".
            using (SQLiteConnection c = SqliteConnectionFactory.Open(SqliteConnectionFactory.InMemoryDataSource))
            {
                Assert.That(Scalar(c, "PRAGMA journal_mode;"), Is.EqualTo("memory").IgnoreCase);
                Assert.That(Scalar(c, "PRAGMA foreign_keys;"), Is.EqualTo("1"));
            }
        }

        [Test]
        public void TheBundledEngineSupportsEverythingTheDesignAdopts()
        {
            // RETURNING (3.35), json_each (3.38), STRICT (3.37), PRAGMA table_list (3.37),
            // VACUUM INTO (3.27). Spec section 0.1 verified 3.53.4; this keeps that true.
            using (SQLiteConnection c = SqliteConnectionFactory.Open(SqliteConnectionFactory.InMemoryDataSource))
            {
                var version = new Version(Scalar(c, "SELECT sqlite_version();"));

                Assert.That(version, Is.GreaterThanOrEqualTo(new Version(3, 38, 0)));
            }
        }

        [Test]
        public void IsInMemory_RecognisesTheInMemoryDataSource()
        {
            Assert.That(SqliteConnectionFactory.IsInMemory(":memory:"), Is.True);
            Assert.That(SqliteConnectionFactory.IsInMemory(@"C:\books.mmdb"), Is.False);
        }
    }
}
