using System;

namespace Walkabout.Data
{
    /// <summary>
    /// ONE guard for every capability that must only ever touch a test database.
    ///
    /// Scope, in the spec's corrected words (section 1.8 as widened by revision 8): this is not
    /// only about DESTRUCTIVE operations. There are two kinds - operations that destroy data, and
    /// operations that fabricate data indistinguishable from real data. Sample-data generation is
    /// squarely the second and is not destructive at all, so a reader who stops at the word
    /// "destructive" would wrongly conclude it is out of scope.
    ///
    /// ALWAYS COMPILED IN, in every configuration, and never in a *.TestKit* assembly: "the
    /// assembly isn't shipped" protects end users but does not protect a developer running a test
    /// suite against the wrong registry entry, and the sample-data feature is deliberately
    /// reachable by a customer in a release build.
    ///
    /// What this cannot claim (spec section 6.9, stated so nobody overclaims later): it is a
    /// runtime check in a shipped assembly, the weakest of this design's three protections. It
    /// stops the FEATURE, not the DATA - a caller with the database open can still write ordinary
    /// rows through SaveRoots, and a caller who assembles a sample set by hand has simply left the
    /// feature. What it can claim: always compiled in, one code path rather than two that drift,
    /// runs before anything is written so a refusal leaves nothing partial, and every failure mode
    /// of its input resolves to "refuse".
    /// </summary>
    public static class TestDatabaseGuard
    {
        public static void Require(StoreIdentity identity, string capability)
        {
            if (identity == null)
            {
                throw new ArgumentNullException(nameof(identity));
            }

            if (!identity.IsTestDatabase)
            {
                throw new TestDatabaseRequiredException(identity.DisplayName, capability);
            }
        }
    }

    /// <summary>
    /// Surfaced through IBusinessLayerUiCallback as plain language, never as a type name or a raw
    /// engine string - spec section 7's UI rule.
    /// </summary>
    public sealed class TestDatabaseRequiredException : Exception
    {
        public TestDatabaseRequiredException(string databaseDisplayName, string capability)
            : base($"'{databaseDisplayName}' is not marked as a test database, so '{capability}' is "
                   + "not allowed on it. Create a new database with 'Test database' checked if you "
                   + "want to try this out.")
        {
            this.DatabaseDisplayName = databaseDisplayName;
            this.Capability = capability;
        }

        public string DatabaseDisplayName { get; }

        public string Capability { get; }
    }
}
