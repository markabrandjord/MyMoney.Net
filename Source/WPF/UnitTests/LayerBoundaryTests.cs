using System.Linq;
using System.Reflection;
using NUnit.Framework;
using NUnit.Framework.Legacy;

namespace Walkabout.Tests
{
    [TestFixture]
    public class LayerBoundaryTests
    {
        // MyMoney.Business is allowed WindowsBase (the domain model's
        // event-marshaling backbone has a load-bearing dependency on
        // System.Windows.Threading.Dispatcher/DependencyObject -- Ruling,
        // 2026-09-15, see the plan's Global Constraints) but never the two
        // true UI-rendering assemblies. MyMoney.Data forbids all three --
        // nothing in it needs WindowsBase even transitively through its own
        // direct references (it only reaches MyMoney.Business's WindowsBase
        // dependency via the ProjectReference, which GetReferencedAssemblies
        // does not surface -- that method returns only an assembly's own
        // direct references).
        private static readonly string[] UiRenderingAssemblyNames =
        {
            "PresentationFramework", "PresentationCore"
        };

        private static readonly string[] AllWpfAssemblyNames =
        {
            "PresentationFramework", "PresentationCore", "WindowsBase"
        };

        [Test]
        public void MyMoneyBusiness_HasNoUiRenderingAssemblyReference()
        {
            var assembly = typeof(Walkabout.Data.MyMoney).Assembly;
            var referenced = assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();
            CollectionAssert.IsEmpty(
                referenced.Where(n => UiRenderingAssemblyNames.Contains(n)).ToList(),
                $"MyMoney.Business referenced a UI-rendering assembly: {string.Join(", ", referenced)}");
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
