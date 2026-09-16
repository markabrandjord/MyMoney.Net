using NUnit.Framework;
using System.Data.SqlTypes;
using Walkabout.Data;

namespace Walkabout.Tests
{
    /// <summary>
    /// Summary description for DataTests
    /// </summary>
    public class SqlMappingTests
    {
        [SetUp]
        public void Setup()
        {
        }

        [Test]
        public void TestSqlMapping()
        {
            MyMoney m = new MyMoney();
            Account a = new Account();
            a.Name = "Bank of America";
            m.Accounts.AddAccount(a);
            Transaction t = new Transaction();
            t.Account = a;
            t.Date = DateTime.Now;
            t.Amount = -65.00M;
            t.Payee = m.Payees.FindPayee("Costco", true);
            t.Category = m.Categories.FindCategory("Food");
            t.Memo = "something";
            m.Transactions.AddTransaction(t);

            string dbPath = System.IO.Path.GetTempPath();
            string dbName = System.IO.Path.Combine(dbPath, "Test.MyMoney.db");
            if (System.IO.File.Exists(dbName))
            {
                System.IO.File.Delete(dbName);
            }

            SqliteDatabase db = new SqliteDatabase();
            db.DatabasePath = dbName;
            db.Create();
            db.Save(m);

            // test we can add a column to the Transactions table.
            TableMapping mapping = new TableMapping() { TableName = "Transactions" };
            mapping.ObjectType = typeof(Transaction);
            var c = new ColumnMapping()
            {
                ColumnName = "Foo",
                SqlType = typeof(SqlChars),
                AllowNulls = true,
                MaxLength = 20
            };
            mapping.Columns.Add(c);
            db.CreateOrUpdateTable(mapping);

            // make sure the new column exists
            var metadata = db.LoadTableMetadata(mapping.TableName);
            var d = (from i in metadata.Columns where i.ColumnName == "Foo" select i).FirstOrDefault();
            Assert.IsNotNull(d);
            Assert.AreEqual(20, d.MaxLength);

            // test we can change the max length
            c.MaxLength = 50;
            db.CreateOrUpdateTable(mapping);

            // make sure the new column has new length
            metadata = db.LoadTableMetadata(mapping.TableName);
            d = (from i in metadata.Columns where i.ColumnName == "Foo" select i).FirstOrDefault();
            Assert.IsNotNull(d);
            Assert.AreEqual(50, d.MaxLength);

            // test we can drop the column
            mapping.Columns.Remove(c);
            db.CreateOrUpdateTable(mapping);

            // verify it's gone!
            metadata = db.LoadTableMetadata(mapping.TableName);
            d = (from i in metadata.Columns where i.ColumnName == "Foo" select i).FirstOrDefault();
            Assert.IsNull(d);

            // test we can still load the modified database!
            MyMoney test = db.Load(null);

            Account b = test.Accounts.FindAccount(a.Name);
            Assert.IsNotNull(b);

            Transaction s = test.Transactions.GetTransactionsFrom(b).FirstOrDefault();
            Assert.IsNotNull(s);

            // Only comparing the Dates not the times
            Assert.AreEqual(t.Date.ToShortDateString(), s.Date.ToShortDateString());

            Assert.AreEqual(t.Amount, s.Amount);
            Assert.AreEqual(t.CategoryFullName, s.CategoryFullName);
            Assert.AreEqual(t.PayeeName, s.PayeeName);
            Assert.AreEqual(t.Memo, s.Memo);

        }
    }
}

namespace Walkabout.Data.Tests
{
    [TestFixture]
    public class SqlMappingTests
    {
        [TableMapping(TableName = "MappingTestTable")]
        private class FakeRow
        {
            [ColumnMapping(ColumnName = "Id", IsPrimaryKey = true)]
            public int Id { get; set; }
        }

        [Test]
        public void GetCreateTableScript_SqlServer_AppendsRowVersionColumn()
        {
            var mapping = new TableMapping { ObjectType = typeof(FakeRow) };
            string script = SqlServerDatabase.GetCreateTableScript(mapping, DbFlavor.SqlServer);
            Assert.That(script, Does.Contain("[RowVersion] ROWVERSION NOT NULL"));
        }

        [Test]
        public void GetCreateTableScript_Sqlite_AppendsIntegerVersionColumn()
        {
            var mapping = new TableMapping { ObjectType = typeof(FakeRow) };
            string script = SqlServerDatabase.GetCreateTableScript(mapping, DbFlavor.Sqlite);
            Assert.That(script, Does.Contain("[Version] INTEGER NOT NULL DEFAULT 1"));
            Assert.That(script, Does.Not.Contain("ROWVERSION"));
        }

        [TableMapping(TableName = "MappingTestParentTable")]
        private class FakeParentRow
        {
            [ColumnMapping(ColumnName = "Id", IsPrimaryKey = true)]
            public int Id { get; set; }
        }

        [TableMapping(TableName = "MappingTestChildTable")]
        private class FakeChildRow
        {
            [ColumnMapping(ColumnName = "Id", IsPrimaryKey = true)]
            public int Id { get; set; }

            [ColumnObjectMapping(ColumnName = "ParentId", KeyProperty = "Id")]
            public FakeParentRow Parent { get; set; }
        }

        [Test]
        public void GetCreateTableScript_DerivesForeignKeyFromColumnObjectMapping()
        {
            var mapping = new TableMapping { ObjectType = typeof(FakeChildRow) };
            string script = SqlServerDatabase.GetCreateTableScript(mapping, DbFlavor.SqlServer);
            Assert.That(script, Does.Contain("FOREIGN KEY ([ParentId]) REFERENCES [MappingTestParentTable]([Id])"));
        }

        [Test]
        public void GetCreateIndexScripts_EmitsIndexForColumnObjectMapping()
        {
            // NOTE: TableName must be set explicitly here (matching the pattern used at the top
            // of this file, e.g. `new TableMapping() { TableName = "Transactions" }`), because
            // TableMapping.ObjectType's setter (Mapping.cs) only derives Columns from the type's
            // reflected attributes, not TableName. TableName is only ever populated "for free"
            // when a TableMapping is obtained directly off a type via reflection as the actual
            // attribute instance (as SqlServerDatabase.Load does), not when constructed fresh as
            // here or in the sibling FK test above (which didn't need TableName for its assert).
            var mapping = new TableMapping { ObjectType = typeof(FakeChildRow), TableName = "MappingTestChildTable" };
            var scripts = SqlServerDatabase.GetCreateIndexScripts(mapping).ToList();
            Assert.That(scripts, Has.One.Matches<string>(s =>
                s.Contains("CREATE INDEX") && s.Contains("MappingTestChildTable") && s.Contains("ParentId")));
        }
    }
}
