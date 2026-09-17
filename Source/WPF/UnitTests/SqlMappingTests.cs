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
    public class SchemaGenerationTests
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
            // SQLite (and SQL CE) resolve FK targets at DML time, not DDL time, so they keep the
            // FOREIGN KEY constraint inline in the CREATE TABLE statement itself.
            var mapping = new TableMapping { ObjectType = typeof(FakeChildRow) };
            string script = SqlServerDatabase.GetCreateTableScript(mapping, DbFlavor.Sqlite);
            Assert.That(script, Does.Contain("FOREIGN KEY ([ParentId]) REFERENCES [MappingTestParentTable]([Id])"));
        }

        [Test]
        public void GetCreateTableScript_SqlServer_OmitsInlineForeignKey()
        {
            // Real SQL Server is DDL-strict about FOREIGN KEY targets existing at CREATE TABLE
            // time, and LazyCreateTables() creates tables in reflection order, which does not
            // guarantee dependency order. So for SqlServer, FK constraints must NOT be inlined
            // into CREATE TABLE -- they're added afterwards via GetAddForeignKeyScripts() instead
            // (see the companion test below).
            var mapping = new TableMapping { ObjectType = typeof(FakeChildRow) };
            string script = SqlServerDatabase.GetCreateTableScript(mapping, DbFlavor.SqlServer);
            Assert.That(script, Does.Not.Contain("FOREIGN KEY"));
        }

        [Test]
        public void GetAddForeignKeyScripts_DerivesForeignKeyFromColumnObjectMapping()
        {
            var mapping = new TableMapping { ObjectType = typeof(FakeChildRow), TableName = "MappingTestChildTable" };
            var scripts = SqlServerDatabase.GetAddForeignKeyScripts(mapping).ToList();
            Assert.That(scripts, Has.One.Matches<string>(s =>
                s.Contains("ALTER TABLE [MappingTestChildTable]") &&
                s.Contains("FOREIGN KEY ([ParentId]) REFERENCES [MappingTestParentTable]([Id])")));
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

    [TestFixture]
    public class VersionColumnRetrofitTests
    {
        [TableMapping(TableName = "VersionRetrofitTestTable")]
        private class FakeVersionRetrofitRow
        {
            [ColumnMapping(ColumnName = "Id", IsPrimaryKey = true)]
            public int Id { get; set; }

            [ColumnMapping(ColumnName = "Name", MaxLength = 50)]
            public string Name { get; set; }
        }

        [Test]
        public void CreateOrUpdateTable_Sqlite_RetrofitsMissingVersionColumnWithDefaultOfOne()
        {
            string dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"VersionRetrofitTest_{Guid.NewGuid():N}.mmdb");
            if (System.IO.File.Exists(dbPath))
            {
                System.IO.File.Delete(dbPath);
            }

            var db = new SqliteDatabase { DatabasePath = dbPath };
            try
            {
                db.Create();

                // Simulate a database file that predates the Version-column retrofit (persistence-
                // concurrency Phase 1 Task 3 added Version only to GetCreateTableScript's freshly
                // CREATEd tables): create the table directly via raw SQL, deliberately WITHOUT a
                // Version column, and insert a row into it BEFORE CreateOrUpdateTable ever runs -
                // exactly what an on-disk file saved before that change looks like. Every other
                // column matches FakeVersionRetrofitRow's mapping exactly (same nullability/type),
                // and each column is on its own line (matching GetCreateTableScript's own
                // formatting - GetTableSchema's CREATE-TABLE-text parser assumes the opening
                // "create table (" line carries no column of its own), so CreateOrUpdateTable takes
                // the simple in-place ALTER TABLE path instead of the newTable rebuild path - this
                // test is specifically about the ALTER-path retrofit, not the separate (and
                // separately tracked, see the plan's "What's next") newTable-rebuild Version-loss
                // gap.
                db.ExecuteNonQuery(
                    "CREATE TABLE [VersionRetrofitTestTable] (\r\n" +
                    "  [Id] int PRIMARY KEY,\r\n" +
                    "  [Name] nvarchar(50) NOT NULL\r\n" +
                    ");");
                db.ExecuteNonQuery("INSERT INTO [VersionRetrofitTestTable] ([Id],[Name]) VALUES (1,'pre-existing row');");

                TableMapping beforeMetadata = db.LoadTableMetadata("VersionRetrofitTestTable");
                Assert.That(beforeMetadata.FindColumn("Id"), Is.Not.Null,
                    "Precondition: the raw CREATE TABLE must parse with both real columns present (guards against silently mis-formatting the raw SQL).");
                Assert.That(beforeMetadata.FindColumn("Version"), Is.Null,
                    "Precondition: the simulated pre-existing table must not already have a Version column.");

                var mapping = new TableMapping { ObjectType = typeof(FakeVersionRetrofitRow), TableName = "VersionRetrofitTestTable" };
                db.CreateOrUpdateTable(mapping);

                TableMapping afterMetadata = db.LoadTableMetadata("VersionRetrofitTestTable");
                ColumnMapping versionColumn = afterMetadata.FindColumn("Version");
                Assert.That(versionColumn, Is.Not.Null,
                    "CreateOrUpdateTable must retrofit the Version column onto a pre-existing table that lacks it.");
                Assert.That(versionColumn.AllowNulls, Is.False);

                // Confirm DEFAULT 1 actually applied to the pre-existing row, not just to the new
                // column's metadata - this is what SqliteDatabase.ReadCategories/SaveOneCategory
                // depend on to have a sane starting RowVersion for rows that existed before Phase 2b.
                object versionValue = db.ExecuteScalar("SELECT [Version] FROM [VersionRetrofitTestTable] WHERE [Id]=1;");
                Assert.That(Convert.ToInt64(versionValue), Is.EqualTo(1));
            }
            finally
            {
                db.Delete();
            }
        }
    }
}
