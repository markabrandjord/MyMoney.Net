using System;
using NUnit.Framework;
using Walkabout.Data;

namespace Walkabout.TestSupport
{
    public abstract partial class DatabaseContractTests
    {
        private static MyMoney BuildOneRentBuildingMoney(out RentBuilding building)
        {
            MyMoney money = new MyMoney();
            building = new RentBuilding(money.Buildings) { Name = "123 Main St" };
            money.Buildings.AddRentBuilding(building);
            return money;
        }

        [Test]
        public void SaveOne_NewRentBuilding_PersistsAndSetsRowVersionToOne()
        {
            BuildOneRentBuildingMoney(out RentBuilding building);

            this.Database.SaveOne(building);

            Assert.That(building.RowVersion, Is.EqualTo(1));
            Assert.That(building.IsInserted, Is.False);
            Assert.That(building.IsChanged, Is.False);

            MyMoney reloaded = this.Database.Load(null);
            RentBuilding found = reloaded.Buildings.FindByName("123 Main St");
            Assert.That(found, Is.Not.Null);
            Assert.That(found.RowVersion, Is.EqualTo(1));
        }

        [Test]
        public void SaveOne_UpdateRentBuildingAfterReload_IncrementsRowVersion()
        {
            BuildOneRentBuildingMoney(out RentBuilding building);
            this.Database.SaveOne(building);

            MyMoney reloaded = this.Database.Load(null);
            RentBuilding found = reloaded.Buildings.FindByName("123 Main St");
            found.Address = "Updated Address";
            this.Database.SaveOne(found);

            Assert.That(found.RowVersion, Is.EqualTo(2));

            MyMoney reloadedAgain = this.Database.Load(null);
            RentBuilding foundAgain = reloadedAgain.Buildings.FindByName("123 Main St");
            Assert.That(foundAgain.Address, Is.EqualTo("Updated Address"));
            Assert.That(foundAgain.RowVersion, Is.EqualTo(2));
        }

        [Test]
        public void SaveOne_StaleRentBuildingRowVersion_ThrowsConcurrencyConflictException()
        {
            BuildOneRentBuildingMoney(out RentBuilding building);
            this.Database.SaveOne(building);

            MyMoney readerA = this.Database.Load(null);
            MyMoney readerB = this.Database.Load(null);

            RentBuilding buildingA = readerA.Buildings.FindByName("123 Main St");
            buildingA.Address = "From A";
            this.Database.SaveOne(buildingA);

            RentBuilding buildingB = readerB.Buildings.FindByName("123 Main St");
            buildingB.Address = "From B";
            var ex = Assert.Throws<ConcurrencyConflictException>(() => this.Database.SaveOne(buildingB));
            Assert.That(ex.StoredRowVersion, Is.EqualTo(2));
            Assert.That(ex.CallerRowVersion, Is.EqualTo(1));

            MyMoney reloaded = this.Database.Load(null);
            Assert.That(reloaded.Buildings.FindByName("123 Main St").Address, Is.EqualTo("From A"));
        }

        [Test]
        public void SaveOne_DeleteRentBuilding_RemovesRowFromDatabaseAndContainer()
        {
            BuildOneRentBuildingMoney(out RentBuilding building);
            this.Database.SaveOne(building);

            MyMoney reloaded = this.Database.Load(null);
            RentBuilding toDelete = reloaded.Buildings.FindByName("123 Main St");
            reloaded.Buildings.RemoveBuilding(toDelete);
            Assert.That(toDelete.IsDeleted, Is.True);

            // RentBuildings.Contains checks the underlying dictionary directly (keyed by
            // GetUniqueKey(), i.e. the Id) with no separate name-based cache to worry about, unlike
            // Payee/Security/Currency - so it's a genuine proof either before or after SaveOne.
            Assert.That(reloaded.Buildings.Contains(toDelete), Is.True,
                "sanity check: the building must still be in the underlying dictionary before SaveOne runs its postCommit RemoveChild");

            this.Database.SaveOne(toDelete);

            Assert.That(reloaded.Buildings.Contains(toDelete), Is.False);

            MyMoney reloadedAgain = this.Database.Load(null);
            Assert.That(reloadedAgain.Buildings.FindByName("123 Main St"), Is.Null);
        }

        [Test]
        public void SaveOne_RentBuildingWithNewUnit_PersistsUnitAndSetsItClean()
        {
            BuildOneRentBuildingMoney(out RentBuilding building);
            this.Database.SaveOne(building);

            RentUnit unit = new RentUnit(((RentBuildings)building.Parent).Units) { Building = building.Id, Name = "Unit A", Renter = "Alice" };
            ((RentBuildings)building.Parent).Units.AddRentUnit(unit);

            this.Database.SaveOne(building);

            Assert.That(unit.IsInserted, Is.False);
            Assert.That(unit.IsChanged, Is.False);

            MyMoney reloaded = this.Database.Load(null);
            RentBuilding foundBuilding = reloaded.Buildings.FindByName("123 Main St");
            RentUnit foundUnit = reloaded.Buildings.Units.Get(unit.Id);
            Assert.That(foundUnit, Is.Not.Null);
            Assert.That(foundUnit.Name, Is.EqualTo("Unit A"));
            Assert.That(foundUnit.Renter, Is.EqualTo("Alice"));
            Assert.That(foundBuilding.Units, Has.One.Matches<RentUnit>(u => u.Id == foundUnit.Id));
        }

        [Test]
        public void SaveOne_RentBuildingWithUnitOnlyEdit_PersistsUnitEvenThoughBuildingItselfIsUnchanged()
        {
            BuildOneRentBuildingMoney(out RentBuilding building);
            this.Database.SaveOne(building);

            RentUnit unit = new RentUnit(((RentBuildings)building.Parent).Units) { Building = building.Id, Name = "Unit A" };
            ((RentBuildings)building.Parent).Units.AddRentUnit(unit);
            this.Database.SaveOne(building);

            MyMoney reloaded = this.Database.Load(null);
            RentBuilding reloadedBuilding = reloaded.Buildings.FindByName("123 Main St");
            RentUnit reloadedUnit = reloaded.Buildings.Units.Get(unit.Id);
            reloadedUnit.Renter = "Bob";
            Assert.That(reloadedBuilding.IsChanged, Is.False,
                "precondition: only the unit changed, not the building itself - this is what exercises " +
                "the 'process units regardless of the building's own change state' path.");

            this.Database.SaveOne(reloadedBuilding);

            MyMoney reloadedAgain = this.Database.Load(null);
            Assert.That(reloadedAgain.Buildings.Units.Get(unit.Id).Renter, Is.EqualTo("Bob"));
        }

        [Test]
        public void SaveOne_RentBuildingWithDeletedUnit_RemovesUnitRow()
        {
            BuildOneRentBuildingMoney(out RentBuilding building);
            this.Database.SaveOne(building);

            RentUnit unit = new RentUnit(((RentBuildings)building.Parent).Units) { Building = building.Id, Name = "Unit A" };
            ((RentBuildings)building.Parent).Units.AddRentUnit(unit);
            this.Database.SaveOne(building);
            int unitId = unit.Id;

            MyMoney reloaded = this.Database.Load(null);
            RentBuilding reloadedBuilding = reloaded.Buildings.FindByName("123 Main St");
            RentUnit toDelete = reloaded.Buildings.Units.Get(unitId);
            reloaded.Buildings.Units.RemoveRentUnit(toDelete);
            Assert.That(toDelete.IsDeleted, Is.True);
            Assert.That(reloaded.Buildings.Units.Contains(toDelete), Is.True,
                "sanity check: RemoveRentUnit(forceRemoveAfterSave: false) must not have removed it from the dictionary yet");

            this.Database.SaveOne(reloadedBuilding);

            Assert.That(reloaded.Buildings.Units.Contains(toDelete), Is.False);

            MyMoney reloadedAgain = this.Database.Load(null);
            Assert.That(reloadedAgain.Buildings.Units.Get(unitId), Is.Null);
        }
    }
}
