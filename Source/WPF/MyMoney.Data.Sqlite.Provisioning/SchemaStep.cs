using System;
using System.Security.Cryptography;
using System.Text;

namespace Walkabout.Data.Sqlite.Provisioning
{
    /// <summary>
    /// One numbered, immutable unit of DDL. A mistake in step 7 is fixed by step 8, never by
    /// editing step 7 - spec section 1.5, S-3 rule 1. ChecksumHash is what makes that rule
    /// OBSERVABLE during a phase where breaking it is otherwise free.
    /// </summary>
    public sealed record SchemaStep(int Version, string Name, string Sql, string ChecksumHash)
    {
        /// <summary>
        /// SHA-256 over the step SQL with line endings normalised to \n.
        ///
        /// The normalisation is load-bearing, not tidiness: .sql files are checked out with
        /// platform line endings, so without it the same step would hash differently on two
        /// machines and every database paved by the other one would look like it had run an
        /// edited step. Same class of trap CLAUDE.md records for core.autocrlf + .gitattributes.
        /// </summary>
        public static string ComputeChecksum(string sql)
        {
            string normalised = sql.Replace("\r\n", "\n").Replace("\r", "\n");
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalised));
            return Convert.ToHexString(hash);
        }
    }
}
