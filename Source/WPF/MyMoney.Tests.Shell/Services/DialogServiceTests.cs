using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using MyMoney.Shell.Services;
using NUnit.Framework;
using Wpf.Ui.Controls;

namespace MyMoney.Tests.Shell.Services;

// WPF FrameworkElement-derived types (TextBlock, ContentPresenter) assert the calling thread is
// STA the moment they're constructed (System.Windows.Input.InputManager's ctor). NUnit's default
// test-runner thread is MTA, so these tests need an explicit STA apartment - the first tests in
// this suite to construct real WPF elements rather than plain service/POCO types.
[TestFixture]
[Apartment(ApartmentState.STA)]
public class DialogServiceTests
{
    [Test]
    public void ShowAsync_WithNoHostSet_Throws()
    {
        var service = new DialogService();
        var content = new System.Windows.Controls.TextBlock();

        Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.ShowAsync(content, "Title", "OK"));
    }

    [Test]
    public void SetHost_TwiceThrows()
    {
        var service = new DialogService();
        var presenter1 = new ContentPresenter();
        var presenter2 = new ContentPresenter();

        service.SetHost(presenter1);

        Assert.Throws<InvalidOperationException>(() => service.SetHost(presenter2));
    }

    [Test]
    public async Task ShowAsync_WhileAlreadyShowing_ThrowsAndFirstDialogStillCompletesAfterward()
    {
        // A real ContentDialog.ShowAsync() never completes without a live visual tree and a
        // button click, so this uses ControllableDialogService's DisplayAsync seam to hold the
        // "first dialog" open under our control while we make the overlapping second call.
        var service = new ControllableDialogService();
        var presenter = new ContentPresenter();
        service.SetHost(presenter);

        var firstShow = service.ShowAsync(new System.Windows.Controls.TextBlock(), "First", "OK");

        Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.ShowAsync(new System.Windows.Controls.TextBlock(), "Second", "OK"));

        // The guard must not have wedged the service - completing the first dialog still
        // resolves normally, and a third call after that succeeds (the guard resets).
        service.CompletePending(ContentDialogResult.Primary);
        Assert.That(await firstShow, Is.EqualTo(DialogOutcome.Committed));

        var thirdShow = service.ShowAsync(new System.Windows.Controls.TextBlock(), "Third", "OK");
        service.CompletePending(ContentDialogResult.None);
        Assert.That(await thirdShow, Is.EqualTo(DialogOutcome.Cancelled));
    }

    private sealed class ControllableDialogService : DialogService
    {
        private TaskCompletionSource<ContentDialogResult>? pending;

        protected override Task<ContentDialogResult> DisplayAsync(ContentDialog dialog)
        {
            this.pending = new TaskCompletionSource<ContentDialogResult>();
            return this.pending.Task;
        }

        public void CompletePending(ContentDialogResult result) => this.pending!.SetResult(result);
    }
}
