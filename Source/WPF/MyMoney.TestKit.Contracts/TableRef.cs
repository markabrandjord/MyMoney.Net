using System;
using System.Collections.Generic;
using Walkabout.Data;

namespace MyMoney.TestKit.Contracts
{
    /// <summary>
    /// A table identifier that cannot be mistyped, cannot be a view, and cannot be a table that
    /// does not exist - spec section 2.6.6. Instances come from Of&lt;TRoot&gt;() or the named
    /// statics; there is no public constructor.
    ///
    /// A sealed class rather than the spec's readonly record struct: a record struct always has a
    /// public parameterless constructor that cannot be removed, so default(TableRef) would be a
    /// TableRef with a null name - precisely the hole "no public constructor" was buying.
    ///
    /// Note the one-character distance from IMoneyStore.DeleteRoot: this type's DeleteRow is raw,
    /// unchecked, below-the-version-check row removal. They live in different assemblies behind
    /// different interfaces on purpose - spec section 2.6.3.
    /// </summary>
    public sealed class TableRef : IEquatable<TableRef>
    {
        private TableRef(string name)
        {
            this.Name = name;
        }

        public string Name { get; }

        public static TableRef Accounts { get; } = new TableRef("Accounts");
        public static TableRef Categories { get; } = new TableRef("Categories");
        public static TableRef OnlineAccounts { get; } = new TableRef("OnlineAccounts");

        /// <summary>
        /// Every TableRef this build exposes. Task 19's anti-drift contract test asserts this is
        /// exactly the set of data tables the introspected schema contains at the current version,
        /// so adding a table without a TableRef - or leaving one behind after dropping a table -
        /// goes red.
        /// </summary>
        public static IReadOnlyList<TableRef> All { get; } = new[] { Accounts, Categories, OnlineAccounts };

        private static readonly Dictionary<Type, TableRef> ByRootType = new Dictionary<Type, TableRef>
        {
            { typeof(Account), Accounts },
            { typeof(Category), Categories },
            { typeof(OnlineAccount), OnlineAccounts },
        };

        public static TableRef Of<TRoot>() where TRoot : IAggregateRoot
        {
            if (ByRootType.TryGetValue(typeof(TRoot), out TableRef table))
            {
                return table;
            }

            throw new ArgumentException(
                $"No table is mapped for root type {typeof(TRoot).Name}. Add it to TableRef when "
                + "the slice creating its table lands - the anti-drift contract test will already "
                + "be red if the table exists and the TableRef does not.");
        }

        public bool Equals(TableRef other) =>
            other != null && string.Equals(this.Name, other.Name, StringComparison.Ordinal);

        public override bool Equals(object obj) => this.Equals(obj as TableRef);

        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(this.Name);

        public override string ToString() => this.Name;

        public static bool operator ==(TableRef left, TableRef right) => Equals(left, right);

        public static bool operator !=(TableRef left, TableRef right) => !Equals(left, right);
    }
}
