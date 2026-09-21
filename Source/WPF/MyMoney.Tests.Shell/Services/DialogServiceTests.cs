using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using MyMoney.Shell.Services;
using NUnit.Framework;

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
        var content = new TextBlock();

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
}
