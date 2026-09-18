namespace Walkabout.Data
{
    /// <summary>
    /// MyMoney.Data's implementation of MyMoney.Business's IDatabasePasswordStore
    /// seam: the Windows Credential Manager wrapper (DatabaseSecurity, backed by the
    /// P/Invoke Credential class) lives in this assembly, which MyMoney.Business
    /// cannot reference.
    /// </summary>
    public class DatabaseSecurityPasswordStore : IDatabasePasswordStore
    {
        public string LoadPassword(string databaseName)
        {
            return DatabaseSecurity.LoadDatabasePassword(databaseName);
        }

        public void SavePassword(string databaseName, string password)
        {
            DatabaseSecurity.SaveDatabasePassword(databaseName, password);
        }
    }
}
