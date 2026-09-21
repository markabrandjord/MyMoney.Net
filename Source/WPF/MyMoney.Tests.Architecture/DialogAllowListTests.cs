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
/// real: every Window-derived type in the legacy MyMoney.csproj app must be a deliberate,
/// recorded decision, not silent growth. A new dialog either gets added to the allow-list (a
/// conscious choice, reviewable in the diff) or the build fails.
/// </summary>
[TestFixture]
public class DialogAllowListTests
{
    [Test]
    public void EveryWindowDerivedType_IsOnTheAllowList()
    {
        // Referencing the type directly (rather than Assembly.LoadFrom over a guessed output
        // path) means this test can't silently pass against the wrong assembly - if MyMoney.csproj
        // isn't referenced/built, this fails to compile/load rather than quietly finding nothing.
        var assembly = typeof(Walkabout.MainWindow).Assembly;

        var allowListPath = Path.GetFullPath(Path.Combine(
            TestContext.CurrentContext.TestDirectory, "..", "..", "..", "allowed-dialog-windows.txt"));
        var allowList = File.ReadAllLines(allowListPath)
            .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith("#", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

        var actualWindowTypes = assembly.GetTypes()
            .Where(t => typeof(Window).IsAssignableFrom(t) && !t.IsAbstract)
            .Select(t => t.FullName!)
            .ToList();

        var undocumented = actualWindowTypes.Except(allowList).ToList();

        Assert.That(undocumented, Is.Empty,
            "Window-derived type(s) not on the allow-list (add them deliberately to " +
            "allowed-dialog-windows.txt if this addition is intentional): " +
            string.Join(", ", undocumented));
    }
}
