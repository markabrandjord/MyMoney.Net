using System;
using System.Collections.Generic;

namespace Walkabout.Data
{
    /// <summary>One difference between an expected and an actual schema object.</summary>
    public sealed record SchemaDifference(string ObjectKind, string ObjectName, string Expected, string Actual);

    public sealed record SchemaVerifyResult(int Version, IReadOnlyList<SchemaDifference> Differences)
    {
        public bool IsClean => this.Differences.Count == 0;
    }

    /// <summary>
    /// Thrown when an already-recorded step's checksum no longer matches the step SQL in this
    /// build - i.e. someone edited an applied step. Spec section 1.5, S-3 rule 1: during
    /// nuke-and-pave that rule is cheap to follow AND cheap to break, so the check is on from day
    /// one rather than turned on later against a corpus nobody was disciplined about.
    /// </summary>
    public class SchemaDriftException : Exception
    {
        public SchemaDriftException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// Thrown at open when the database is at a HIGHER schema version than this binary knows.
    /// ApplyTo handles "the app is newer than the database"; the reverse needs a refusal, not a
    /// best-effort open - spec section 1.8's second bullet.
    /// </summary>
    public class SchemaTooNewException : Exception
    {
        public SchemaTooNewException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// Create the file, run the schema, verify, back up, delete. ONE mechanism, several entry
    /// points: creating a database is ApplyTo(N) from version 0, nuke-and-pave is DropAll() then
    /// ApplyTo(N), and the eventual production upgrade is ApplyTo(N) from version M. They differ
    /// only in what CurrentVersion() returns when they start, which is what makes the upgrade
    /// path run on every single fresh creation. Spec section 1.5, S-1.
    /// </summary>
    public interface IMoneyStoreProvisioner : IDisposable
    {
        StoreIdentity Identity { get; }

        /// <summary>The highest step number this binary carries.</summary>
        int LatestKnownVersion { get; }

        /// <summary>Highest fully-applied step; 0 on an empty database.</summary>
        int CurrentVersion();

        /// <summary>Apply every unapplied step from CurrentVersion()+1 up to targetVersion.</summary>
        void ApplyTo(int targetVersion);

        /// <summary>Introspected actual schema vs. expected for the current version.</summary>
        SchemaVerifyResult Verify();

        /// <summary>
        /// Drop every object this schema family owns. DESTRUCTIVE. Lives on the SHIPPED
        /// provisioner rather than only in the never-shipped test tier, because "the assembly
        /// isn't shipped" protects end users but does not protect a developer running a test
        /// suite against the wrong registry entry (spec section 1.8). Task 21 puts
        /// TestDatabaseGuard.Require in front of it.
        /// </summary>
        void DropAll();

        /// <summary>
        /// Transactionally-consistent copy, overwriting destinationPath if it exists. Present
        /// from the first slice on spec section 1.8's third compatibility property: a backup
        /// routine that only appears when production appears is one that has never been restored
        /// from.
        /// </summary>
        void Backup(string destinationPath);

        /// <summary>Remove the database entirely. DESTRUCTIVE; guarded in Task 21.</summary>
        void Delete();
    }
}
