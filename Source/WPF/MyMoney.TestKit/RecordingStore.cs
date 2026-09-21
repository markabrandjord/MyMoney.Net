using System;
using System.Collections.Generic;
using Walkabout.Data;

namespace MyMoney.TestKit
{
    /// <summary>
    /// Records every operation so a business test can assert WHAT WAS ASKED of the data layer -
    /// explicitly shape (b) of the business-layer testing responsibility, which is currently done
    /// by inference. Spec section 2.4.
    ///
    /// It implements IMoneyStore DIRECTLY and forwards each of the four public methods, so the
    /// precondition fires in the inner store and the recording happens at the PUBLIC level, where
    /// "SaveRoot(Account#3)" is a more useful record than "WriteRoots([...])". MoneyStoreBase's
    /// protected WriteRoots being unreachable from a decorator is the correct shape, not a
    /// constraint to work around - spec section 1.6c, Developer note 3.
    /// </summary>
    public sealed class RecordingStore : IMoneyStore
    {
        private readonly IMoneyStore inner;
        private readonly List<string> calls = new List<string>();

        public RecordingStore(IMoneyStore inner)
        {
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public IReadOnlyList<string> Calls => this.calls;

        /// <summary>How many calls actually reached the inner store (a refused call is not one).</summary>
        public int WriteCount { get; private set; }

        public StoreIdentity Identity => this.inner.Identity;

        public void SaveRoot<TRoot>(TRoot root) where TRoot : PersistentObject, IAggregateRoot
        {
            this.inner.SaveRoot(root);
            this.Record("SaveRoot", root);
        }

        public void DeleteRoot<TRoot>(TRoot root) where TRoot : PersistentObject, IAggregateRoot
        {
            this.inner.DeleteRoot(root);
            this.Record("DeleteRoot", root);
        }

        public void SaveRoots(IReadOnlyList<IAggregateRoot> roots)
        {
            this.inner.SaveRoots(roots);
            var names = new List<string>(roots.Count);
            foreach (IAggregateRoot root in roots)
            {
                names.Add(Describe(root));
            }

            this.calls.Add($"SaveRoots([{string.Join(", ", names)}])");
            this.WriteCount++;
        }

        public void SaveTransfer(Transaction from, Transaction to)
        {
            this.inner.SaveTransfer(from, to);
            this.calls.Add($"SaveTransfer({Describe(from)}, {Describe(to)})");
            this.WriteCount++;
        }

        public IReadOnlyList<Account> LoadAccounts()
        {
            this.calls.Add("LoadAccounts()");
            return this.inner.LoadAccounts();
        }

        public void Dispose() => this.inner.Dispose();

        private void Record(string method, IAggregateRoot root)
        {
            this.calls.Add($"{method}({Describe(root)})");
            this.WriteCount++;
        }

        private static string Describe(IAggregateRoot root) => $"{root.GetType().Name}#{root.Id}";
    }
}
