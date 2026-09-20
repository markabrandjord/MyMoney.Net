using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using NUnit.Framework;
using Walkabout.Dialogs;

namespace Walkabout.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public class WpfUiGridEditSpikeTests
    {
        [Test]
        public void EnteringEditModeOnTemplateColumn_SwapsInThemedComboBox_AndItRendersWithNonZeroSize()
        {
            var window = new WpfUiGridEditSpike();
            window.Show();
            window.UpdateLayout();

            DataGrid grid = (DataGrid)window.FindName("SpikeGrid");
            Assert.That(grid, Is.Not.Null, "Expected to find SpikeGrid via FindName.");
            grid.UpdateLayout();

            // Select and begin editing the first row's single template column.
            grid.SelectedIndex = 0;
            grid.CurrentCell = new DataGridCellInfo(grid.Items[0], grid.Columns[0]);
            bool enteredEditMode = grid.BeginEdit();
            grid.UpdateLayout();

            Assert.That(enteredEditMode, Is.True, "DataGrid.BeginEdit() should succeed on the template column.");

            DataGridCell cell = FindCell(grid, 0, 0);
            Assert.That(cell, Is.Not.Null, "Expected to find the DataGridCell for row 0, column 0.");

            ComboBox editCombo = FindVisualChild<ComboBox>(cell);
            Assert.That(editCombo, Is.Not.Null,
                "Expected the CellEditingTemplate's ComboBox to be present in the visual tree after BeginEdit().");
            Assert.That(editCombo.ActualWidth, Is.GreaterThan(0),
                "The themed ComboBox should have a real rendered width, not collapse to zero (the 'big white box'/broken-template failure mode).");
            Assert.That(editCombo.ActualHeight, Is.GreaterThan(0),
                "The themed ComboBox should have a real rendered height.");

            window.Close();
        }

        private static DataGridCell FindCell(DataGrid grid, int row, int column)
        {
            DataGridRow dataGridRow = (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(row);
            if (dataGridRow == null)
            {
                return null;
            }
            DataGridCellsPresenter presenter = FindVisualChild<DataGridCellsPresenter>(dataGridRow);
            return (DataGridCell)presenter?.ItemContainerGenerator.ContainerFromIndex(column);
        }

        private static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null)
            {
                return null;
            }
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typed)
                {
                    return typed;
                }
                T found = FindVisualChild<T>(child);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }
    }
}
