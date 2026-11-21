using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace SempaPumpStation.Controls
{
    /// <summary>
    /// Editable DataGrid cells for touch and mouse: one click on an editable cell starts editing with the whole value
    /// selected, so typing replaces it; a combo box column opens its list (as <see cref="TagTextBox"/> and Terakki's EnterKeyUpdateTextBox do). Read-only
    /// cells and columns behave as before. Set on the "Dg" style, so every editable table has it.
    /// </summary>
    public static class DataGridEdit
    {
        public static readonly DependencyProperty SelectAllOnEditProperty =
            DependencyProperty.RegisterAttached("SelectAllOnEdit", typeof(bool), typeof(DataGridEdit),
                new PropertyMetadata(false, OnChanged));

        public static bool GetSelectAllOnEdit(DependencyObject d) => (bool)d.GetValue(SelectAllOnEditProperty);
        public static void SetSelectAllOnEdit(DependencyObject d, bool value) => d.SetValue(SelectAllOnEditProperty, value);

        private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (!(d is DataGrid grid)) return;
            grid.PreviewMouseLeftButtonDown -= OnMouseDown;
            grid.PreparingCellForEdit -= OnPreparingCellForEdit;
            grid.CellEditEnding -= OnCellEditEnding;
            if ((bool)e.NewValue)
            {
                grid.PreviewMouseLeftButtonDown += OnMouseDown;
                grid.PreparingCellForEdit += OnPreparingCellForEdit;
                grid.CellEditEnding += OnCellEditEnding;
            }
        }

        // The table writes a row's values only when the row is committed (Enter, or leaving the row). Commit it as soon as a
        // cell is, so every value is written the moment its cell is left
        private static void OnCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;
            var grid = (DataGrid)sender;
            grid.Dispatcher.BeginInvoke(DispatcherPriority.Input, (Action)(() => grid.CommitEdit(DataGridEditingUnit.Row, true)));
        }

        // A click on an editable cell that is not being edited yet: select it and start editing right away
        private static void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            var grid = (DataGrid)sender;
            var cell = Find<DataGridCell>(e.OriginalSource as DependencyObject);
            if (cell == null || cell.IsEditing || cell.IsReadOnly || grid.IsReadOnly || !grid.IsEnabled) return;

            cell.Focus();
            var row = Find<DataGridRow>(cell);
            if (row != null && !row.IsSelected) grid.SelectedItem = row.Item;
            grid.CurrentCell = new DataGridCellInfo(cell);
            if (grid.BeginEdit(e)) e.Handled = true;   // handled: the click does not drop a caret into the new text box
        }

        private static void OnPreparingCellForEdit(object sender, DataGridPreparingCellForEditEventArgs e)
        {
            var grid = (DataGrid)sender;
            // A choice (combo box column): its list opens right away, and the choice is taken when it closes
            if (e.EditingElement is ComboBox combo)
            {
                // Taken as soon as a new item is picked; closing the list without a pick just ends the edit
                EventHandler closed = null;
                SelectionChangedEventHandler picked = null;
                void Done()
                {
                    combo.DropDownClosed -= closed;
                    combo.SelectionChanged -= picked;
                    // After the combo box has finished its own selection handling
                    grid.CommitEdit(DataGridEditingUnit.Cell, true);   // OnCellEditEnding then commits the row, which writes the value
                }
                closed = (s, a) => Done();
                picked = (s, a) => Done();
                combo.DropDownClosed += closed;
                combo.SelectionChanged += picked;
                combo.Dispatcher.BeginInvoke(DispatcherPriority.Input, (Action)(() =>
                {
                    combo.Focus();
                    combo.IsDropDownOpen = true;
                }));
                return;
            }
            if (!(e.EditingElement is TextBox box)) return;
            // After the grid has finished moving focus into the box
            box.Dispatcher.BeginInvoke(DispatcherPriority.Input, (Action)(() =>
            {
                box.Focus();
                box.SelectAll();
            }));
        }

        private static T Find<T>(DependencyObject d) where T : DependencyObject
        {
            while (d != null && !(d is T))
                d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            return d as T;
        }
    }
}
