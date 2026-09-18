using System.Linq;
using System.Reflection;
using NUnit.Framework;
using NUnit.Framework.Legacy;

namespace Walkabout.Tests
{
    [TestFixture]
    public class LayerBoundaryTests
    {
        // MyMoney.Business and MyMoney.Data both forbid all three WPF-family
        // assemblies. MyMoney.Business's former WindowsBase dependency (the
        // domain model's event-marshaling backbone had a load-bearing
        // dependency on System.Windows.Threading.Dispatcher/DependencyObject)
        // was removed by the issue #7 rewrite - see
        // docs/superpowers/specs/2026-09-17-uidispatcher-portability-rewrite-design.md.
        // UiDispatcher now wraps System.Threading.SynchronizationContext, and
        // EventHandlerCollection has no UI-framework awareness of any kind.
        private static readonly string[] AllWpfAssemblyNames =
        {
            "PresentationFramework", "PresentationCore", "WindowsBase"
        };

        [Test]
        public void MyMoneyBusiness_HasNoWpfAssemblyReference()
        {
            var assembly = typeof(Walkabout.Data.MyMoney).Assembly;
            var referenced = assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();
            CollectionAssert.IsEmpty(
                referenced.Where(n => AllWpfAssemblyNames.Contains(n)).ToList(),
                $"MyMoney.Business referenced a WPF assembly: {string.Join(", ", referenced)}");
        }

        [Test]
        public void MyMoneyData_HasNoWpfAssemblyReference()
        {
            var assembly = typeof(Walkabout.Data.SqliteDatabase).Assembly;
            var referenced = assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();
            CollectionAssert.IsEmpty(
                referenced.Where(n => AllWpfAssemblyNames.Contains(n)).ToList(),
                $"MyMoney.Data referenced a WPF assembly: {string.Join(", ", referenced)}");
        }

        [Test]
        public void MyMoneyBusiness_IsADistinctAssemblyFromMyMoneyData()
        {
            var businessAssembly = typeof(Walkabout.Data.MyMoney).Assembly;
            var dataAssembly = typeof(Walkabout.Data.SqliteDatabase).Assembly;
            Assert.AreNotEqual(businessAssembly.GetName().Name, dataAssembly.GetName().Name);
            Assert.AreEqual("MyMoney.Business", businessAssembly.GetName().Name);
            Assert.AreEqual("MyMoney.Data", dataAssembly.GetName().Name);
        }
    }
}
