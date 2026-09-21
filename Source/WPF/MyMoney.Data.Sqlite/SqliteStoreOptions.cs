namespace Walkabout.Data.Sqlite
{
    /// <summary>
    /// What an open store handle needs to know about the database it is talking to. IsTestDatabase
    /// comes from DatabaseEntry.TestDatabase, a permanent per-database attribute fixed at creation
    /// (spec section 4 item 8), and is a bool defaulting to false so every way it can break
    /// resolves to "the guarded capability refuses" - spec section 1.9.2.
    /// </summary>
    public sealed record SqliteStoreOptions(string DisplayName, string DataSource, bool IsTestDatabase);
}
