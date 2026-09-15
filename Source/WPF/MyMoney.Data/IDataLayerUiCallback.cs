namespace Walkabout.Data
{
    /// <summary>
    /// The data layer has no WPF dependency, so it cannot show a message
    /// box or own a dialog directly. The two call sites that used to do
    /// this (SqlServerDatabase.AddLogin's mixed-mode warning, and the
    /// sample-database dialog owner lookup that lives with SampleDatabase
    /// in MyMoney.csproj) go through this instead. MyMoney.csproj supplies
    /// the real, WPF-backed implementation.
    /// </summary>
    public interface IDataLayerUiCallback
    {
        void ShowWarning(string message, string title);
    }
}
