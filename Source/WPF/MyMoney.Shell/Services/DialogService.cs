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
//
// Not sealed: DisplayAsync below is a protected testing seam (see DialogServiceTests'
// ControllableDialogService) so the "one dialog at a time" guard can be exercised without a
// real ContentDialog, which needs a live visual tree and user interaction to ever complete
// ShowAsync().
public class DialogService : IDialogService
{
    private ContentPresenter? host;
    private bool isShowing;

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

        if (this.isShowing)
        {
            throw new InvalidOperationException(
                "A dialog is already being shown. DialogService only supports one dialog at a time.");
        }

        this.isShowing = true;
        try
        {
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

            var result = await this.DisplayAsync(dialog);

            return result == ContentDialogResult.Primary
                ? DialogOutcome.Committed
                : DialogOutcome.Cancelled;
        }
        finally
        {
            this.isShowing = false;
        }
    }

    // Testing seam: overridden by a test double so tests can control exactly when a "shown"
    // dialog completes, without needing a real ContentDialog inside a live visual tree.
    protected virtual Task<ContentDialogResult> DisplayAsync(ContentDialog dialog) => dialog.ShowAsync();
}
