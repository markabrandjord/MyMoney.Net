using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace MyMoney.Shell.Services;

// D-15: form-shaped dialogs become shell-hosted Fluent content dialogs. D-14: one declared
// default/cancel mechanism - ContentDialog's own Primary/Close buttons, not a hand-rolled
// second one. D-13: this service does not touch the caller's data at all; cancel-restores
// is the caller's editing-scope responsibility (Task 9), this service only reports which
// button was pressed.
public sealed class DialogService : IDialogService
{
    private ContentPresenter? host;

    public void SetHost(ContentPresenter presenter)
    {
        if (this.host is not null)
        {
            throw new InvalidOperationException(
                "DialogService already has a host. Exactly one ContentPresenter may host dialogs - " +
                "this is what guarantees only one dialog can be shown at a time.");
        }

        this.host = presenter;
    }

    public async Task<DialogOutcome> ShowAsync(FrameworkElement content, string title, string primaryButtonText)
    {
        if (this.host is null)
        {
            throw new InvalidOperationException(
                "DialogService has no host. Call SetHost from the shell window before showing a dialog.");
        }

        var dialog = new ContentDialog
        {
            Title = title,
            Content = content,
            PrimaryButtonText = primaryButtonText,
            CloseButtonText = "Cancel",
#pragma warning disable CS0618 // ContentDialog.DialogHost(ContentPresenter) is WPF-UI 4.3.0's deprecated
                               // "legacy host" property/constructor overload, superseded by
                               // ContentDialogHost. The plan intentionally uses the ContentPresenter
                               // shape here (SetHost's signature, see brief's Step 3) rather than
                               // adopting ContentDialogHost, which is a bigger XAML/API surface change
                               // out of scope for this task.
            DialogHost = this.host,
#pragma warning restore CS0618
        };

        var result = await dialog.ShowAsync();

        return result == ContentDialogResult.Primary
            ? DialogOutcome.Committed
            : DialogOutcome.Cancelled;
    }
}
