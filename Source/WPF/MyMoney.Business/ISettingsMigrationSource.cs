namespace Walkabout.Data
{
    /// <summary>
    /// DatabaseSettings.MigrateSettings needs two values from the
    /// application-wide (WPF) Settings object for a one-time migration.
    /// This interface lets it depend on the shape it needs instead of the
    /// concrete Walkabout.Configuration.Settings type, which stays in
    /// MyMoney.csproj. The real Settings class implements this in
    /// MyMoney.csproj (Task 4).
    /// </summary>
    public interface ISettingsMigrationSource
    {
        int MigrateFiscalYearStart();

        bool MigrateRentalManagement();
    }
}
