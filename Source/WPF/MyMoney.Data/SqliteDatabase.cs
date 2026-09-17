// #define DEBUG_DATABASE
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Data.SQLite;
using System.Data.SqlTypes;
using System.Diagnostics;
using System.IO;
using System.Text;
using Walkabout.Utilities;

namespace Walkabout.Data
{
    public class SqliteDatabase : SqlServerDatabase
    {
        public SqliteDatabase()
        {
        }

        /// <summary>
        /// Return true if SQL CE is installed.
        /// </summary>
        public static bool IsSqliteInstalled
        {
            get
            {
                return true;
            }
        }

        public static string OfficialSqliteFileExtension = ".mmdb";

        private SQLiteConnection sqliteConnection;

        protected override string VersionColumnName { get { return "Version"; } }


        // BugBug: there's some sort of horrible exponential performance bug in the System.Data.Sqlite wrappers.
        // so for now we have to return false, even though that too is slow.
        public override bool SupportsBatchUpdate { get { return false; } }

        public override bool SupportsParameterizedUpdate { get { return true; } }

        protected override string GetConnectionString(bool includeDatabase)
        {
            SQLiteConnectionStringBuilder builder = new SQLiteConnectionStringBuilder();
            builder.DataSource = this.DatabasePath;
            if (!string.IsNullOrEmpty(this.Password))
            {
                builder.Password = this.Password;
            }
            return builder.ConnectionString;
        }

        public override void OnPasswordChanged(string oldPassword, string newPassword)
        {
            this.Connect();
            this.sqliteConnection.ChangePassword(newPassword);

            // if ChangePassword succeeds the next operation needs a new connection with the new password.
            this.Disconnect();
        }

        public override string GetDatabaseFullPath()
        {
            return this.DatabasePath;
        }

        public override void Create()
        {
            string connectionString = this.GetConnectionString(true);

            if (!File.Exists(this.DatabasePath))
            {
                this.LazyCreateTables();
            }
        }

        public override void Delete()
        {
            this.Disconnect();
            if (File.Exists(this.DatabasePath))
            {
                File.Delete(this.DatabasePath);
            }
            string walPath = this.DatabasePath + "-wal";
            if (File.Exists(walPath))
            {
                File.Delete(walPath);
            }
            string shmPath = this.DatabasePath + "-shm";
            if (File.Exists(shmPath))
            {
                File.Delete(shmPath);
            }
        }

        public static SqliteDatabase Restore(string backup, string databaseFile, string password)
        {
            string fullBackupPath = Path.GetFullPath(backup);
            string fullDatabasePath = Path.GetFullPath(databaseFile);

            var result = new SqliteDatabase()
            {
                DatabasePath = fullBackupPath,
                Password = password
            };

            result.Connect(); // make sure we can connect to it.
            result.Disconnect();

            // Clear any stale WAL/SHM sidecars at the target before overwriting the main file -
            // otherwise SQLite could replay a stale WAL from a previous session against the
            // freshly restored database.
            string targetWalPath = fullDatabasePath + "-wal";
            if (File.Exists(targetWalPath))
            {
                File.Delete(targetWalPath);
            }
            string targetShmPath = fullDatabasePath + "-shm";
            if (File.Exists(targetShmPath))
            {
                File.Delete(targetShmPath);
            }

            // Ok, then we're good to copy it.
            File.Copy(fullBackupPath, fullDatabasePath, true);

            return new SqliteDatabase()
            {
                DatabasePath = fullDatabasePath,
                Password = password,
                BackupPath = fullBackupPath
            };
        }

        public override DbFlavor DbFlavor
        {
            get { return Data.DbFlavor.Sqlite; }
        }

        public override bool UpgradeRequired
        {
            get
            {
                return false;
            }
        }

        public override bool SupportsUserLogin => false;

        public override void Upgrade()
        {
            // TBD
        }

        public override DbConnection Connect()
        {
            if (this.sqliteConnection == null || this.sqliteConnection.State != ConnectionState.Open)
            {
                string constr = this.GetConnectionString(true);
                this.sqliteConnection = new SQLiteConnection(constr);
                this.sqliteConnection.Open();
                using (var pragmaCommand = new SQLiteCommand("PRAGMA foreign_keys = ON;", this.sqliteConnection))
                {
                    pragmaCommand.ExecuteNonQuery();
                }
                // WAL mode: readers never block writers, writers never block readers - the
                // opposite of the default rollback-journal mode, which serializes all access.
                // busy_timeout: a second writer retries for up to this many milliseconds instead
                // of failing immediately with SQLITE_BUSY. 5000ms is a starting default (not
                // spec-mandated - a same-machine local agent contending with an interactive user
                // should resolve well within this window; revisit if real contention testing
                // shows it's too short or needlessly long). See
                // docs/superpowers/specs/2026-09-16-persistence-concurrency-design.md, R3.
                using (var walCommand = new SQLiteCommand("PRAGMA journal_mode=WAL;", this.sqliteConnection))
                {
                    walCommand.ExecuteNonQuery();
                }
                using (var busyTimeoutCommand = new SQLiteCommand("PRAGMA busy_timeout=5000;", this.sqliteConnection))
                {
                    busyTimeoutCommand.ExecuteNonQuery();
                }
            }
            return this.sqliteConnection;
        }

        public override void Disconnect()
        {
            if (this.sqliteConnection != null && this.sqliteConnection.State == ConnectionState.Open)
            {
                try
                {
                    using (this.sqliteConnection)
                    {
                        this.sqliteConnection.Close();
                    }
                }
                catch { }
            }
            this.sqliteConnection = null;
        }

        public override bool Exists
        {
            get
            {
                return File.Exists(this.DatabasePath);
            }
        }

        public override bool TableExists(string name)
        {
            //object result = ExecuteScalar("select * from INFORMATION_SCHEMA.tables where table_name = '" + mapping.TableName + "'");
            object result = this.ExecuteScalar("SELECT tbl_name FROM sqlite_master where tbl_name='" + name + "'");
            return result != null;
        }

        /// <summary>
        /// Get the schema of the given table
        /// </summary>
        /// <param name="tableName"></param>
        /// <returns>Returns the list of columns found</returns>
        public override List<ColumnMapping> GetTableSchema(string tableName)
        {
            // get the CREATE TABLE statement, the second and subsequent lines are the column information.
            string sql = this.ExecuteScalar("select sql from sqlite_master where tbl_name='" + tableName + "'").ToString();

            // replace comma column separator with newline.
            sql = sql.Replace(",", "\r\n");

            List<ColumnMapping> columns = new List<ColumnMapping>();

            // parse it to find the columns and their type infomration.
            using (StringReader reader = new StringReader(sql))
            {
                bool first = true;
                for (string line = reader.ReadLine(); line != null; line = reader.ReadLine())
                {
                    if (!first)
                    {
                        line = line.Trim();
                        if (line == ")")
                        {
                            // done
                            break;
                        }
                        if (string.IsNullOrEmpty(line))
                        {
                            continue;
                        }

                        if (!line.StartsWith("["))
                        {
                            // Not a column definition -- a table-level constraint clause,
                            // e.g. the "FOREIGN KEY ([Col]) REFERENCES [Table]([Col])" lines
                            // GetCreateTableScript now emits per ColumnObjectMapping column
                            // (persistence-concurrency phase 1, Task 4). Every real column
                            // definition line starts with "[ColumnName]", so this is a safe
                            // way to skip constraint-only lines without a column to parse.
                            continue;
                        }

                        ColumnMapping c = this.ParseColumnSql(line.TrimEnd(new char[] { ' ', '\t', '\r', '\n', ',' }));
                        if (c != null)
                        {
                            columns.Add(c);
                        }
                    }
                    first = false;
                }
            }

            return columns;
        }

        /// <summary>
        /// Parse a line of SQL that creates a column and return the mapping for it.
        /// </summary>
        /// <param name="line"></param>
        /// <returns>A ColumnMapping</returns>
        private ColumnMapping ParseColumnSql(string line)
        {
            // eg:   [Id] int NOT NULL,

            ColumnMapping result = new ColumnMapping();
            result.AllowNulls = true;

            int state = 0;

            for (int i = 0, n = line.Length; i < n; i++)
            {
                string id = null;
                char ch = line[i];
                if (char.IsWhiteSpace(ch))
                {
                    // skip whitespace.
                    continue;
                }

                if (ch == '[')
                {
                    // scan for closing bracket.
                    int j = line.IndexOf(']', i);
                    if (j < 0)
                    {
                        return null;
                    }
                    id = line.Substring(i + 1, j - i - 1);
                    i = j;
                }
                else if (state == 2)
                {
                    // take rest of line then.
                    id = line.Substring(i, n - i);
                    i = n;
                }
                else
                {
                    int j = line.IndexOfAny(new char[] { ' ', '\t', '\r', '\n' }, i);
                    if (j < 0)
                    {
                        // take rest of line then.
                        id = line.Substring(i, n - i);
                        i = n;
                    }
                    else
                    {
                        id = line.Substring(i, j - i);
                        i = j;
                    }
                }

                id = id.Trim();
                if (state == 0)
                {
                    result.ColumnName = id;
                    state++;
                }
                else if (state == 1)
                {
                    // ok, now we have a sql data type.
                    this.ParseSqlType(id, result);
                    state++;
                }
                else
                {
                    // NOT NULL
                    // PRIMARY KEY
                    if (id.ToUpperInvariant().Contains("NOT NULL"))
                    {
                        result.AllowNulls = false;
                    }
                    if (id.ToUpperInvariant().Contains("PRIMARY KEY"))
                    {
                        result.IsPrimaryKey = true;
                        result.AllowNulls = false;
                    }
                }
            }
            return result;
        }

        private void ParseSqlType(string type, ColumnMapping result)
        {
            // split nvarchar(20) into [nvarchar][20].
            // split decimal(8,12) into [decimal[9][12]
            string[] parts = type.Split('(', ',', ')');
            Type columnType = null;
            bool hasLength = false;
            bool hasPrecision = false;
            // Column types are matched case-insensitively: GetCreateTableScript emits the
            // application-maintained version column as uppercase "INTEGER" (SqlDatabase.cs),
            // while ColumnMapping.GetSqlDefinition emits everything else lowercase. Sqlite's
            // sqlite_master.sql stores the CREATE TABLE text verbatim, so GetTableSchema/
            // ParseColumnSql round-trip whatever case was originally written.
            switch (parts[0].ToLowerInvariant())
            {
                case "int":
                case "integer":
                case "numeric":
                    columnType = typeof(SqlInt32);
                    break;
                case "char":
                    columnType = typeof(SqlAscii);
                    hasLength = true;
                    break;
                case "nchar":
                case "nvarchar":
                    columnType = typeof(SqlChars);
                    hasLength = true;
                    break;
                case "money":
                    columnType = typeof(SqlMoney);
                    hasPrecision = true;
                    break;
                case "datetime":
                    columnType = typeof(SqlDateTime);
                    break;
                case "uniqueidentifier":
                    columnType = typeof(SqlGuid);
                    break;
                case "decimal":
                    columnType = typeof(SqlDecimal);
                    hasPrecision = true;
                    break;
                case "bigint":
                    columnType = typeof(SqlInt64);
                    break;
                case "smallint":
                    columnType = typeof(SqlInt16);
                    break;
                case "tinyint":
                    columnType = typeof(SqlByte);
                    break;
                case "float":
                    columnType = typeof(SqlSingle);
                    break;
                case "real":
                    columnType = typeof(SqlDouble);
                    break;
                case "bit":
                    columnType = typeof(SqlBoolean);
                    break;
                default:
                    throw new NotImplementedException(string.Format("SQL type '{0}' is not supported by the mapping engine", parts[0]));
            }
            result.SqlType = columnType;

            if (hasLength)
            {
                if (parts.Length > 1)
                {
                    int len = 0;
                    if (int.TryParse(parts[1], out len))
                    {
                        result.MaxLength = len;
                    }
                }
            }
            else if (hasPrecision)
            {
                if (parts.Length > 1)
                {
                    int precision = 0;
                    if (int.TryParse(parts[1], out precision))
                    {
                        result.Precision = precision;
                    }
                }
                if (parts.Length > 2)
                {
                    int scale = 0;
                    if (int.TryParse(parts[2], out scale))
                    {
                        result.Scale = scale;
                    }
                }
            }


        }

        public override object ExecuteScalar(string cmd)
        {
            Debug.Assert(this.DbFlavor == DbFlavor.Sqlite);

            if (cmd == null || cmd.Trim().Length == 0)
            {
                return null;
            }

            this.AppendLog(cmd);
            object result = null;
            try
            {
                this.Connect();
#if DEBUG_DATABASE
                Debug.WriteLine(string.Format("ExecuteScalar {0}", cmd));
#endif
                using (DbCommand command = new SQLiteCommand(cmd, this.sqliteConnection))
                {
                    result = command.ExecuteScalar();
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Error executing SQL \"" + cmd + "\"\n" + ex.Message);
            }
            return result;
        }

        public override DataSet QueryDataSet(string query)
        {
            Debug.Assert(this.DbFlavor == DbFlavor.Sqlite);

            if (string.IsNullOrWhiteSpace(query))
            {
                return null;
            }

            try
            {
                this.Connect();

                DataSet dataSet = new DataSet();

                using (DbDataAdapter da = new SQLiteDataAdapter(query, this.sqliteConnection))
                {
                    da.Fill(dataSet, "Results");
                }

                if (dataSet.Tables.Contains("Results"))
                {
                    return dataSet;
                }

            }
            catch (Exception)
            {
                throw; // useful for setting breakpoints.
            }
            return null;
        }

        public override void ExecuteNonQuery(string cmd)
        {
            Debug.Assert(this.DbFlavor == DbFlavor.Sqlite);
            if (cmd == null || cmd.Trim().Length == 0)
            {
                return;
            }

            this.AppendLog(cmd);
            try
            {
                this.Connect();
#if DEBUG_DATABASE
                Debug.WriteLine(string.Format("ExecuteNonQuery {0}", cmd));
#endif
                using (DbCommand command = new SQLiteCommand(cmd, this.sqliteConnection))
                {
                    command.ExecuteNonQuery();
                }
            }
            catch (Exception)
            {
                throw; // useful for setting breakpoints.
            }
        }

        public override void ExecuteNonQuery(string cmd, params (string Name, object Value)[] parameters)
        {
            Debug.Assert(this.DbFlavor == DbFlavor.Sqlite);
            if (cmd == null || cmd.Trim().Length == 0)
            {
                return;
            }

            this.AppendLog(cmd);
            try
            {
                this.Connect();
                using (DbCommand command = new SQLiteCommand(cmd, this.sqliteConnection))
                {
                    foreach (var (name, value) in parameters)
                    {
                        DbParameter p = command.CreateParameter();
                        p.ParameterName = name;
                        p.Value = value ?? DBNull.Value;
                        command.Parameters.Add(p);
                    }
                    command.ExecuteNonQuery();
                }
            }
            catch (Exception)
            {
                throw; // useful for setting breakpoints.
            }
        }

        public override IDataReader ExecuteReader(string cmd)
        {
            Debug.Assert(this.DbFlavor == DbFlavor.Sqlite);

            this.AppendLog(cmd);

            try
            {
                this.Connect();
#if DEBUG_DATABASE
                Debug.WriteLine(string.Format("ExecuteReader {0}", cmd));
#endif
                using (DbCommand command = new SQLiteCommand(cmd, this.sqliteConnection))
                {
                    return command.ExecuteReader();
                }
            }
            catch (Exception)
            {
                throw; // useful for setting breakpoints.
            }
        }


        public override void Backup(string backupPath)
        {
            this.BackupPath = backupPath;
            if (this.sqliteConnection != null && this.sqliteConnection.State == ConnectionState.Open)
            {
                // Under WAL mode, committed transactions can live in the "-wal" sidecar file until
                // a checkpoint happens - a raw File.Copy of just the main .mmdb file can silently
                // produce a backup missing the most recent transactions. Checkpoint (and truncate
                // the WAL back to empty) before copying so the main file is fully up to date.
                using (var checkpointCommand = new SQLiteCommand("PRAGMA wal_checkpoint(TRUNCATE);", this.sqliteConnection))
                {
                    checkpointCommand.ExecuteNonQuery();
                }
            }
            File.Copy(this.DatabasePath, backupPath);
        }


        public override void CreateOrUpdateTable(TableMapping mapping)
        {
            if (!this.TableExists(mapping.TableName))
            {
                // this is the easy case, we need to create the table
                string createTable = GetCreateTableScript(mapping, DbFlavor.Sqlite);
                this.ExecuteNonQuery(createTable);

                foreach (string indexScript in GetCreateIndexScripts(mapping))
                {
                    this.ExecuteNonQuery(indexScript);
                }
            }
            else
            {
                StringBuilder sb = new StringBuilder();
                StringBuilder log = new StringBuilder();

                using (var transaction = this.sqliteConnection.BeginTransaction())
                {

                    // the hard part, figure out if table needs to be altered...                
                    TableMapping actual = this.LoadTableMetadata(mapping.TableName);

                    // Lastly see if any types or maxlengths need to be changed.
                    // Unfortunately SqlLite does not support ALTER a column!!
                    // See https://www.sqlite.org/lang_altertable.html

                    bool newTable = false;
                    foreach (ColumnMapping c in actual.Columns)
                    {
                        ColumnMapping ac = mapping.FindColumn(c.ColumnName);
                        if (ac != null)
                        {
                            if (c.MaxLength != ac.MaxLength || c.SqlType != ac.SqlType || c.Precision != ac.Precision || c.Scale != ac.Scale ||
                                c.AllowNulls != ac.AllowNulls)
                            {
                                // bummer, so the only way to do this is to copy all the data over to a new table!
                                newTable = true;
                                break;
                            }
                        }
                    }
                    List<ColumnMapping> newColumns = new List<ColumnMapping>();
                    List<ColumnMapping> renames = new List<ColumnMapping>();
                    // See if any new columns need to be added.
                    foreach (ColumnMapping c in mapping.Columns)
                    {
                        ColumnMapping ac = actual.FindColumn(c.ColumnName);
                        if (ac == null)
                        {
                            if (!string.IsNullOrEmpty(c.OldColumnName))
                            {
                                ac = actual.FindColumn(c.OldColumnName);
                                if (ac != null)
                                {
                                    // this column needs to be renamed, ALTER TABLE doesn't allow renames, so the most efficient way to do it
                                    // is add the new column, do an UPDATE to copy all the values over, then DROP the old column.
                                    if (c.IsPrimaryKey || ac.IsPrimaryKey)
                                    {
                                        newTable = true; // then we need to create a new table!
                                    }
                                    // we deliberately do NOT copy the AllowNulls because we can't set that yet.
                                    ColumnMapping clone = new ColumnMapping()
                                    {
                                        ColumnName = c.ColumnName,
                                        OldColumnName = c.OldColumnName,
                                        MaxLength = c.MaxLength,
                                        Precision = c.Precision,
                                        Scale = c.Scale,
                                        SqlType = c.SqlType,
                                        AllowNulls = true // because we can't set this initially until we populate the column.
                                    };
                                    renames.Add(clone);
                                }
                                else
                                {
                                    newColumns.Add(c);
                                }
                            }
                            else
                            {
                                newColumns.Add(c);
                            }
                        }
                    }

                    // See if any columns need to be dropped.
                    foreach (ColumnMapping c in actual.Columns)
                    {
                        if (c.ColumnName == "Version" || c.ColumnName == "RowVersion")
                        {
                            // The optimistic-concurrency version column (persistence-concurrency
                            // phase 1, Task 3) is injected by GetCreateTableScript itself, not
                            // by MappingEngine.GetColumnsFromObject, so it's never present in
                            // mapping.Columns even though every real table has it. Without this
                            // guard every table looked like it needed a column dropped on every
                            // subsequent CreateOrUpdateTable call, forcing the newTable
                            // rename/copy/drop/rename rebuild below unconditionally. That was
                            // harmless before Task 4 added FK constraints, but DROP TABLE on a
                            // table that's the parent of an FK now fails outright if any other
                            // table still has rows referencing it (e.g. dropping Accounts while
                            // Transactions.Account still points at it).
                            continue;
                        }

                        ColumnMapping ac = mapping.FindColumn(c.ColumnName);
                        if (ac == null)
                        {
                            // bummer, sqllite can't drop columns.
                            newTable = true;
                            break;
                        }
                    }

                    if (newTable)
                    {
                        // invent a new name for the temporary table
                        string originalTableName = mapping.TableName;
                        mapping.TableName = "NEW_" + originalTableName;
                        string createTable = GetCreateTableScript(mapping, DbFlavor.Sqlite);
                        this.ExecuteNonQuery(createTable);

                        // copy the data across to this new table, taking any "renames" into account.
                        // INSERT INTO new_X SELECT ... FROM X
                        sb.Length = 0;
                        sb.Append(string.Format("INSERT INTO [{0}] (", mapping.TableName));

                        StringBuilder select = new StringBuilder();
                        select.Append("SELECT ");
                        bool first = true;

                        foreach (ColumnMapping c in actual.Columns)
                        {
                            if (c.ColumnName == this.VersionColumnName)
                            {
                                // Version/RowVersion is deliberately excluded from mapping.Columns
                                // (it's engine-injected, not reflected off the domain model - see
                                // the column-drop guard below), so mapping.FindColumn(c.ColumnName)
                                // always returns null for it here and it would otherwise fall into
                                // the "dropping this column" branch below, discarding every row's
                                // real accumulated version history - the new table's freshly
                                // GetCreateTableScript-generated Version column would then default
                                // every row back to 1, letting a stale in-memory RowVersion win a
                                // write it should have conflicted on. Carry the real column across
                                // explicitly instead.
                                if (!first)
                                {
                                    sb.Append(", ");
                                    select.Append(", ");
                                }
                                sb.Append("[" + c.ColumnName + "]");
                                select.Append("[" + c.ColumnName + "]");
                                first = false;
                                continue;
                            }

                            ColumnMapping ac = mapping.FindColumn(c.ColumnName);
                            if (ac == null)
                            {
                                if (!string.IsNullOrEmpty(c.OldColumnName))
                                {
                                    // renaming a column
                                    ac = actual.FindColumn(c.OldColumnName);
                                    if (ac != null)
                                    {
                                        if (!first)
                                        {
                                            sb.Append(", ");
                                            select.Append(", ");
                                        }
                                        sb.Append("[" + c.ColumnName + "]");
                                        select.Append("[" + c.OldColumnName + "]");
                                        first = false;
                                    }
                                    else
                                    {
                                        // already been renamed, now we are dropping it.
                                    }
                                }
                                else
                                {
                                    // dropping this column
                                }
                            }
                            else
                            {
                                // copy as is.
                                if (!first)
                                {
                                    sb.Append(", ");
                                    select.Append(", ");
                                }
                                sb.Append("[" + c.ColumnName + "]");
                                select.Append("[" + c.ColumnName + "]");
                                first = false;
                            }
                        }

                        if (first)
                        {
                            throw new Exception("Invalid table definition, no columns found in the old table");
                        }

                        sb.Append(") ");
                        select.Append(string.Format(" FROM [{0}]", originalTableName));
                        sb.Append(select.ToString());
                        this.ExecuteNonQuery(sb.ToString());

                        // drop the old table, now that we've copied all the data into the new one
                        this.ExecuteNonQuery(string.Format("DROP TABLE [{0}]", originalTableName));

                        // rename new table to original table name.
                        this.ExecuteNonQuery(string.Format("ALTER TABLE [{0}] RENAME TO [{1}]", mapping.TableName, originalTableName));

                        mapping.TableName = originalTableName;
                    }
                    else
                    {
                        if (newColumns.Count > 0)
                        {
                            // create the new column that was added.
                            foreach (ColumnMapping c in newColumns)
                            {
                                sb.Append(string.Format("ALTER TABLE [{0}] ADD ", mapping.TableName));
                                c.GetPartialSqlDefinition(sb);
                                string cmd = sb.ToString();
                                log.AppendLine(cmd);
                                this.ExecuteScalar(cmd);
                                sb.Length = 0;
                            }
                        }

                        if (actual.FindColumn(this.VersionColumnName) == null)
                        {
                            // Retrofit the optimistic-concurrency version column onto a database
                            // file that predates persistence-concurrency Phase 1 Task 3 (which
                            // added it only to GetCreateTableScript's freshly-CREATEd tables).
                            // The newColumns loop above can never catch this itself: Version/
                            // RowVersion is deliberately excluded from mapping.Columns (Task 4's
                            // guard a few lines below, so it's also never treated as a column to
                            // drop), since it's engine-injected rather than reflected off the
                            // domain model. Without this, an old on-disk file would never get the
                            // column added - harmless until persistence-concurrency Phase 2b's
                            // SaveOne/ReadCategories (SqliteDatabase.ReadCategories) started
                            // unconditionally selecting it, at which point every load of such a
                            // file would fail with "no such column: Version". DEFAULT 1 mirrors
                            // GetCreateTableScript's own literal for freshly created tables, and
                            // SQLite allows a NOT NULL ADD COLUMN precisely because it has one.
                            string addVersionColumn = string.Format(
                                "ALTER TABLE [{0}] ADD COLUMN [{1}] INTEGER NOT NULL DEFAULT 1;",
                                mapping.TableName, this.VersionColumnName);
                            log.AppendLine(addVersionColumn);
                            this.ExecuteScalar(addVersionColumn);
                        }

                        if (renames.Count > 0)
                        {
                            // create the new column that was added.
                            foreach (ColumnMapping c in renames)
                            {
                                sb.Append(string.Format("ALTER TABLE [{0}] ADD ", mapping.TableName));
                                c.GetPartialSqlDefinition(sb);
                                string cmd = sb.ToString();
                                log.AppendLine(cmd);
                                this.ExecuteScalar(cmd);
                                sb.Length = 0;
                            }

                            // now copy the data across to the new column that was added.
                            foreach (ColumnMapping c in renames)
                            {
                                if (sb.Length > 0)
                                {
                                    sb.AppendLine(",");
                                }
                                sb.Append(string.Format("[{0}] = [{1}]", c.ColumnName, c.OldColumnName));
                                actual.Columns.Add(c);
                            }

                            string update = string.Format("UPDATE [{0}] SET ", mapping.TableName) + sb.ToString();
                            log.AppendLine(update);
                            this.ExecuteScalar(update);
                            sb.Length = 0;
                        }

                        // See if any columns need to be dropped.
                        foreach (ColumnMapping c in actual.Columns)
                        {
                            if (c.ColumnName == "Version" || c.ColumnName == "RowVersion")
                            {
                                // See the matching guard/comment above (Task 4): this engine-
                                // injected column is never in mapping.Columns, so it must never
                                // be treated as a column to drop.
                                continue;
                            }

                            ColumnMapping ac = mapping.FindColumn(c.ColumnName);
                            if (ac == null)
                            {
                                string drop = string.Format("ALTER TABLE [{0}]  DROP  COLUMN [{1}] ", mapping.TableName, c.ColumnName);
                                log.AppendLine(drop);
                                this.ExecuteScalar(drop);
                            }
                        }

                    }

                    // Ok, commit the transaction!
                    transaction.Commit();
                }


                if (log.Length > 0)
                {
                    this.AppendLog(log.ToString());
                }
            }
        }

        /// <summary>
        /// Runs a parameterized non-query inside the given explicit transaction and returns the
        /// affected-row count - unlike the existing ExecuteNonQuery overrides (which never take a
        /// transaction parameter and discard the row count), this is what SaveOne/SaveBatch need
        /// to detect a RowVersion conflict (zero rows affected on an UPDATE/DELETE whose WHERE
        /// clause checked the version). Private: only this class's new SaveOne/SaveBatch path
        /// uses it.
        /// </summary>
        private int ExecuteNonQueryInTransaction(SQLiteTransaction transaction, string cmd, params (string Name, object Value)[] parameters)
        {
            this.AppendLog(cmd);
            using (SQLiteCommand command = new SQLiteCommand(cmd, this.sqliteConnection, transaction))
            {
                foreach (var (name, value) in parameters)
                {
                    DbParameter p = command.CreateParameter();
                    p.ParameterName = name;
                    p.Value = value ?? DBNull.Value;
                    command.Parameters.Add(p);
                }
                return command.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Same transaction-explicit pattern as ExecuteNonQueryInTransaction, for the single
        /// follow-up SELECT SaveOneCategory issues after a detected conflict, to report the
        /// store's actual current RowVersion in ConcurrencyConflictException rather than a
        /// sentinel. Only reached on the rare conflict path.
        /// </summary>
        private object ExecuteScalarInTransaction(SQLiteTransaction transaction, string cmd, params (string Name, object Value)[] parameters)
        {
            using (SQLiteCommand command = new SQLiteCommand(cmd, this.sqliteConnection, transaction))
            {
                foreach (var (name, value) in parameters)
                {
                    DbParameter p = command.CreateParameter();
                    p.ParameterName = name;
                    p.Value = value ?? DBNull.Value;
                    command.Parameters.Add(p);
                }
                return command.ExecuteScalar();
            }
        }

        public override void SaveOne<T>(T root)
        {
            this.SaveBatch(new PersistentObject[] { root });
        }

        public override void SaveBatch(IEnumerable<PersistentObject> roots)
        {
            List<PersistentObject> list = new List<PersistentObject>(roots);
            if (list.Count == 0)
            {
                return;
            }

            // Only these types have a real implementation so far (entity-extension follow-up to
            // Phase 2b, adding one flat-row aggregate root at a time). If the batch contains
            // anything else, delegate the WHOLE call to the inherited Phase 2a stub rather than
            // partially committing some roots and throwing on others.
            foreach (PersistentObject root in list)
            {
                if (!(root is Category || root is Currency || root is OnlineAccount || root is Account || root is Payee))
                {
                    base.SaveBatch(list);
                    return;
                }
            }

            this.Connect();
            using (SQLiteTransaction transaction = this.sqliteConnection.BeginTransaction())
            {
                // Every in-memory side effect (RowVersion assignment, OnUpdated, RemoveChild) is
                // deferred into this list and only run AFTER Commit() succeeds. If any later root in
                // this batch conflicts, the catch below rolls back EVERY write this transaction made -
                // including ones whose own SQL statement already "succeeded" earlier in the loop. If
                // this method mutated in-memory state immediately per-root instead, an earlier root's
                // RowVersion/dirty-flag would end up claiming a commit that the rollback undid,
                // violating R3 ("a failure leaves in-memory dirty-tracking state exactly matching
                // what's actually in the database"). See SaveBatch_OneStaleRootAmongMany_
                // RollsBackTransactionAndPreservesInMemoryState below, which fails without this.
                List<Action> postCommitActions = new List<Action>();
                try
                {
                    foreach (PersistentObject root in list)
                    {
                        if (root is Category category)
                        {
                            this.SaveOneCategory(category, transaction, postCommitActions);
                        }
                        else if (root is Currency currency)
                        {
                            this.SaveOneCurrency(currency, transaction, postCommitActions);
                        }
                        else if (root is OnlineAccount onlineAccount)
                        {
                            this.SaveOneOnlineAccount(onlineAccount, transaction, postCommitActions);
                        }
                        else if (root is Account account)
                        {
                            this.SaveOneAccount(account, transaction, postCommitActions);
                        }
                        else if (root is Payee payee)
                        {
                            this.SaveOnePayee(payee, transaction, postCommitActions);
                        }
                    }
                    transaction.Commit();
                }
                catch (Exception originalException)
                {
                    // A failing Rollback() (plausible under WAL/busy_timeout - Commit() can fail
                    // with SQLITE_BUSY, and rollback of an already-troubled transaction can fail
                    // too) must never silently replace/hide the original exception (e.g. a
                    // ConcurrencyConflictException the caller needs to see), and must never leave
                    // the caller thinking the rollback happened when it didn't.
                    try
                    {
                        transaction.Rollback();
                    }
                    catch (Exception rollbackException)
                    {
                        throw new Exception("Rollback failed after: " + originalException.Message, rollbackException);
                    }
                    throw;
                }

                // Only reached after Commit() succeeded - the catch above always rethrows before
                // falling through here, so postCommitActions never runs against a rolled-back
                // transaction. Runs inside the using block, before the transaction is disposed.
                foreach (Action action in postCommitActions)
                {
                    action();
                }
            }
        }

        /// <summary>
        /// Writes one Category row with a RowVersion-checked WHERE clause on UPDATE/DELETE, and
        /// queues (but does not yet apply) the matching in-memory side effect - see SaveBatch's
        /// comment on postCommitActions for why applying it immediately would be wrong. SQL
        /// mirrors UpdateCategories' existing parameterized branch (SqlDatabase.cs) exactly, plus
        /// the version check/bump - deliberately duplicated rather than shared, since
        /// UpdateCategories serves the whole-graph Save(MyMoney) path (additive-only constraint:
        /// not touched here) and operates on a whole Categories collection, not one root.
        /// </summary>
        private void SaveOneCategory(Category c, SQLiteTransaction transaction, List<Action> postCommitActions)
        {
            long callerRowVersion = c.RowVersion;

            if (c.IsInserted)
            {
                this.ExecuteNonQueryInTransaction(transaction,
                    "INSERT INTO Categories (Id,Name,Description,Type,ParentId,Budget,Frequency,Balance,Color,TaxRefNum) " +
                    "VALUES (@Id,@Name,@Description,@Type,@ParentId,@Budget,@Frequency,@Balance,@Color,@TaxRefNum);",
                    ("@Id", c.Id), ("@Name", c.Name), ("@Description", c.Description), ("@Type", (int)c.Type),
                    ("@ParentId", c.ParentCategory != null ? (object)c.ParentCategory.Id : DBNull.Value), ("@Budget", c.Budget),
                    ("@Frequency", (int)c.Frequency), ("@Balance", c.Balance), ("@Color", c.Color),
                    ("@TaxRefNum", c.TaxRefNum));
                postCommitActions.Add(() =>
                {
                    c.RowVersion = 1; // matches the schema's "Version INTEGER NOT NULL DEFAULT 1"
                    c.OnUpdated();
                });
                return;
            }

            if (c.IsChanged)
            {
                int rowsAffected = this.ExecuteNonQueryInTransaction(transaction,
                    "UPDATE Categories SET Name=@Name,Description=@Description,Type=@Type,ParentId=@ParentId,Budget=@Budget," +
                    "Frequency=@Frequency,Balance=@Balance,Color=@Color,TaxRefNum=@TaxRefNum," +
                    this.VersionColumnName + "=" + this.VersionColumnName + "+1 " +
                    "WHERE Id=@Id AND " + this.VersionColumnName + "=@ExpectedVersion;",
                    ("@Name", c.Name), ("@Description", c.Description), ("@Type", (int)c.Type),
                    ("@ParentId", c.ParentCategory != null ? (object)c.ParentCategory.Id : DBNull.Value), ("@Budget", c.Budget),
                    ("@Frequency", (int)c.Frequency), ("@Balance", c.Balance), ("@Color", c.Color),
                    ("@TaxRefNum", c.TaxRefNum), ("@Id", c.Id), ("@ExpectedVersion", callerRowVersion));
                if (rowsAffected == 0)
                {
                    this.ThrowConflict(c, "Categories", c.Id, transaction, callerRowVersion);
                }
                postCommitActions.Add(() =>
                {
                    c.RowVersion = callerRowVersion + 1;
                    c.OnUpdated();
                });
                return;
            }

            if (c.IsDeleted)
            {
                int rowsAffected = this.ExecuteNonQueryInTransaction(transaction,
                    "DELETE FROM Categories WHERE Id=@Id AND " + this.VersionColumnName + "=@ExpectedVersion;",
                    ("@Id", c.Id), ("@ExpectedVersion", callerRowVersion));
                if (rowsAffected == 0)
                {
                    this.ThrowConflict(c, "Categories", c.Id, transaction, callerRowVersion);
                }
                postCommitActions.Add(() =>
                {
                    c.OnUpdated();
                    c.Parent.RemoveChild(c, true);
                });
                return;
            }

            // No pending change - nothing to do.
        }

        /// <summary>
        /// Writes one Currency row - same shape as SaveOneCategory (see its comment for why the
        /// in-memory side effect is deferred into postCommitActions). SQL mirrors UpdateCurrencies'
        /// existing parameterized branch (SqlDatabase.cs) exactly, plus the version check/bump.
        /// </summary>
        private void SaveOneCurrency(Currency c, SQLiteTransaction transaction, List<Action> postCommitActions)
        {
            long callerRowVersion = c.RowVersion;

            if (c.IsInserted)
            {
                this.ExecuteNonQueryInTransaction(transaction,
                    "INSERT INTO Currencies (Id,Symbol,Name,Ratio,LastRatio,CultureCode) VALUES (@Id,@Symbol,@Name,@Ratio,@LastRatio,@CultureCode);",
                    ("@Id", c.Id), ("@Symbol", c.Symbol), ("@Name", c.Name), ("@Ratio", c.Ratio), ("@LastRatio", c.LastRatio),
                    ("@CultureCode", c.CultureCode));
                postCommitActions.Add(() =>
                {
                    c.RowVersion = 1;
                    c.OnUpdated();
                });
                return;
            }

            if (c.IsChanged)
            {
                int rowsAffected = this.ExecuteNonQueryInTransaction(transaction,
                    "UPDATE Currencies SET Symbol=@Symbol,Name=@Name,Ratio=@Ratio,LastRatio=@LastRatio,CultureCode=@CultureCode," +
                    this.VersionColumnName + "=" + this.VersionColumnName + "+1 " +
                    "WHERE Id=@Id AND " + this.VersionColumnName + "=@ExpectedVersion;",
                    ("@Symbol", c.Symbol), ("@Name", c.Name), ("@Ratio", c.Ratio), ("@LastRatio", c.LastRatio),
                    ("@CultureCode", c.CultureCode), ("@Id", c.Id), ("@ExpectedVersion", callerRowVersion));
                if (rowsAffected == 0)
                {
                    this.ThrowConflict(c, "Currencies", c.Id, transaction, callerRowVersion);
                }
                postCommitActions.Add(() =>
                {
                    c.RowVersion = callerRowVersion + 1;
                    c.OnUpdated();
                });
                return;
            }

            if (c.IsDeleted)
            {
                int rowsAffected = this.ExecuteNonQueryInTransaction(transaction,
                    "DELETE FROM Currencies WHERE Id=@Id AND " + this.VersionColumnName + "=@ExpectedVersion;",
                    ("@Id", c.Id), ("@ExpectedVersion", callerRowVersion));
                if (rowsAffected == 0)
                {
                    this.ThrowConflict(c, "Currencies", c.Id, transaction, callerRowVersion);
                }
                postCommitActions.Add(() =>
                {
                    c.OnUpdated();
                    c.Parent.RemoveChild(c, true);
                });
                return;
            }

            // No pending change - nothing to do.
        }

        /// <summary>
        /// Writes one OnlineAccount row - same shape as SaveOneCategory. SQL mirrors
        /// UpdateOnlineAccounts' existing parameterized branch (SqlDatabase.cs) exactly, plus the
        /// version check/bump.
        /// </summary>
        private void SaveOneOnlineAccount(OnlineAccount i, SQLiteTransaction transaction, List<Action> postCommitActions)
        {
            long callerRowVersion = i.RowVersion;

            if (i.IsInserted)
            {
                this.ExecuteNonQueryInTransaction(transaction,
                    "INSERT INTO OnlineAccounts (Id,Name,Institution,OFX,FID,UserId,Password,BankId,BranchId,BrokerId,OfxVersion,LogoUrl,AppId,AppVersion,ClientUid,UserCred1,UserCred2,AuthToken,AccessKey,UserKey,UserKeyExpireDate) " +
                    "VALUES (@Id,@Name,@Institution,@OFX,@FID,@UserId,@Password,@BankId,@BranchId,@BrokerId,@OfxVersion,@LogoUrl,@AppId,@AppVersion,@ClientUid,@UserCred1,@UserCred2,@AuthToken,@AccessKey,@UserKey,@UserKeyExpireDate);",
                    ("@Id", i.Id), ("@Name", i.Name), ("@Institution", i.Institution), ("@OFX", i.Ofx), ("@FID", i.FID),
                    ("@UserId", i.UserId), ("@Password", i.Password), ("@BankId", i.BankId), ("@BranchId", i.BranchId),
                    ("@BrokerId", i.BrokerId), ("@OfxVersion", i.OfxVersion), ("@LogoUrl", i.LogoUrl), ("@AppId", i.AppId),
                    ("@AppVersion", i.AppVersion), ("@ClientUid", i.ClientUid), ("@UserCred1", i.UserCred1),
                    ("@UserCred2", i.UserCred2), ("@AuthToken", i.AuthToken), ("@AccessKey", i.AccessKey),
                    ("@UserKey", i.UserKey), ("@UserKeyExpireDate", DBNullableDateTimeParam(i.UserKeyExpireDate)));
                postCommitActions.Add(() =>
                {
                    i.RowVersion = 1;
                    i.OnUpdated();
                });
                return;
            }

            if (i.IsChanged)
            {
                int rowsAffected = this.ExecuteNonQueryInTransaction(transaction,
                    "UPDATE OnlineAccounts SET Name=@Name,Institution=@Institution,OFX=@OFX,FID=@FID,UserId=@UserId,Password=@Password," +
                    "BankId=@BankId,BranchId=@BranchId,BrokerId=@BrokerId,OfxVersion=@OfxVersion,LogoUrl=@LogoUrl,AppId=@AppId,AppVersion=@AppVersion," +
                    "ClientUid=@ClientUid,UserCred1=@UserCred1,UserCred2=@UserCred2,AuthToken=@AuthToken,AccessKey=@AccessKey,UserKey=@UserKey," +
                    "UserKeyExpireDate=@UserKeyExpireDate," + this.VersionColumnName + "=" + this.VersionColumnName + "+1 " +
                    "WHERE Id=@Id AND " + this.VersionColumnName + "=@ExpectedVersion;",
                    ("@Name", i.Name), ("@Institution", i.Institution), ("@OFX", i.Ofx), ("@FID", i.FID),
                    ("@UserId", i.UserId), ("@Password", i.Password), ("@BankId", i.BankId), ("@BranchId", i.BranchId),
                    ("@BrokerId", i.BrokerId), ("@OfxVersion", i.OfxVersion), ("@LogoUrl", i.LogoUrl), ("@AppId", i.AppId),
                    ("@AppVersion", i.AppVersion), ("@ClientUid", i.ClientUid), ("@UserCred1", i.UserCred1),
                    ("@UserCred2", i.UserCred2), ("@AuthToken", i.AuthToken), ("@AccessKey", i.AccessKey),
                    ("@UserKey", i.UserKey), ("@UserKeyExpireDate", DBNullableDateTimeParam(i.UserKeyExpireDate)),
                    ("@Id", i.Id), ("@ExpectedVersion", callerRowVersion));
                if (rowsAffected == 0)
                {
                    this.ThrowConflict(i, "OnlineAccounts", i.Id, transaction, callerRowVersion);
                }
                postCommitActions.Add(() =>
                {
                    i.RowVersion = callerRowVersion + 1;
                    i.OnUpdated();
                });
                return;
            }

            if (i.IsDeleted)
            {
                int rowsAffected = this.ExecuteNonQueryInTransaction(transaction,
                    "DELETE FROM OnlineAccounts WHERE Id=@Id AND " + this.VersionColumnName + "=@ExpectedVersion;",
                    ("@Id", i.Id), ("@ExpectedVersion", callerRowVersion));
                if (rowsAffected == 0)
                {
                    this.ThrowConflict(i, "OnlineAccounts", i.Id, transaction, callerRowVersion);
                }
                postCommitActions.Add(() =>
                {
                    i.OnUpdated();
                    i.Parent.RemoveChild(i, true);
                });
                return;
            }

            // No pending change - nothing to do.
        }

        /// <summary>
        /// Writes one Account row - same shape as SaveOneCategory. SQL mirrors UpdateAccounts'
        /// existing parameterized branch (SqlDatabase.cs) exactly, plus the version check/bump.
        /// </summary>
        private void SaveOneAccount(Account a, SQLiteTransaction transaction, List<Action> postCommitActions)
        {
            long callerRowVersion = a.RowVersion;

            if (a.IsInserted)
            {
                this.ExecuteNonQueryInTransaction(transaction,
                    "INSERT INTO Accounts (Id,AccountId,OfxAccountId,Name,Type,Description,OnlineAccount,OpeningBalance,LastSync,LastBalance,SyncGuid,Flags,Currency,WebSite,ReconcileWarning,CategoryIdForPrincipal,CategoryIdForInterest) " +
                    "VALUES (@Id,@AccountId,@OfxAccountId,@Name,@Type,@Description,@OnlineAccount,@OpeningBalance,@LastSync,@LastBalance,@SyncGuid,@Flags,@Currency,@WebSite,@ReconcileWarning,@CategoryIdForPrincipal,@CategoryIdForInterest);",
                    ("@Id", a.Id), ("@AccountId", a.AccountId), ("@OfxAccountId", a.OfxAccountId), ("@Name", a.Name),
                    ("@Type", (int)a.Type), ("@Description", a.Description),
                    ("@OnlineAccount", a.OnlineAccount != null ? (object)a.OnlineAccount.Id : DBNull.Value),
                    ("@OpeningBalance", a.OpeningBalance), ("@LastSync", DBDateTimeParam(a.LastSync)),
                    ("@LastBalance", DBDateTimeParam(a.LastBalance)), ("@SyncGuid", DBGuidParam(a.SyncGuid)),
                    ("@Flags", (int)a.Flags), ("@Currency", a.Currency), ("@WebSite", a.WebSite),
                    ("@ReconcileWarning", a.ReconcileWarning),
                    ("@CategoryIdForPrincipal", a.CategoryForPrincipal == null ? (object)DBNull.Value : a.CategoryForPrincipal.Id),
                    ("@CategoryIdForInterest", a.CategoryForInterest == null ? (object)DBNull.Value : a.CategoryForInterest.Id));
                postCommitActions.Add(() =>
                {
                    a.RowVersion = 1;
                    a.OnUpdated();
                });
                return;
            }

            if (a.IsChanged)
            {
                int rowsAffected = this.ExecuteNonQueryInTransaction(transaction,
                    "UPDATE Accounts SET AccountId=@AccountId,OfxAccountId=@OfxAccountId,Name=@Name,Type=@Type,Description=@Description," +
                    "OnlineAccount=@OnlineAccount,OpeningBalance=@OpeningBalance,LastSync=@LastSync,LastBalance=@LastBalance,SyncGuid=@SyncGuid," +
                    "Flags=@Flags,Currency=@Currency,WebSite=@WebSite,ReconcileWarning=@ReconcileWarning,CategoryIdForPrincipal=@CategoryIdForPrincipal," +
                    "CategoryIdForInterest=@CategoryIdForInterest," + this.VersionColumnName + "=" + this.VersionColumnName + "+1 " +
                    "WHERE Id=@Id AND " + this.VersionColumnName + "=@ExpectedVersion;",
                    ("@AccountId", a.AccountId), ("@OfxAccountId", a.OfxAccountId), ("@Name", a.Name),
                    ("@Type", (int)a.Type), ("@Description", a.Description),
                    ("@OnlineAccount", a.OnlineAccount != null ? (object)a.OnlineAccount.Id : DBNull.Value),
                    ("@OpeningBalance", a.OpeningBalance), ("@LastSync", DBDateTimeParam(a.LastSync)),
                    ("@LastBalance", DBDateTimeParam(a.LastBalance)), ("@SyncGuid", DBGuidParam(a.SyncGuid)),
                    ("@Flags", (int)a.Flags), ("@Currency", a.Currency), ("@WebSite", a.WebSite),
                    ("@ReconcileWarning", a.ReconcileWarning),
                    ("@CategoryIdForPrincipal", a.CategoryForPrincipal == null ? (object)DBNull.Value : a.CategoryForPrincipal.Id),
                    ("@CategoryIdForInterest", a.CategoryForInterest == null ? (object)DBNull.Value : a.CategoryForInterest.Id),
                    ("@Id", a.Id), ("@ExpectedVersion", callerRowVersion));
                if (rowsAffected == 0)
                {
                    this.ThrowConflict(a, "Accounts", a.Id, transaction, callerRowVersion);
                }
                postCommitActions.Add(() =>
                {
                    a.RowVersion = callerRowVersion + 1;
                    a.OnUpdated();
                });
                return;
            }

            if (a.IsDeleted)
            {
                int rowsAffected = this.ExecuteNonQueryInTransaction(transaction,
                    "DELETE FROM Accounts WHERE Id=@Id AND " + this.VersionColumnName + "=@ExpectedVersion;",
                    ("@Id", a.Id), ("@ExpectedVersion", callerRowVersion));
                if (rowsAffected == 0)
                {
                    this.ThrowConflict(a, "Accounts", a.Id, transaction, callerRowVersion);
                }
                postCommitActions.Add(() =>
                {
                    a.OnUpdated();
                    a.Parent.RemoveChild(a, true);
                });
                return;
            }

            // No pending change - nothing to do.
        }

        /// <summary>
        /// Writes one Payee row - same shape as SaveOneCategory. SQL mirrors UpdatePayees' existing
        /// parameterized branch (SqlDatabase.cs) exactly, plus the version check/bump.
        /// </summary>
        private void SaveOnePayee(Payee p, SQLiteTransaction transaction, List<Action> postCommitActions)
        {
            long callerRowVersion = p.RowVersion;

            if (p.IsInserted)
            {
                this.ExecuteNonQueryInTransaction(transaction,
                    "INSERT INTO Payees (Id, Name) VALUES (@Id, @Name);",
                    ("@Id", p.Id), ("@Name", p.Name));
                postCommitActions.Add(() =>
                {
                    p.RowVersion = 1;
                    p.OnUpdated();
                });
                return;
            }

            if (p.IsChanged)
            {
                int rowsAffected = this.ExecuteNonQueryInTransaction(transaction,
                    "UPDATE Payees SET Name=@Name," + this.VersionColumnName + "=" + this.VersionColumnName + "+1 " +
                    "WHERE Id=@Id AND " + this.VersionColumnName + "=@ExpectedVersion;",
                    ("@Name", p.Name), ("@Id", p.Id), ("@ExpectedVersion", callerRowVersion));
                if (rowsAffected == 0)
                {
                    this.ThrowConflict(p, "Payees", p.Id, transaction, callerRowVersion);
                }
                postCommitActions.Add(() =>
                {
                    p.RowVersion = callerRowVersion + 1;
                    p.OnUpdated();
                });
                return;
            }

            if (p.IsDeleted)
            {
                int rowsAffected = this.ExecuteNonQueryInTransaction(transaction,
                    "DELETE FROM Payees WHERE Id=@Id AND " + this.VersionColumnName + "=@ExpectedVersion;",
                    ("@Id", p.Id), ("@ExpectedVersion", callerRowVersion));
                if (rowsAffected == 0)
                {
                    this.ThrowConflict(p, "Payees", p.Id, transaction, callerRowVersion);
                }
                postCommitActions.Add(() =>
                {
                    p.OnUpdated();
                    p.Parent.RemoveChild(p, true);
                });
                return;
            }

            // No pending change - nothing to do.
        }

        /// <summary>
        /// A zero-rows-affected UPDATE/DELETE means a conflict, but not what the store's current
        /// version actually is - one more SELECT (inside the same transaction, so it sees a
        /// consistent view) gets an accurate diagnostic instead of a sentinel. -1 means the row no
        /// longer exists at all (e.g. already deleted by someone else).
        /// </summary>
        private void ThrowConflict(PersistentObject root, string tableName, int id, SQLiteTransaction transaction, long callerRowVersion)
        {
            object result = this.ExecuteScalarInTransaction(transaction,
                "SELECT " + this.VersionColumnName + " FROM " + tableName + " WHERE Id=@Id;", ("@Id", id));
            long storedRowVersion = (result == null || result == DBNull.Value) ? -1 : Convert.ToInt64(result);
            throw new ConcurrencyConflictException(root, storedRowVersion, callerRowVersion);
        }

        /// <summary>
        /// Overrides the inherited (shared-with-SqlServerDatabase) ReadCategories to also read
        /// back the Version column - deliberately a full override, not a shared generic change,
        /// so SQL Server's read path (which will need ROWVERSION's binary(8)-to-long conversion,
        /// not a plain integer read) is untouched until Phase 2c actually needs it.
        /// </summary>
        public override void ReadCategories(Categories categories, MyMoney money)
        {
            categories.Clear();
            IDataReader reader = this.ExecuteReader("SELECT [Id],[Name],[Description],[Type],[ParentId],[Budget],[Frequency],[Balance],[Color],[TaxRefNum],[" + this.VersionColumnName + "] FROM Categories");
            categories.BeginUpdate(false);
            while (reader.Read())
            {
                this.IncrementProgress("Categories");
                int id = reader.GetInt32(0);
                Category c = new Category(categories);
                c.Id = id;
                categories.AddCategory(c);
                c.Name = ReadDbString(reader, 1);
                c.Description = ReadDbString(reader, 2);
                if (!reader.IsDBNull(3))
                {
                    c.Type = (CategoryType)reader.GetInt32(3);
                }
                if (!reader.IsDBNull(4))
                {
                    c.ParentId = reader.GetInt32(4);
                }
                if (!reader.IsDBNull(5))
                {
                    c.Budget = reader.GetDecimal(5);
                }
                if (!reader.IsDBNull(6))
                {
                    c.Frequency = (CalendarRange)reader.GetInt32(6);
                }
                if (!reader.IsDBNull(7))
                {
                    c.Balance = reader.GetDecimal(7);
                }
                if (!reader.IsDBNull(8))
                {
                    c.Color = reader.GetString(8);
                }
                if (!reader.IsDBNull(9))
                {
                    c.TaxRefNum = reader.GetInt32(9);
                }
                c.RowVersion = reader.GetInt64(10);
                c.OnUpdated();
                // one more fix up that will need to be saved (so must come after c.OnUpdated).
                if (c.Type == CategoryType.Reserved)
                {
                    c.Type = CategoryType.Expense;
                }
            }
            categories.EndUpdate();
            categories.FireChangeEvent(categories, categories, null, ChangeType.Reloaded);
            reader.Close();
        }

        /// <summary>
        /// Overrides the inherited ReadCurrencies to also read back the Version column - same
        /// rationale as ReadCategories' override.
        /// </summary>
        public override void ReadCurrencies(Currencies currencies, MyMoney money)
        {
            currencies.Clear();
            IDataReader reader = this.ExecuteReader("SELECT [Id],[Symbol],[Name],[Ratio],[LastRatio],[CultureCode],[" + this.VersionColumnName + "] FROM Currencies");
            currencies.BeginUpdate(false);
            while (reader.Read())
            {
                this.IncrementProgress("Currencies");
                int id = reader.GetInt32(0);
                Currency c = currencies.AddCurrency(id);
                c.Symbol = ReadDbString(reader, 1);
                c.Name = ReadDbString(reader, 2);
                if (!reader.IsDBNull(3))
                {
                    c.Ratio = reader.GetDecimal(3);
                }
                if (!reader.IsDBNull(4))
                {
                    c.LastRatio = reader.GetDecimal(4);
                }
                if (reader.IsDBNull(5))
                {
                    c.CultureCode = "en-US";
                }
                else
                {
                    c.CultureCode = ReadDbString(reader, 5);
                }
                c.RowVersion = reader.GetInt64(6);
                c.OnUpdated();
            }
            currencies.EndUpdate();
            currencies.FireChangeEvent(currencies, currencies, null, ChangeType.Reloaded);
            reader.Close();
        }

        /// <summary>
        /// Overrides the inherited ReadOnlineAccounts to also read back the Version column - same
        /// rationale as ReadCategories' override.
        /// </summary>
        public override void ReadOnlineAccounts(OnlineAccounts onlineAccounts, MyMoney money)
        {
            onlineAccounts.Clear();
            IDataReader reader = this.ExecuteReader("SELECT [Id],[Name],[Institution],[OFX],[FID],[UserId],[Password],[BankId],[BranchId],[BrokerId],[OfxVersion],[LogoUrl],[AppId],[AppVersion],[ClientUid],[UserCred1],[UserCred2],[AuthToken],[AccessKey],[UserKey],[UserKeyExpireDate],[" + this.VersionColumnName + "] FROM OnlineAccounts");
            onlineAccounts.BeginUpdate(false);
            while (reader.Read())
            {
                this.IncrementProgress("OnlineAccounts");
                int id = reader.GetInt32(0);
                OnlineAccount i = onlineAccounts.AddOnlineAccount(id);
                i.Name = ReadDbString(reader, 1);
                i.Institution = ReadDbString(reader, 2);
                i.Ofx = ReadDbString(reader, 3);
                i.FID = ReadDbString(reader, 4);
                i.UserId = ReadDbString(reader, 5);
                i.Password = ReadDbString(reader, 6);
                i.BankId = ReadDbString(reader, 7);
                i.BranchId = ReadDbString(reader, 8);
                i.BrokerId = ReadDbString(reader, 9);
                i.OfxVersion = ReadDbString(reader, 10);
                i.LogoUrl = ReadDbString(reader, 11);
                i.AppId = ReadDbString(reader, 12);
                i.AppVersion = ReadDbString(reader, 13);
                i.ClientUid = ReadDbString(reader, 14);
                i.UserCred1 = ReadDbString(reader, 15);
                i.UserCred2 = ReadDbString(reader, 16);
                i.AuthToken = ReadDbString(reader, 17);
                i.AccessKey = ReadDbString(reader, 18);
                i.UserKey = ReadDbString(reader, 19);
                if (!reader.IsDBNull(20))
                {
                    i.UserKeyExpireDate = reader.SafeGetDateTime(20);
                }
                i.RowVersion = reader.GetInt64(21);
                i.OnUpdated();
            }
            onlineAccounts.EndUpdate();
            onlineAccounts.FireChangeEvent(this, this, null, ChangeType.Reloaded);
            reader.Close();
        }

        /// <summary>
        /// Overrides the inherited ReadAccounts to also read back the Version column - same
        /// rationale as ReadCategories' override.
        /// </summary>
        public override void ReadAccounts(Accounts accts, MyMoney money)
        {
            IDataReader reader = this.ExecuteReader("SELECT [Id],[AccountId],[Name],[Type],[Description],[OnlineAccount],[OpeningBalance],[LastSync],[LastBalance],[SyncGuid],[Flags],[Currency],[WebSite],[ReconcileWarning],[CategoryIdForPrincipal],[CategoryIdForInterest],[OfxAccountId],[" + this.VersionColumnName + "] FROM Accounts");
            accts.BeginUpdate(false);
            accts.Clear();
            while (reader.Read())
            {
                this.IncrementProgress("Accounts");
                int id = reader.GetInt32(0);
                Account a = accts.AddAccount(id);
                a.AccountId = ReadDbString(reader, 1);
                a.Name = ReadDbString(reader, 2);
                a.Type = (AccountType)reader.GetInt32(3);
                a.Description = ReadDbString(reader, 4);
                a.OnlineAccount = reader.IsDBNull(5) ? null : money.OnlineAccounts.FindOnlineAccountAt(reader.GetInt32(5));
                a.OpeningBalance = reader.GetDecimal(6);

                if (!reader.IsDBNull(7))
                {
                    a.LastSync = reader.SafeGetDateTime(7);
                }

                if (!reader.IsDBNull(8))
                {
                    a.LastBalance = reader.SafeGetDateTime(8);
                }

                if (!reader.IsDBNull(9))
                {
                    try
                    {
                        a.SyncGuid = new SqlGuid(reader.GetGuid(9));
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("### invalid GUID Error: {0}", ex.Message);
                    }
                }

                if (!reader.IsDBNull(10))
                {
                    a.Flags = (AccountFlags)reader.GetInt32(10);
                }

                a.Currency = ReadDbString(reader, 11);
                a.WebSite = ReadDbString(reader, 12);
                a.ReconcileWarning = ReadInt32(reader, 13);

                a.CategoryForPrincipal = reader.IsDBNull(14) ? null : money.Categories.FindCategoryById(reader.GetInt32(14));
                a.CategoryForInterest = reader.IsDBNull(15) ? null : money.Categories.FindCategoryById(reader.GetInt32(15));

                a.OfxAccountId = ReadDbString(reader, 16);
                a.RowVersion = reader.GetInt64(17);
                a.OnUpdated();
            }
            accts.EndUpdate();
            accts.FireChangeEvent(accts, accts, null, ChangeType.Reloaded);
            reader.Close();
        }

        /// <summary>
        /// Overrides the inherited ReadPayees to also read back the Version column - same rationale
        /// as ReadCategories' override.
        /// </summary>
        public override void ReadPayees(Payees payees, MyMoney money)
        {
            payees.Clear();
            IDataReader reader = this.ExecuteReader("SELECT [Id],[Name],[" + this.VersionColumnName + "] FROM Payees");
            payees.BeginUpdate(false);
            while (reader.Read())
            {
                this.IncrementProgress("Payees");
                int id = reader.GetInt32(0);
                Payee p = payees.AddPayee(id);
                p.Name = ReadDbString(reader, 1);
                p.RowVersion = reader.GetInt64(2);
                p.OnUpdated();
            }
            payees.EndUpdate();
            payees.FireChangeEvent(payees, payees, null, ChangeType.Reloaded);
            reader.Close();
        }

    }
}
