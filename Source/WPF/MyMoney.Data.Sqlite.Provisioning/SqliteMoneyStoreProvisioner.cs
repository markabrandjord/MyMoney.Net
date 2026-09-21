using System;
using System.Collections.Generic;
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
                using (SQLiteTransaction tx = this.connection.BeginTransaction(deferredLock: false))
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

        // Verify, DropAll, Backup and Delete arrive in Tasks 8, 18, 9 and 9.
        public SchemaVerifyResult Verify() => throw new NotImplementedException("Task 8.");

        public void DropAll() => throw new NotImplementedException("Task 18.");

        public void Backup(string destinationPath) => throw new NotImplementedException("Task 9.");

        public void Delete() => throw new NotImplementedException("Task 9.");

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
