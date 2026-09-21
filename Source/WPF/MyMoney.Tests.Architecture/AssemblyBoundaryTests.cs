using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using MyMoney.TestKit.Contracts;
using NUnit.Framework;
using Walkabout.Data;
using Walkabout.Data.Sqlite;
using Walkabout.Data.Sqlite.Provisioning;

namespace Walkabout.Tests.Architecture
{
    /// <summary>
    /// Tier 0, layer 3 of the four-layer enforcement in spec section 2.5. Layer 1 is "no
    /// ProjectReference" (necessary, insufficient, one merge away from untrue), layer 2 is the
    /// Directory.Build.targets failure this task also adds, layer 4 is "production cannot name the
    /// type". These run in under a second and on every build.
    /// </summary>
    [TestFixture]
    public class AssemblyBoundaryTests
    {
        private static readonly string[] WpfAssemblyNames =
        {
            "PresentationFramework", "PresentationCore", "WindowsBase"
        };

        private static readonly Assembly[] ProductionAssemblies =
        {
            typeof(IMoneyStore).Assembly,                    // MyMoney.Business
            typeof(SqliteMoneyStore).Assembly,               // MyMoney.Data.Sqlite
            typeof(SqliteMoneyStoreProvisioner).Assembly,    // MyMoney.Data.Sqlite.Provisioning
        };

        [Test]
        public void ProductionAssemblies_DoNotReferenceTestKit()
        {
            foreach (Assembly assembly in ProductionAssemblies)
            {
                var offending = assembly.GetReferencedAssemblies()
                    .Select(a => a.Name)
                    .Where(n => n.Contains("TestKit", StringComparison.Ordinal)
                                || n.Contains("TestTier", StringComparison.Ordinal))
                    .ToList();

                Assert.That(offending, Is.Empty,
                    $"{assembly.GetName().Name} references {string.Join(", ", offending)}.");
            }
        }

        /// <summary>
        /// Grants that already existed when this design landed, kept as a frozen baseline rather
        /// than quietly deleted.
        ///
        /// MyMoney.Business predates the tier split: MyMoney.Data reads the internal
        /// ColumnObjectMapping.ForeignKeyTable/KeyProperty, UnitTests exercises the same type, and
        /// MyMoney reaches internal StockQuotes/Importers/Ofx members that moved into
        /// MyMoney.Business with their call sites left behind (see its AssemblyInfo.cs, which
        /// records the reasoning for each). Every one of them is load-bearing for the shipped app
        /// today, and removing them is not this plan's work - the plan's own global constraint is
        /// that no existing C# file is edited.
        ///
        /// The spec's rule still bites where it was aimed: the THREE new assemblies must declare
        /// none at all, and a new grant on MyMoney.Business fails this test too, because the
        /// baseline is an exact set rather than a blanket exemption. It should shrink to empty
        /// when MyMoney.Data and UnitTests are retired.
        /// </summary>
        private static readonly IReadOnlyDictionary<string, string[]> PreExistingInternalsVisibleTo =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["MyMoney.Business"] = new[] { "MyMoney.Data", "UnitTests", "MyMoney" },
            };

        [Test]
        public void ProductionAssemblies_DeclareNoInternalsVisibleTo()
        {
            // Spec section 6.2 calls this the most likely crack: it is the standard .NET answer to
            // "the test needs to reach this", and it dissolves the whole tier split in one line.
            foreach (Assembly assembly in ProductionAssemblies)
            {
                string name = assembly.GetName().Name;
                if (!PreExistingInternalsVisibleTo.TryGetValue(name, out string[] baseline))
                {
                    baseline = Array.Empty<string>();
                }

                Assert.That(
                    assembly.GetCustomAttributes<InternalsVisibleToAttribute>().Select(a => a.AssemblyName),
                    Is.EquivalentTo(baseline),
                    $"{name}'s InternalsVisibleTo grants are not the frozen baseline. If a test "
                    + "genuinely needs to reach something, the tier boundary was drawn in the wrong "
                    + "place - move the boundary, do not punch through it.");
            }
        }

        [Test]
        public void SqliteStoreAssembly_ContainsNoTestControlImplementation()
        {
            Assembly store = typeof(SqliteMoneyStore).Assembly;

            Assert.That(
                store.GetTypes().Where(t => typeof(IMoneyStoreTestControl).IsAssignableFrom(t)),
                Is.Empty);
        }

        [Test]
        public void SqliteStoreAssembly_DoesNotReferenceTheProvisioningAssembly()
        {
            // The spec's section 1 Recommendation rules out both a shared Common assembly and
            // InternalsVisibleTo and accepts ~40 lines of duplication instead. This is what makes
            // that an enforced decision rather than an intention.
            Assert.That(
                typeof(SqliteMoneyStore).Assembly.GetReferencedAssemblies().Select(a => a.Name),
                Has.None.Contains("Provisioning"));
        }

        [Test]
        public void SqliteStoreAssembly_ContainsNoDestructiveSql()
        {
            // Spec section 1, "How the SQLite facade gets its teeth": the shipped store assembly
            // contains no DROP TABLE, no VACUUM INTO, and no DELETE FROM without a WHERE Id=.
            // A crude but honest string scan; the SQL in this assembly is all literal, so a
            // literal scan is the right shape.
            //
            // The ENCODING is load-bearing, and getting it wrong makes this test vacuous rather
            // than merely weak. A C# string literal lives in the assembly's #US heap as UTF-16,
            // so a UTF-8 decode of the file cannot contain "DROP TABLE" no matter what the
            // assembly says - every assertion below would pass unconditionally and the DELETE
            // loop would iterate zero times. The #US heap is also not 2-byte aligned against the
            // start of the file, so the UTF-16 decode has to be tried from BOTH byte offsets.
            // UTF-8 is kept as well, for any ASCII embedded resource.
            byte[] bytes = File.ReadAllBytes(typeof(SqliteMoneyStore).Assembly.Location);
            int deleteStatementsExamined = 0;

            foreach (string text in DecodedForms(bytes))
            {
                foreach (string forbidden in new[] { "DROP TABLE", "DROP VIEW", "VACUUM", "TRUNCATE" })
                {
                    Assert.That(text, Does.Not.Contain(forbidden),
                        $"MyMoney.Data.Sqlite.dll contains the string '{forbidden}'. The destructive code "
                        + "must not be in a release build's bits at all - that is the whole guarantee.");
                }

                foreach (string deleteStatement in Occurrences(text, "DELETE FROM"))
                {
                    deleteStatementsExamined++;
                    Assert.That(deleteStatement, Does.Contain("WHERE Id="),
                        "A DELETE without a WHERE Id= clause in the store assembly: " + deleteStatement);
                }
            }

            // Positive control. The store has exactly one DELETE literal, so finding none means
            // the scan itself stopped working - which is precisely how this test was vacuous
            // before. Without this line a broken scan reports a pass.
            Assert.That(deleteStatementsExamined, Is.GreaterThan(0),
                "The scan found no DELETE statement at all in MyMoney.Data.Sqlite.dll. The store's "
                + "version-checked DELETE is still there, so the scan is broken, not the assembly.");
        }

        [Test]
        public void ProductionReferenceClosure_ContainsNoSqlServerEngineAssembly()
        {
            // See the plan's D-4 for why this is a reference-closure walk rather than an assertion
            // over a dotnet publish directory. In Plan A no MyMoney.Data.SqlServer* assembly
            // exists, so this cannot fail today - it is built now as the tripwire Plan B's slice 8
            // has to satisfy, and it says so rather than implying it proves something.
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var queue = new Queue<Assembly>(ProductionAssemblies);

            while (queue.Count > 0)
            {
                Assembly current = queue.Dequeue();
                if (!seen.Add(current.GetName().Name))
                {
                    continue;
                }

                foreach (AssemblyName reference in current.GetReferencedAssemblies())
                {
                    Assert.That(
                        reference.Name.StartsWith("MyMoney.Data.SqlServer", StringComparison.Ordinal),
                        Is.False,
                        $"{current.GetName().Name} pulls in {reference.Name}. The released assembly "
                        + "set must not contain the SQL Server engine at all.");

                    if (reference.Name.StartsWith("MyMoney", StringComparison.Ordinal)
                        && !seen.Contains(reference.Name))
                    {
                        queue.Enqueue(Assembly.Load(reference));
                    }
                }
            }
        }

        [Test]
        public void MyMoneyBusiness_HasNoWpfAssemblyReference()
        {
            AssertNoWpf(typeof(IMoneyStore).Assembly);
        }

        [TestCase("MyMoney.Data.Sqlite")]
        [TestCase("MyMoney.Data.Sqlite.Provisioning")]
        public void MyMoneyData_HasNoWpfAssemblyReference(string assemblyName)
        {
            AssertNoWpf(ProductionAssemblies.Single(a => a.GetName().Name == assemblyName));
        }

        [Test]
        public void TestsBusiness_DoesNotReferenceTheWpfUiProject()
        {
            // Fixes spec section 2.1 item 2: UnitTests.csproj references MyMoney.csproj today, so
            // "business test" is currently an honor system. The new business tier is not.
            Assembly tests = Assembly.Load("MyMoney.Tests.Business");

            Assert.That(
                tests.GetReferencedAssemblies().Select(a => a.Name),
                Has.None.EqualTo("MyMoney"),
                "MyMoney.Tests.Business referenced the WPF UI project.");
        }

        private static void AssertNoWpf(Assembly assembly)
        {
            Assert.That(
                assembly.GetReferencedAssemblies().Select(a => a.Name).Where(n => WpfAssemblyNames.Contains(n)),
                Is.Empty,
                $"{assembly.GetName().Name} referenced a WPF assembly.");
        }

        /// <summary>
        /// The assembly's bytes read as text, in every encoding a string literal can be hiding in:
        /// UTF-8 (embedded ASCII resources) and UTF-16 from each of the two byte alignments (the
        /// #US heap, where every C# string literal actually lives).
        /// </summary>
        private static IEnumerable<string> DecodedForms(byte[] bytes)
        {
            yield return Encoding.UTF8.GetString(bytes);
            yield return DecodeUtf16(bytes, 0);
            yield return DecodeUtf16(bytes, 1);
        }

        private static string DecodeUtf16(byte[] bytes, int offset) =>
            Encoding.Unicode.GetString(bytes, offset, ((bytes.Length - offset) / 2) * 2);

        private static IEnumerable<string> Occurrences(string text, string needle)
        {
            int index = text.IndexOf(needle, StringComparison.Ordinal);
            while (index >= 0)
            {
                yield return text.Substring(index, Math.Min(160, text.Length - index));
                index = text.IndexOf(needle, index + needle.Length, StringComparison.Ordinal);
            }
        }
    }
}
