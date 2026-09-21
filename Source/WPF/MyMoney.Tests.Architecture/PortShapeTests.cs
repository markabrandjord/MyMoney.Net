using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Walkabout.Data;

namespace Walkabout.Tests.Architecture
{
    /// <summary>
    /// Tier 0. Turns "the business layer cannot write view-backed data" and "a delete is
    /// nameable" from sentences in a design document into properties of the compiled assemblies.
    /// Spec sections 2.5, 2.7.2, 1.6c.
    /// </summary>
    [TestFixture]
    public class PortShapeTests
    {
        /// <summary>
        /// A "write member" is a public method on IMoneyStore taking at least one parameter that
        /// is an aggregate root, a generic parameter constrained to one, or a read-only list of
        /// them. Defined structurally rather than by name prefix so a method cannot dodge one of
        /// these two tests by being named to look like it belongs to the other. Identity (a
        /// property) and LoadAccounts (no parameters) are excluded by construction - spec 1.9.2
        /// relies on exactly that.
        /// </summary>
        private static bool IsRootShaped(Type t)
        {
            if (typeof(IAggregateRoot).IsAssignableFrom(t))
            {
                return true;
            }

            if (t.IsGenericParameter)
            {
                return t.GetGenericParameterConstraints().Any(c => typeof(IAggregateRoot).IsAssignableFrom(c));
            }

            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
            {
                return typeof(IAggregateRoot).IsAssignableFrom(t.GetGenericArguments()[0]);
            }

            return false;
        }

        private static IReadOnlyList<MethodInfo> WriteMembers()
        {
            return typeof(IMoneyStore)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetParameters().Any(p => IsRootShaped(p.ParameterType)))
                .ToList();
        }

        [Test]
        public void StoreWriteSurface_IsExactlyTheFourNamedMethods()
        {
            var names = WriteMembers().Select(m => m.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();

            Assert.That(
                names,
                Is.EqualTo(new[] { "DeleteRoot", "SaveRoot", "SaveRoots", "SaveTransfer" }),
                "IMoneyStore's write surface changed. The benefit of naming the delete evaporates "
                + "the day someone re-adds a convenience Save<T> that does everything - spec 1.6c.");
        }

        [Test]
        public void StoreWriteMethods_AcceptOnlyAggregateRoots()
        {
            foreach (MethodInfo m in WriteMembers())
            {
                foreach (ParameterInfo p in m.GetParameters())
                {
                    Assert.That(
                        IsRootShaped(p.ParameterType),
                        Is.True,
                        $"IMoneyStore.{m.Name} takes '{p.Name}' of type {p.ParameterType.Name}, "
                        + "which is not an aggregate root. A write method that takes a DTO 'just "
                        + "for this one import path' is the hole this test keeps shut - spec 2.7.2.");
                }
            }
        }

        [Test]
        public void ProjectionTypes_DoNotImplementIAggregateRoot()
        {
            Assembly business = typeof(IMoneyStore).Assembly;
            var projections = business.GetTypes()
                .Where(t => typeof(IProjection).IsAssignableFrom(t) && !t.IsInterface)
                .ToList();

            Assert.That(projections, Is.Not.Empty, "No IProjection implementers - this would pass vacuously.");

            foreach (Type t in projections)
            {
                Assert.That(
                    typeof(IAggregateRoot).IsAssignableFrom(t),
                    Is.False,
                    $"{t.Name} is a projection and must not be an aggregate root, or store.SaveRoot(row) would compile.");

                Assert.That(
                    t.GetProperty("RowVersion", BindingFlags.Public | BindingFlags.Instance),
                    Is.Null,
                    $"{t.Name} exposes RowVersion. A projection carrying a version lets a stale "
                    + "version be smuggled from a report into a write - spec 2.7.2 point 3.");

                foreach (PropertyInfo p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    // Record positional members compile to init-only setters, which report
                    // CanWrite == true. An init-only setter carries the IsExternalInit modreq.
                    MethodInfo setter = p.SetMethod;
                    bool isInitOnly = setter != null
                        && setter.ReturnParameter.GetRequiredCustomModifiers()
                            .Any(x => x.FullName == "System.Runtime.CompilerServices.IsExternalInit");

                    Assert.That(
                        setter == null || isInitOnly,
                        Is.True,
                        $"{t.Name}.{p.Name} has a settable (not init-only) setter. Projections are read models.");
                }
            }
        }
    }
}
