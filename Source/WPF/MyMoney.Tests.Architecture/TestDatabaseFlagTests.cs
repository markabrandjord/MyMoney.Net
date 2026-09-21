using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using NUnit.Framework;
using Walkabout.Data;

namespace Walkabout.Tests.Architecture
{
    /// <summary>
    /// Tier 0, and heavier than the GetReferencedAssemblies checks beside it - an IL scan, called
    /// out as such rather than glossed (spec section 1.9.7). It is worth the weight because the
    /// failure it prevents is the single way section 1.9's "it's the same mechanism as 1.8's, not
    /// a parallel one" claim stops being true: a second, hand-rolled if (entry.TestDatabase) check
    /// appearing somewhere and drifting from the shared one.
    /// </summary>
    [TestFixture]
    public class TestDatabaseFlagTests
    {
        /// <summary>
        /// Types allowed to read StoreIdentity.IsTestDatabase in a production assembly. The UI's
        /// CanExecute handler joins this list when the sample-data feature is scheduled; it is a
        /// read with no write behind it (greying a command), deliberate and not a loophole.
        /// </summary>
        private static readonly string[] AllowedReaders =
        {
            "TestDatabaseGuard",

            // StoreIdentity's OWN members. A positional record's compiler-generated PrintMembers
            // (and, on some compiler versions, Equals/GetHashCode) reads every property including
            // this one, purely to render or compare the record. A type reading its own property
            // cannot be the "second, hand-rolled check somewhere else" this test exists to catch,
            // and the alternative - giving StoreIdentity a hand-written ToString to dodge the scan
            // - would be contorting production code to satisfy a test.
            "StoreIdentity.",
        };

        private static readonly string[] ProductionAssemblies =
        {
            "MyMoney.Business", "MyMoney.Data.Sqlite", "MyMoney.Data.Sqlite.Provisioning"
        };

        [Test]
        public void TestDatabaseFlag_IsReadOnlyByTheSharedGuard()
        {
            foreach (string assemblyName in ProductionAssemblies)
            {
                string path = AssemblyPath(assemblyName);
                foreach (string caller in CallersOf(path, "StoreIdentity", "get_IsTestDatabase"))
                {
                    Assert.That(
                        AllowedReaders.Any(a => caller.Contains(a, StringComparison.Ordinal)),
                        Is.True,
                        $"{assemblyName}'s {caller} reads StoreIdentity.IsTestDatabase directly. Call "
                        + "TestDatabaseGuard.Require instead - a second, hand-rolled check is exactly "
                        + "what makes 'it's the same mechanism' stop being true.");
                }
            }
        }

        private static string AssemblyPath(string name) =>
            System.IO.Path.Combine(TestContext.CurrentContext.TestDirectory, name + ".dll");

        /// <summary>
        /// Names of methods whose IL contains a call to
        /// <paramref name="calleeType"/>.<paramref name="calleeName"/>. Uses
        /// System.Reflection.Metadata so no assembly has to be loaded for execution.
        ///
        /// The DECLARING TYPE is part of the match, not just the member name: SqliteStoreOptions
        /// also has an IsTestDatabase property, and a name-only match would report every store
        /// reading its own options record as if it were bypassing the guard.
        /// </summary>
        private static IEnumerable<string> CallersOf(string assemblyPath, string calleeType, string calleeName)
        {
            using var stream = System.IO.File.OpenRead(assemblyPath);
            using var pe = new PEReader(stream);
            MetadataReader md = pe.GetMetadataReader();

            foreach (MethodDefinitionHandle handle in md.MethodDefinitions)
            {
                MethodDefinition method = md.GetMethodDefinition(handle);
                if (method.RelativeVirtualAddress == 0)
                {
                    continue;
                }

                byte[] il = pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes();
                if (il == null)
                {
                    continue;
                }

                for (int i = 0; i + 4 < il.Length; i++)
                {
                    // 0x28 call, 0x6F callvirt - followed by a 4-byte metadata token.
                    if (il[i] != 0x28 && il[i] != 0x6F)
                    {
                        continue;
                    }

                    int token = BitConverter.ToInt32(il, i + 1);
                    string callee = ResolveMemberName(md, token);
                    if (callee != null
                        && string.Equals(callee, calleeType + "." + calleeName, StringComparison.Ordinal))
                    {
                        string declaring = md.GetString(md.GetTypeDefinition(method.GetDeclaringType()).Name);
                        yield return declaring + "." + md.GetString(method.Name);
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// "DeclaringType.MemberName" for a resolvable member token; null for anything else.
        ///
        /// This scan walks raw IL bytes looking for 0x28/0x6F, which means it also hits those
        /// values where they appear INSIDE another instruction's operand. Such a false hit yields
        /// four arbitrary bytes, and EntityHandle throws on a token whose table or row is not
        /// real - so the miss has to be absorbed here rather than crashing the scan. A garbage
        /// token that happens to resolve is still harmless: the caller only acts on it when the
        /// resolved NAME ends with the callee it is looking for.
        /// </summary>
        private static string ResolveMemberName(MetadataReader md, int token)
        {
            try
            {
                var handle = MetadataTokens.EntityHandle(token);
                switch (handle.Kind)
                {
                    case HandleKind.MemberReference:
                        MemberReference member = md.GetMemberReference((MemberReferenceHandle)handle);
                        string owner = TypeNameOf(md, member.Parent);
                        return owner == null ? null : owner + "." + md.GetString(member.Name);

                    case HandleKind.MethodDefinition:
                        MethodDefinition definition = md.GetMethodDefinition((MethodDefinitionHandle)handle);
                        return md.GetString(md.GetTypeDefinition(definition.GetDeclaringType()).Name)
                               + "." + md.GetString(definition.Name);

                    default:
                        return null;
                }
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (BadImageFormatException)
            {
                return null;
            }
        }

        /// <summary>The simple name of the type a MemberReference hangs off, or null.</summary>
        private static string TypeNameOf(MetadataReader md, EntityHandle parent) => parent.Kind switch
        {
            HandleKind.TypeReference => md.GetString(md.GetTypeReference((TypeReferenceHandle)parent).Name),
            HandleKind.TypeDefinition => md.GetString(md.GetTypeDefinition((TypeDefinitionHandle)parent).Name),
            _ => null
        };
    }
}
