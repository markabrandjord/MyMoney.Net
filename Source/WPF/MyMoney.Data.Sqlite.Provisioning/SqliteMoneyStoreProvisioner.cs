using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Globalization;

namespace Walkabout.Data.Sqlite.Provisioning
{
    public sealed record SqliteProvisioningOptions(string DisplayName, string DataSource, bool IsTestDatabase);

    /// <summary>One recorded row of __SchemaHistory.</summary>
    public sealed record AppliedStep(int Version, string StepName, string ChecksumHash, string AppliedUtc, string AppliedBy);

    /// <summary>
    /// The SQLite half of spec section 1.5's one mechanism. Creating a database is ApplyTo(N) from
    /// version 0; nuke-and-pave is DropAll() then ApplyTo(N) and is then LITERALLY
    /// indistinguishable from creating a new database; the eventual production upgrade is
    /// ApplyTo(N) from version M. The upgrade path is therefore exercised on every single fresh
    /// creation, which is the whole trick.
    /// </summary>
    public sealed class SqliteMoneyStoreProvisioner : IMoneyStoreProvisioner
    {
        private readonly SqliteProvisioningOptions options;
        private readonly SQLiteConnection connection;

        private SqliteMoneyStoreProvisioner(SqliteProvisioningOptions options, SQLiteConnection connection)
        {
            this.options = options;
            this.connection = connection;
        }

        public static SqliteMoneyStoreProvisioner Open(SqliteProvisioningOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            return new SqliteMoneyStoreProvisioner(options, SqliteConnectionFactory.Open(options.DataSource));
        }

        /// <summary>
        /// Exposed for the provisioning tier's own tests and for the test tier, which runs its
        /// ladders over the same open handle. Not on IMoneyStoreProvisioner: the port must not
        /// hand a raw connection to the business layer.
        /// </summary>
        public SQLiteConnection Connection => this.connection;

        public string DataSource => this.options.DataSource;

        public int LatestKnownVersion => SchemaStepCatalog.LatestVersion;

        public StoreIdentity Identity => new StoreIdentity(
            this.options.DisplayName,
            DbFlavor.Sqlite,
            this.options.IsTestDatabase,
            this.CurrentVersion());

        public int CurrentVersion()
        {
            if (!this.HistoryTableExists())
            {
                return 0;
            }

            using (var cmd = new SQLiteCommand(
                "SELECT COALESCE(MAX(Version), 0) FROM __SchemaHistory;", this.connection))
            {
                return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
            }
        }

        public IReadOnlyList<AppliedStep> AppliedSteps()
        {
            var applied = new List<AppliedStep>();
            if (!this.HistoryTableExists())
            {
                return applied;
            }

            using (var cmd = new SQLiteCommand(
                "SELECT Version, StepName, ChecksumHash, AppliedUtc, AppliedBy "
                + "FROM __SchemaHistory ORDER BY Version;", this.connection))
            using (SQLiteDataReader reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    applied.Add(new AppliedStep(
                        reader.GetInt32(0), reader.GetString(1), reader.GetString(2),
                        reader.GetString(3), reader.GetString(4)));
                }
            }

            return applied;
        }

        public void ApplyTo(int targetVersion)
        {
            if (targetVersion < 0 || targetVersion > SchemaStepCatalog.LatestVersion)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(targetVersion),
                    targetVersion,
                    $"This build carries schema steps 1..{SchemaStepCatalog.LatestVersion}.");
            }

            this.EnsureHistoryTable();
            this.VerifyNoDriftInAppliedSteps();

            int current = this.CurrentVersion();

            foreach (SchemaStep step in SchemaStepCatalog.All)
            {
                if (step.Version <= current || step.Version > targetVersion)
                {
                    continue;
                }

                // One transaction PER STEP, with the ledger row written inside it. A failure at
                // step 9 of 12 must leave the database at version 8, not at "version 0 but
                // actually partly at 9" - spec section 1.5, S-3 rule 3. BEGIN IMMEDIATE takes the
                // write lock up front rather than upgrading mid-transaction.
                //
                // IsolationLevel.Serializable is this provider's BEGIN IMMEDIATE (ReadCommitted is
                // its BEGIN DEFERRED). The plan wrote BeginTransaction(deferredLock: false), which
                // is the same thing but is marked [Obsolete] "will be removed soon" - and this
                // repo builds warning-free.
                using (SQLiteTransaction tx = this.connection.BeginTransaction(IsolationLevel.Serializable))
                {
                    try
                    {
                        using (var cmd = new SQLiteCommand(step.Sql, this.connection, tx))
                        {
                            cmd.ExecuteNonQuery();
                        }

                        using (var cmd = new SQLiteCommand(
                            "INSERT INTO __SchemaHistory (Version, StepName, ChecksumHash, AppliedUtc, AppliedBy) "
                            + "VALUES (@v, @n, @c, @u, @b);", this.connection, tx))
                        {
                            cmd.Parameters.AddWithValue("@v", step.Version);
                            cmd.Parameters.AddWithValue("@n", step.Name);
                            cmd.Parameters.AddWithValue("@c", step.ChecksumHash);
                            cmd.Parameters.AddWithValue("@u", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
                            cmd.Parameters.AddWithValue("@b", Environment.UserName ?? "unknown");
                            cmd.ExecuteNonQuery();
                        }

                        tx.Commit();
                    }
                    catch (Exception original)
                    {
                        try
                        {
                            tx.Rollback();
                        }
                        catch (Exception rollbackFailure)
                        {
                            throw new InvalidOperationException(
                                $"Rollback failed after schema step {step.Version} ('{step.Name}') failed: "
                                + original.Message,
                                rollbackFailure);
                        }

                        throw new InvalidOperationException(
                            $"Schema step {step.Version} ('{step.Name}') failed; the database is still at "
                            + $"version {step.Version - 1}.",
                            original);
                    }
                }
            }
        }

        // DropAll, Backup and Delete arrive in Tasks 18, 9 and 9.

        /// <summary>
        /// The expected object set is NOT a hand-maintained list. It is produced by paving a
        /// scratch :memory: database to this database's current version with the very same
        /// executor, then introspecting that. Hand-list-free by construction (issue #34's lesson,
        /// spec section 2.6.4), identical to the comparison slice 2b's equality test performs, and
        /// cheap enough to be an assertion rather than a ritual - a :memory: pave is DDL against
        /// an empty page cache with no fsync and no journal (spec section 2.4).
        /// </summary>
        public SchemaVerifyResult Verify()
        {
            int version = this.CurrentVersion();
            SchemaSnapshot actual = SchemaSnapshot.Capture(this.connection);
            SchemaSnapshot expected = CaptureExpected(version);

            return new SchemaVerifyResult(version, SchemaSnapshot.Diff(expected, actual));
        }

        /// <summary>Pave a throwaway in-memory database to <paramref name="version"/> and introspect it.</summary>
        public static SchemaSnapshot CaptureExpected(int version)
        {
            using (var scratch = Open(new SqliteProvisioningOptions(
                "<expected>", SqliteConnectionFactory.InMemoryDataSource, true)))
            {
                scratch.ApplyTo(version);
                return SchemaSnapshot.Capture(scratch.Connection);
            }
        }

        /// <summary>
        /// Drops every object this schema family owns, INCLUDING __SchemaHistory, so
        /// CurrentVersion() returns 0 afterwards and the next ApplyTo(N) is literally
        /// indistinguishable from creating a new database - spec section 1.5, S-1.
        ///
        /// Views before tables (required on SQL Server, done here for parity of the step list -
        /// spec section 2.7.1), then triggers, then tables in reverse-FK order. Idempotent: every
        /// statement is IF EXISTS and the object list is introspected each time. Task 21 puts
        /// TestDatabaseGuard.Require at the top of this method.
        /// </summary>
        public void DropAll()
        {
            // FIRST statement, so a refusal leaves nothing partially done.
            TestDatabaseGuard.Require(this.Identity, "Drop the whole schema");

            using (SQLiteTransaction tx = this.connection.BeginTransaction(IsolationLevel.Serializable))
            {
                try
                {
                    using (var cmd = new SQLiteCommand("PRAGMA defer_foreign_keys = ON;", this.connection, tx))
                    {
                        cmd.ExecuteNonQuery();
                    }

                    foreach (string view in ObjectNames(this.connection, tx, "view"))
                    {
                        Execute(this.connection, tx, $"DROP VIEW IF EXISTS \"{Quote(view)}\";");
                    }

                    foreach (string trigger in ObjectNames(this.connection, tx, "trigger"))
                    {
                        Execute(this.connection, tx, $"DROP TRIGGER IF EXISTS \"{Quote(trigger)}\";");
                    }

                    foreach (string table in SqliteTableOrder.ForDelete(this.connection))
                    {
                        Execute(this.connection, tx, $"DROP TABLE IF EXISTS \"{Quote(table)}\";");
                    }

                    Execute(this.connection, tx, $"DROP TABLE IF EXISTS \"{SqliteTableOrder.HistoryTable}\";");

                    tx.Commit();
                }
                catch
                {
                    tx.Rollback();
                    throw;
                }
            }
        }

        private static IReadOnlyList<string> ObjectNames(SQLiteConnection connection, SQLiteTransaction tx, string type)
        {
            var names = new List<string>();
            using (var cmd = new SQLiteCommand(
                "SELECT name FROM sqlite_master WHERE type = @type AND name NOT LIKE 'sqlite_%';",
                connection, tx))
            {
                cmd.Parameters.AddWithValue("@type", type);
                using (SQLiteDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        names.Add(reader.GetString(0));
                    }
                }
            }

            return names;
        }

        private static void Execute(SQLiteConnection connection, SQLiteTransaction tx, string sql)
        {
            using (var cmd = new SQLiteCommand(sql, connection, tx))
            {
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// An identifier cannot be parameterized in any SQL dialect, so interpolation is
        /// unavoidable here. The names come from sqlite_master introspection, never from a caller,
        /// and a name containing a double quote is rejected rather than escaped - global
        /// constraint R-CRUD-1's identifier rule.
        /// </summary>
        private static string Quote(string identifier)
        {
            if (identifier.IndexOf('"') >= 0)
            {
                throw new InvalidOperationException($"Refusing to build SQL for the identifier {identifier}.");
            }

            return identifier;
        }

        /// <summary>
        /// VACUUM INTO rather than close-and-File.Copy: it is transactionally consistent and does
        /// not require closing a live WAL database - spec sections 1.7.1 and 3.2. The filename is
        /// a bound parameter, not concatenated (R-CRUD-1).
        ///
        /// VACUUM INTO refuses an existing destination, so an overwrite is explicit. Spec section
        /// 3.2 requires Backup to be able to overwrite.
        ///
        /// It vacuums to a scratch file BESIDE the destination and moves it into place only once
        /// the vacuum has succeeded. Deleting the destination first and then vacuuming would mean
        /// a failure half way - a full disk, a denied write, a corrupt page - destroys the
        /// previous backup and produces no new one, which is the one outcome a backup routine
        /// must never have.
        ///
        /// Task 21 puts TestDatabaseGuard in front of the destructive operations on this class;
        /// Backup is not one of them - it only ever writes a NEW file.
        /// </summary>
        public void Backup(string destinationPath)
        {
            if (string.IsNullOrWhiteSpace(destinationPath))
            {
                throw new ArgumentException("A backup destination path is required.", nameof(destinationPath));
            }

            string scratchPath = destinationPath + ".backup-tmp";
            if (System.IO.File.Exists(scratchPath))
            {
                System.IO.File.Delete(scratchPath);
            }

            try
            {
                using (var cmd = new SQLiteCommand("VACUUM INTO @path;", this.connection))
                {
                    cmd.Parameters.AddWithValue("@path", scratchPath);
                    cmd.ExecuteNonQuery();
                }

                System.IO.File.Move(scratchPath, destinationPath, true);
            }
            catch
            {
                try
                {
                    System.IO.File.Delete(scratchPath);
                }
                catch (System.IO.IOException)
                {
                    // Leaving the scratch file behind is not worth masking the real failure.
                }

                throw;
            }
        }

        public void Delete()
        {
            // FIRST statement, so a refusal leaves nothing partially done.
            TestDatabaseGuard.Require(this.Identity, "Delete the database");

            string dataSource = this.options.DataSource;
            this.connection.Close();

            if (!SqliteConnectionFactory.IsInMemory(dataSource) && System.IO.File.Exists(dataSource))
            {
                System.IO.File.Delete(dataSource);
            }
        }

        public void Dispose() => this.connection?.Dispose();

        private bool HistoryTableExists()
        {
            using (var cmd = new SQLiteCommand(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '__SchemaHistory';",
                this.connection))
            {
                return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
            }
        }

        /// <summary>
        /// __SchemaHistory is step 0: the executor creates it before the ledger exists to record
        /// it. That is the one genuine bootstrap exception, and it is the same exception on both
        /// engines - spec section 1.5, S-2.
        /// </summary>
        private void EnsureHistoryTable()
        {
            using (var cmd = new SQLiteCommand(
                "CREATE TABLE IF NOT EXISTS __SchemaHistory ("
                + "Version INTEGER NOT NULL PRIMARY KEY,"
                + "StepName TEXT NOT NULL,"
                + "ChecksumHash TEXT NOT NULL,"
                + "AppliedUtc TEXT NOT NULL,"
                + "AppliedBy TEXT NOT NULL) STRICT;",
                this.connection))
            {
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Spec section 1.5, S-3 rule 1: a step is never edited once it has been applied
        /// ANYWHERE. This is the detection that makes that rule observable. It is on from the
        /// first commit rather than "when it matters", because under nuke-and-pave (section 6.7)
        /// breaking the rule is free and invisible, so it is the first rule to erode - and
        /// turning the check on later means turning it on against a corpus of steps nobody was
        /// disciplined about.
        /// </summary>
        private void VerifyNoDriftInAppliedSteps()
        {
            var known = new Dictionary<int, SchemaStep>();
            foreach (SchemaStep step in SchemaStepCatalog.All)
            {
                known[step.Version] = step;
            }

            foreach (AppliedStep applied in this.AppliedSteps())
            {
                if (!known.TryGetValue(applied.Version, out SchemaStep step))
                {
                    throw new SchemaDriftException(
                        $"'{this.options.DisplayName}' records schema step {applied.Version} "
                        + $"('{applied.StepName}'), which this build does not carry. This database was "
                        + $"paved by a newer build; this one knows steps 1..{SchemaStepCatalog.LatestVersion}.");
                }

                if (!string.Equals(applied.ChecksumHash, step.ChecksumHash, StringComparison.Ordinal))
                {
                    throw new SchemaDriftException(
                        $"Schema step {applied.Version} ('{step.Name}') has been edited since it was "
                        + $"applied to '{this.options.DisplayName}' on {applied.AppliedUtc} by "
                        + $"{applied.AppliedBy}. An applied step is immutable - a mistake in it is "
                        + "fixed by a new step, never by editing it, or every database that already "
                        + "ran it silently disagrees with every database that has not.");
                }
            }
        }
    }
}
