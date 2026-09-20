// This file is used by Code Analysis to maintain SuppressMessage
// attributes that are applied to this project.
// Project-level suppressions either have no target or are given
// a specific target and scoped to a namespace, type, member, etc.

using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage("Assertion", "NUnit2005:Consider using Assert.That(actual, Is.EqualTo(expected)) instead of Assert.AreEqual(expected, actual)", Justification = "114 existing Assert.AreEqual/AreNotEqual/AreSame call sites across Walkabout.Tests as of 2026-09-19 - converting them to the constraint model would be a large, purely mechanical rewrite with no behavior change, not a warning-noise fix. Deliberately deferred rather than done piecemeal; revisit as a dedicated batch conversion if ever prioritized.", Scope = "namespaceanddescendants", Target = "Walkabout.Tests")]
