using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace SolidDNA
{
    /// <summary>
    /// Shared Cabin Tools checkbox behaviour for table-based tools.
    ///
    /// Behaviour:
    /// 1. When multiple (but not all) rows are selected and any selected row is unchecked,
    ///    the first press checks only those selected rows.
    /// 2. If those selected rows are already checked while other rows remain unchecked,
    ///    the next press checks the whole table.
    /// 3. If the whole table is checked, the next press unchecks the whole table.
    ///
    /// If there is no deliberate multi-row selection, the button behaves as a normal
    /// check-all / uncheck-all toggle.
    /// </summary>
    internal static class GridCheckBehavior
    {
        public static void ToggleSelectedThenAll(
            DataGridView grid,
            Func<DataGridViewRow, bool> isChecked,
            Action<DataGridViewRow, bool> setChecked)
        {
            if (grid == null || isChecked == null || setChecked == null)
                return;

            List<DataGridViewRow> allRows = grid.Rows
                .Cast<DataGridViewRow>()
                .Where(row => row != null && !row.IsNewRow)
                .ToList();

            if (allRows.Count == 0)
                return;

            List<DataGridViewRow> selectedRows = GetSelectedRows(grid)
                .Where(row => row != null && !row.IsNewRow)
                .Where(allRows.Contains)
                .Distinct()
                .OrderBy(row => row.Index)
                .ToList();

            bool deliberateSubsetSelection =
                selectedRows.Count > 1 && selectedRows.Count < allRows.Count;

            bool allRowsChecked = allRows.All(isChecked);

            if (deliberateSubsetSelection)
            {
                bool selectedRowsChecked = selectedRows.All(isChecked);

                if (!selectedRowsChecked)
                {
                    foreach (DataGridViewRow row in selectedRows)
                        setChecked(row, true);
                    return;
                }

                if (!allRowsChecked)
                {
                    foreach (DataGridViewRow row in allRows)
                        setChecked(row, true);
                    return;
                }

                foreach (DataGridViewRow row in allRows)
                    setChecked(row, false);
                return;
            }

            bool newValue = !allRowsChecked;
            foreach (DataGridViewRow row in allRows)
                setChecked(row, newValue);
        }

        public static List<DataGridViewRow> GetSelectedRows(DataGridView grid)
        {
            if (grid == null)
                return new List<DataGridViewRow>();

            HashSet<DataGridViewRow> rows = new HashSet<DataGridViewRow>();

            foreach (DataGridViewRow row in grid.SelectedRows)
            {
                if (row != null && !row.IsNewRow)
                    rows.Add(row);
            }

            foreach (DataGridViewCell cell in grid.SelectedCells)
            {
                if (cell != null && cell.RowIndex >= 0 && cell.RowIndex < grid.Rows.Count)
                {
                    DataGridViewRow row = grid.Rows[cell.RowIndex];
                    if (row != null && !row.IsNewRow)
                        rows.Add(row);
                }
            }

            return rows.OrderBy(row => row.Index).ToList();
        }
    }
}
