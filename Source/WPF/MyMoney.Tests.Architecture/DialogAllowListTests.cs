using System;
using System.IO;
using System.Linq;
using System.Windows;
using NUnit.Framework;

namespace Walkabout.Tests.Architecture;

/// <summary>
/// D-15: "a base class plus an automated check that every Window-derived type conforms, with a
/// short commented opt-out list. The check is worth building now, on the current codebase,
/// independent of the redesign's timing." No shared base class exists in the legacy app yet and
/// this plan does not retrofit one (D-15 rejected that) - so today's check is narrower and still
/// real: every Window-derived type in the two WPF applications must be a deliberate, recorded
/// decision, not silent growth. A new dialog either gets added to the relevant allow-list (a
/// conscious choice, reviewable in the diff) or the build fails.
///
/// BOTH apps are checked. The legacy MyMoney.csproj app is where the existing dialogs live;
/// MyMoney.Shell is where new ones will actually be written from here on, and it has exactly one
/// Window today (MainWindow, via WPF-UI's FluentWindow), which makes this the cheapest possible
/// moment to lock it in.
/// </summary>
[TestFixture]
public class DialogAllowListTests
{
    // Referencing a type from each assembly directly (rather than Assembly.LoadFrom over a
    // guessed output path) means this test can't silently pass against the wrong assembly - if
    // the project isn't referenced/built, this fails to compile/load rather than quietly
    // finding nothing.
    [TestCase(typeof(Walkabout.MainWindow), "allowed-dialog-windows.txt")]
    [TestCase(typeof(MyMoney.Shell.MainWindow), "allowed-dialog-windows-shell.txt")]
    public void EveryWindowDerivedType_IsOnTheAllowList(Type assemblyAnchor, string allowListFileName)
    {
        var assembly = assemblyAnchor.Assembly;

        var allowListPath = Path.GetFullPath(Path.Combine(
            TestContext.CurrentContext.TestDirectory, "..", "..", "..", allowListFileName));
        var allowList = File.ReadAllLines(allowListPath)
            .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith("#", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

        var actualWindowTypes = assembly.GetTypes()
            .Where(t => typeof(Window).IsAssignableFrom(t) && !t.IsAbstract)
            .Select(t => t.FullName!)
            .ToList();

        var undocumented = actualWindowTypes.Except(allowList).ToList();

        Assert.That(undocumented, Is.Empty,
            $"Window-derived type(s) in {assembly.GetName().Name} not on the allow-list (add them "
            + $"deliberately to {allowListFileName} if this addition is intentional): "
            + string.Join(", ", undocumented));

        // The reverse direction matters just as much: without it, a DELETED dialog leaves its
        // entry behind forever, and a stale allow-list is one that no longer says what it claims
        // to say - it would also silently re-permit a type of the same name reappearing later.
        var stale = allowList.Except(actualWindowTypes).ToList();

        Assert.That(stale, Is.Empty,
            $"{allowListFileName} lists type(s) that no longer exist in {assembly.GetName().Name} "
            + "(remove them, so the list keeps meaning what it says): " + string.Join(", ", stale));
    }
}
