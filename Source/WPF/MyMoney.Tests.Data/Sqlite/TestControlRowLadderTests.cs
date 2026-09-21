using System;
using System.Data.SQLite;
using System.Linq;
using MyMoney.TestKit;
using MyMoney.TestKit.Contracts;
using NUnit.Framework;
using Walkabout.Data;

namespace Walkabout.Tests.Data.Sqlite
{
    [TestFixture]
    public class TestControlRowLadderTests
    {
        private InMemorySqliteStore fixture;
        private Accounts container;

        [SetUp]
        public void SetUp()
        {
            this.fixture = InMemorySqliteStore.Create();
            this.container = new Accounts((PersistentObject)null);
        }

        [TearDown]
        public void TearDown() => this.fixture?.Dispose();

        private IMoneyStoreTestControl Control => this.fixture.TestControl;

        private Account Save(string name)
        {
            var a = new Account(this.container)
            {
                Id = checked((int)this.fixture.Query.NextAccountId()),
                Name = name, Type = AccountType.Checking, Currency = "USD", OpeningBalance = 12.34m
            };
            this.fixture.Store.SaveRoot(a);
            return a;
        }

        [Test]
        public void CaptureRow_ReturnsEveryColumnIncludingIdAndVersion()
        {
            Account a = this.Save("Checking");

            RowSnapshot snapshot = this.Control.CaptureRow(TableRef.Accounts, a.Id);

            Assert.That(snapshot.Id, Is.EqualTo(a.Id));
            Assert.That(snapshot.Values["Name"], Is.EqualTo("Checking"));
            Assert.That(Convert.ToInt64(snapshot.Values["Version"]), Is.EqualTo(1));
            Assert.That(snapshot.Values.ContainsKey("OpeningBalance"), Is.True);
        }

        [Test]
        public void CaptureRow_OnAnAbsentRow_Throws_AndTryCaptureRow_ReturnsNull()
        {
            Assert.Throws<StoreTestControlException>(() => this.Control.CaptureRow(TableRef.Accounts, 404));
            Assert.That(this.Control.TryCaptureRow(TableRef.Accounts, 404), Is.Null);
        }

        [Test]
        public void DeleteRow_RemovesTheRowWithNoVersionCheck()
        {
            Account a = this.Save("Checking");

            // No version is supplied and none is consulted - the fixture does not have to know
            // what version a row is at in order to remove it. Spec section 2.6.3.
            this.Control.DeleteRow(TableRef.Accounts, a.Id);

            Assert.That(this.Control.RowCount(TableRef.Accounts), Is.EqualTo(0));
        }

        [Test]
        public void DeleteRow_OnAnAbsentRow_IsASilentNoOp()
        {
            // Spec section 2.6.8's one explicit tightening: a reset call site can call DeleteRow
            // unconditionally, without first checking existence, and stay idempotent.
            Assert.DoesNotThrow(() => this.Control.DeleteRow(TableRef.Accounts, 404));
            Assert.DoesNotThrow(() => this.Control.DeleteRow(TableRef.Accounts, 404));
        }

        [Test]
        public void DeleteRow_DoesNotCascade_AndRollsBackOnAForeignKeyViolation()
        {
            using (var cmd = new SQLiteCommand(
                "INSERT INTO OnlineAccounts (Id, Name) VALUES (1, 'bank');", this.fixture.Connection))
            {
                cmd.ExecuteNonQuery();
            }

            Account a = this.Save("Checking");
            using (var cmd = new SQLiteCommand(
                "UPDATE Accounts SET OnlineAccount = 1 WHERE Id = @id;", this.fixture.Connection))
            {
                cmd.Parameters.AddWithValue("@id", a.Id);
                cmd.ExecuteNonQuery();
            }

            Assert.Throws<StoreTestControlException>(() => this.Control.DeleteRow(TableRef.OnlineAccounts, 1));
            Assert.That(this.Control.RowCount(TableRef.OnlineAccounts), Is.EqualTo(1));
            Assert.That(this.Control.RowCount(TableRef.Accounts), Is.EqualTo(1));
        }

        [Test]
        public void RestoreRow_ReInsertsVerbatimIncludingIdAndVersion()
        {
            Account a = this.Save("Checking");
            a.Name = "Renamed";
            this.fixture.Store.SaveRoot(a);          // version is now 2
            RowSnapshot snapshot = this.Control.CaptureRow(TableRef.Accounts, a.Id);
            this.Control.DeleteRow(TableRef.Accounts, a.Id);

            this.Control.RestoreRow(snapshot);

            Account restored = this.fixture.Store.LoadAccounts().Single();
            Assert.That(restored.Id, Is.EqualTo(a.Id));
            Assert.That(restored.Name, Is.EqualTo("Renamed"));
            Assert.That(restored.RowVersion, Is.EqualTo(2),
                "A restored row that came back with a bumped version would make the "
                + "version-conflict tests unreproducible - spec 2.6.5 point 2.");
        }

        [Test]
        public void RestoreRow_Twice_Throws_BecauseItIsSingleShotByDesign()
        {
            Account a = this.Save("Checking");
            RowSnapshot snapshot = this.Control.CaptureRow(TableRef.Accounts, a.Id);
            this.Control.DeleteRow(TableRef.Accounts, a.Id);
            this.Control.RestoreRow(snapshot);

            Assert.Throws<StoreTestControlException>(() => this.Control.RestoreRow(snapshot));
        }

        [Test]
        public void RemoveRow_HidesTheRowForTheScopeAndPutsItBackByteForByte()
        {
            Account a = this.Save("Checking");
            this.Save("Savings");

            using (IRowScope scope = this.Control.RemoveRow(TableRef.Accounts, a.Id))
            {
                Assert.That(this.fixture.Query.ListAccounts(AccountQuery.All).Select(r => r.Name),
                    Is.EqualTo(new[] { "Savings" }));
                Assert.That(scope.Removed.Values["Name"], Is.EqualTo("Checking"));
            }

            Assert.That(this.fixture.Query.ListAccounts(AccountQuery.All).Select(r => r.Name),
                Is.EqualTo(new[] { "Checking", "Savings" }));
            Assert.That(this.fixture.Store.LoadAccounts().Single(x => x.Id == a.Id).RowVersion, Is.EqualTo(1));
        }

        [Test]
        public void RemoveRow_DisposedTwice_RestoresOnlyOnce()
        {
            Account a = this.Save("Checking");
            IRowScope scope = this.Control.RemoveRow(TableRef.Accounts, a.Id);
            scope.Dispose();

            Assert.DoesNotThrow(() => scope.Dispose());
            Assert.That(this.Control.RowCount(TableRef.Accounts), Is.EqualTo(1));
        }

        [Test]
        public void RemoveRow_WhenTheRestoreCannotSucceed_Throws_RatherThanSwallowing()
        {
            // A silently-failed restore leaks state into every subsequent test in the fixture -
            // the exact failure mode AppCrashGuard was written to stop tolerating elsewhere in
            // this project. Spec section 2.6.5 point 3.
            Account a = this.Save("Checking");
            IRowScope scope = this.Control.RemoveRow(TableRef.Accounts, a.Id);

            // Someone re-created the row while it was "removed", so restoring it collides.
            this.fixture.Store.SaveRoot(new Account(this.container)
            {
                Id = a.Id, Name = "Impostor", Type = AccountType.Cash, OpeningBalance = 0m
            });

            Assert.Throws<StoreTestControlException>(() => scope.Dispose());
        }
    }
}
