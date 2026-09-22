using System.Globalization;
using System.Resources;

namespace MyMoney.Shell.Resources;

/// <summary>
/// Strongly-typed access to Strings.resx, the Shell's single source of user-facing text (see
/// the design spec's localization entry under D-9). Every property here is a real,
/// satellite-assembly-ready .NET resource lookup via <see cref="ResourceManager"/> - adding a
/// culture-specific "Strings.&lt;culture&gt;.resx" (e.g. Strings.fr.resx) picks it up
/// automatically at runtime with zero code changes, the standard .NET localization mechanism.
/// Hand-written rather than Visual Studio's auto-generated Designer.cs, since this project is
/// built via `dotnet build`/CLI rather than through VS's design-time resx tooling - the
/// generated shape (one static string property per resx key, wrapping GetString by
/// nameof(property)) is otherwise identical.
/// </summary>
public static class Strings
{
    private static readonly ResourceManager Manager =
        new ResourceManager("MyMoney.Shell.Resources.Strings", typeof(Strings).Assembly);

    private static string Get(string name) =>
        Manager.GetString(name, CultureInfo.CurrentUICulture)
        ?? throw new InvalidOperationException($"Missing string resource '{name}' - check Strings.resx.");

    public static string SearchPlaceholder => Get(nameof(SearchPlaceholder));
    public static string SearchPreviousTooltip => Get(nameof(SearchPreviousTooltip));
    public static string SearchNextTooltip => Get(nameof(SearchNextTooltip));
    public static string ThemeLight => Get(nameof(ThemeLight));
    public static string ThemeDark => Get(nameof(ThemeDark));
    public static string ThemeFollowSystem => Get(nameof(ThemeFollowSystem));
    public static string NavAccounts => Get(nameof(NavAccounts));
    public static string StatusReady => Get(nameof(StatusReady));
    public static string ActivityButtonFormat => Get(nameof(ActivityButtonFormat));
    public static string NoMatchesStatusFormat => Get(nameof(NoMatchesStatusFormat));

    public static string AddAccountButtonLabel => Get(nameof(AddAccountButtonLabel));
    public static string EditAccountButtonLabel => Get(nameof(EditAccountButtonLabel));
    public static string DeleteAccountButtonLabel => Get(nameof(DeleteAccountButtonLabel));
    public static string AddAccountDialogTitle => Get(nameof(AddAccountDialogTitle));
    public static string AddAccountCommitLabel => Get(nameof(AddAccountCommitLabel));
    public static string EditAccountDialogTitle => Get(nameof(EditAccountDialogTitle));
    public static string EditAccountCommitLabel => Get(nameof(EditAccountCommitLabel));
    public static string DeleteAccountDialogTitle => Get(nameof(DeleteAccountDialogTitle));
    public static string DeleteAccountCommitLabel => Get(nameof(DeleteAccountCommitLabel));
    public static string AccountAddedStatusFormat => Get(nameof(AccountAddedStatusFormat));
    public static string AccountAddedActivityFormat => Get(nameof(AccountAddedActivityFormat));
    public static string AccountUpdatedStatusFormat => Get(nameof(AccountUpdatedStatusFormat));
    public static string AccountUpdatedActivityFormat => Get(nameof(AccountUpdatedActivityFormat));
    public static string AccountDeletedStatusFormat => Get(nameof(AccountDeletedStatusFormat));
    public static string AccountDeletedActivityFormat => Get(nameof(AccountDeletedActivityFormat));

    public static string FieldLabelName => Get(nameof(FieldLabelName));
    public static string FieldLabelType => Get(nameof(FieldLabelType));
    public static string FieldLabelCurrency => Get(nameof(FieldLabelCurrency));
    public static string FieldLabelOpeningBalance => Get(nameof(FieldLabelOpeningBalance));

    public static string DeleteAccountConfirmPrefix => Get(nameof(DeleteAccountConfirmPrefix));
    public static string DeleteAccountConfirmSuffix => Get(nameof(DeleteAccountConfirmSuffix));
}
