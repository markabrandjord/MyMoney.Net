using MyMoney.Shell.Services;
using NUnit.Framework;

namespace MyMoney.Tests.Shell.Services;

[TestFixture]
public class StatusServiceTests
{
    [Test]
    public void CurrentStatus_DefaultsToReady()
    {
        var service = new StatusService();
        Assert.That(service.CurrentStatus, Is.EqualTo("Ready"));
    }

    [Test]
    public void ShowStatus_SetsCurrentStatusAndRaisesChanged()
    {
        var service = new StatusService();
        var raised = false;
        service.Changed += (_, _) => raised = true;

        service.ShowStatus("Account added: Vacation Fund");

        Assert.That(service.CurrentStatus, Is.EqualTo("Account added: Vacation Fund"));
        Assert.That(raised, Is.True);
    }

    [Test]
    public void LogActivity_AddsToFrontOfActivityAndDoesNotChangeCurrentStatus()
    {
        var service = new StatusService();
        service.ShowStatus("Ready");

        service.LogActivity("Added account 'Vacation Fund' (Investment)");
        service.LogActivity("Added account 'Checking' (Bank)");

        Assert.That(service.Activity, Has.Count.EqualTo(2));
        Assert.That(service.Activity[0].Message, Is.EqualTo("Added account 'Checking' (Bank)"));
        Assert.That(service.Activity[1].Message, Is.EqualTo("Added account 'Vacation Fund' (Investment)"));
        Assert.That(service.CurrentStatus, Is.EqualTo("Ready"));
    }

    [Test]
    public void LogActivity_DoesNotShowOnTheEphemeralChannel()
    {
        // D-5: background/logged chatter never uses the ephemeral channel.
        var service = new StatusService();
        var statusChanges = 0;
        service.Changed += (_, _) => statusChanges++;

        service.LogActivity("Something happened in the background");

        Assert.That(service.CurrentStatus, Is.EqualTo("Ready"));
        // Changed still fires once, for the activity list update - callers re-read both
        // CurrentStatus and Activity off the same event rather than getting two events.
        Assert.That(statusChanges, Is.EqualTo(1));
    }
}
