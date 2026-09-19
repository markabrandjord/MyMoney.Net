using System;
using NUnit.Framework;
using Walkabout.Data;

namespace Walkabout.Tests
{
    [TestFixture]
    public class CategoriesTests
    {
        [Test]
        public void GetOrCreateCategory_ColonSeparatedName_CreatesParentAndChild()
        {
            var money = new MyMoney();

            Category child = money.Categories.GetOrCreateCategory("Food:Groceries", CategoryType.Expense);

            // Category.Name stores the full colon-separated path, not the leaf segment
            // (Category.Label derives the leaf name from it) - see CLAUDE.md.
            Assert.That(child.Name, Is.EqualTo("Food:Groceries"));
            Assert.That(child.Label, Is.EqualTo("Groceries"));
            Assert.That(child.ParentCategory, Is.Not.Null);
            Assert.That(child.ParentCategory.Name, Is.EqualTo("Food"));
        }

        [Test]
        public void GetOrCreateCategory_CalledTwiceWithSameName_ReturnsSameInstance()
        {
            var money = new MyMoney();

            Category first = money.Categories.GetOrCreateCategory("Food:Groceries", CategoryType.Expense);
            int countAfterFirst = money.Categories.Count;
            Category second = money.Categories.GetOrCreateCategory("Food:Groceries", CategoryType.Expense);

            Assert.That(second, Is.SameAs(first), "Calling GetOrCreateCategory twice with the same name must not create a duplicate.");
            Assert.That(money.Categories.Count, Is.EqualTo(countAfterFirst));
        }

        [Test]
        public void OnDelete_CategoryWithNoTransactions_MarksDeletedAndDropsFromGetCategories()
        {
            var money = new MyMoney();
            Category unused = money.Categories.GetOrCreateCategory("Unused", CategoryType.Expense);
            int countBefore = money.Categories.Count;

            // Category.OnDelete() (base PersistentObject.OnDelete) only flips ChangeType to
            // Deleted - a soft delete for change-tracking/dirty-state purposes (see this repo's
            // architecture notes on PersistentContainer). It does NOT remove the category from
            // the raw Categories collection/Count; that only happens via Categories.RemoveCategory
            // (and even then, only immediately for a not-yet-saved/IsInserted category, otherwise
            // it's removed on the next save). Confirmed by reading both OnDelete() and
            // RemoveCategory() - real production code (CategoriesControl.xaml.cs's category-merge
            // handler) calls OnDelete() directly the same way this test does, for exactly this
            // soft-delete effect. GetCategories() is the collection's own "give me the live ones"
            // view - it explicitly filters out IsDeleted categories.
            unused.OnDelete();

            Assert.That(unused.IsDeleted, Is.True);
            Assert.That(money.Categories.Count, Is.EqualTo(countBefore), "Raw Count is unaffected by a soft delete.");
            Assert.That(money.Categories.GetCategories(), Does.Not.Contain(unused), "GetCategories() should filter out deleted categories.");
        }

        [Test]
        public void ReCategorize_MovesAllTransactionsFromOldToNewCategory()
        {
            var money = new MyMoney();
            var account = money.Accounts.AddAccount("Checking");
            Category oldCategory = money.Categories.GetOrCreateCategory("Fun:Videos", CategoryType.Expense);
            Category newCategory = money.Categories.GetOrCreateCategory("Fun:Movies", CategoryType.Expense);

            for (int i = 0; i < 2; i++)
            {
                // new Transaction(money.Transactions), not new Transaction() - the parameterless
                // ctor never gets a Parent, so t.MyMoney stays null and ReCategorize (which reads
                // this.MyMoney.Categories.ReParent(...)) would NRE. See CLAUDE.md.
                var t = new Transaction(money.Transactions);
                t.Account = account;
                t.Date = DateTime.Now;
                t.Amount = -10.00M - i;
                t.Category = oldCategory;
                money.Transactions.AddTransaction(t);
            }

            // Real production bulk-move pattern (CategoriesControl.xaml.cs's category-merge
            // handler): fetch every transaction on the category, then re-categorize each one -
            // there is no single "recategorize all" entry point, ReCategorize is per-Transaction.
            foreach (Transaction t in money.Transactions.GetTransactionsByCategory(oldCategory, null))
            {
                t.ReCategorize(oldCategory, newCategory);
            }

            int stillOnOldCategory = 0;
            int nowOnNewCategory = 0;
            foreach (Transaction t in money.Transactions)
            {
                if (t.Category == oldCategory) stillOnOldCategory++;
                if (t.Category == newCategory) nowOnNewCategory++;
            }

            Assert.That(stillOnOldCategory, Is.EqualTo(0), "No transaction should remain on the old category.");
            Assert.That(nowOnNewCategory, Is.EqualTo(2), "Both transactions should now be on the new category.");
        }

        [Test]
        public void OnDelete_CategoryStillHasTransactions_BusinessLayerDoesNotGuardAgainstOrphaning()
        {
            // CategoriesControl.Delete() (the real UI entry point) is the ONLY thing that guards
            // against this: it checks Transactions.GetTransactionsByCategory(c, null).Count > 0
            // and redirects to MergeCategoryDialog instead of calling OnDelete() when the
            // category is still in use. Category.OnDelete() itself has no such guard - confirmed
            // by reading it (it only cascades to subcategories and flips ChangeType). A caller
            // that bypasses the UI and calls OnDelete() directly on a category with transactions
            // gets no exception and no warning; the transaction is silently left pointing at a
            // now-IsDeleted category. This documents that reality so it isn't assumed to be safe.
            var money = new MyMoney();
            var account = money.Accounts.AddAccount("Checking");
            Category inUse = money.Categories.GetOrCreateCategory("Fun", CategoryType.Expense);

            var t = new Transaction(money.Transactions);
            t.Account = account;
            t.Date = DateTime.Now;
            t.Amount = -10.00M;
            t.Category = inUse;
            money.Transactions.AddTransaction(t);

            Assert.DoesNotThrow(() => inUse.OnDelete());

            Assert.That(inUse.IsDeleted, Is.True);
            Assert.That(t.Category, Is.SameAs(inUse), "Business layer does not clear or redirect the transaction's Category reference.");
            Assert.That(t.Category.IsDeleted, Is.True, "The transaction is left pointing at a deleted category - a real orphaning gap only the UI layer's Delete() guards against.");
        }

        [Test]
        public void RemoveCategory_ForceRemoveAfterSaveOnAlreadyPersistedCategory_RemovesImmediately()
        {
            // Categories.RemoveCategory's normal immediate-removal path only fires for a
            // not-yet-saved category (c.IsInserted). forceRemoveAfterSave is the override used
            // during a real Save() cycle to physically purge a category that was soft-deleted
            // (OnDelete()) in an earlier, already-persisted session - simulate that by calling
            // OnUpdated() (flips ChangeType back to None, as a real load/save would) before
            // removing, so IsInserted is false and only the force flag lets removal through.
            var money = new MyMoney();
            Category category = money.Categories.GetOrCreateCategory("Persisted", CategoryType.Expense);
            category.OnUpdated();
            Assert.That(category.IsInserted, Is.False, "Category should no longer look newly-inserted after OnUpdated().");
            int countBefore = money.Categories.Count;

            bool removed = money.Categories.RemoveCategory(category, forceRemoveAfterSave: true);

            Assert.That(removed, Is.True);
            Assert.That(money.Categories.Count, Is.EqualTo(countBefore - 1), "forceRemoveAfterSave should remove it immediately even though it wasn't IsInserted.");
        }
    }
}
