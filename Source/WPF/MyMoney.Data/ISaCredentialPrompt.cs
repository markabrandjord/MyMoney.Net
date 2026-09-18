namespace Walkabout.Data
{
    /// <summary>
    /// Kept WPF-agnostic so SqlServerBootstrapper (MyMoney.Data, no WPF
    /// reference) can prompt for 'sa' without depending on the UI layer.
    /// Implemented by MyMoney's SaCredentialDialog.
    /// </summary>
    public interface ISaCredentialPrompt
    {
        /// <returns>false if the user cancelled.</returns>
        bool TryGetSaPassword(string server, out string password);
    }
}
