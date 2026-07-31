using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using CADBooster.SolidDna;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

using SwEnvironment = CADBooster.SolidDna.SolidWorksEnvironment;

namespace SolidDNA
{
    /// <summary>
    /// General Assembly Configuration Manager.
    ///
    /// Purpose:
    /// - Scan every component instance exposed by the active assembly.
    /// - Read the available configurations from the referenced part/subassembly files.
    /// - Allow row-by-row and selected-row bulk component configuration assignment.
    /// - Apply the selected target configuration to active / checked / all assembly configurations.
    ///
    /// Important:
    /// - This edits IComponent2.ReferencedConfiguration directly.
    /// - It does not edit design tables. A design table can overwrite the direct result on rebuild.
    /// - The tool does not save the assembly automatically.
    /// </summary>
    internal static class AssemblyConfigurationManagerCommand
    {
        public static void ShowAssemblyConfigurationManagerForm()
        {
            ISldWorks swApp = null;
            try { swApp = SwEnvironment.Application.UnsafeObject as ISldWorks; } catch { swApp = null; }

            if (swApp == null)
            {
                MessageBox.Show("Could not connect to SOLIDWORKS.", "Cabin Tools - Assembly Configuration Manager", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            IModelDoc2 model = swApp.ActiveDoc as IModelDoc2;
            if (model == null || model.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
            {
                MessageBox.Show("Open an assembly before using the Assembly Configuration Manager.", "Cabin Tools - Assembly Configuration Manager", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using (AssemblyConfigurationManagerForm form = new AssemblyConfigurationManagerForm(swApp, model))
            {
                form.ShowDialog();
            }
        }

        private sealed class AssemblyConfigurationManagerForm : Form
        {
            private readonly ISldWorks swApp;
            private readonly IModelDoc2 assemblyModel;
            private readonly IAssemblyDoc assemblyDoc;
            private readonly List<ComponentConfigRow> rows = new List<ComponentConfigRow>();
            private readonly Stack<List<RowSnapshot>> undoStack = new Stack<List<RowSnapshot>>();
            private bool updatingGridInternally;

            private DataGridView grid;
            private Label statusLabel;
            private Button scanButton;
            private Button selectionButton;
            private Button checkAllButton;
            private Button setTargetButton;
            private Button applyButton;
            private Button undoButton;
            private ComboBox assemblyScopeBox;
            private CheckedListBox assemblyConfigList;

            private const int ApplyColumnIndex = 0;
            private const int InstanceColumnIndex = 1;
            private const int CurrentConfigColumnIndex = 2;
            private const int TargetConfigColumnIndex = 3;
            private const int AvailableConfigColumnIndex = 4;
            private const int StatusColumnIndex = 5;

            private enum RowScope
            {
                Cancel,
                SelectedRows,
                All
            }

            private enum AssemblyConfigScope
            {
                Active,
                Checked,
                All
            }

            public AssemblyConfigurationManagerForm(ISldWorks swApp, IModelDoc2 assemblyModel)
            {
                this.swApp = swApp;
                this.assemblyModel = assemblyModel;
                this.assemblyDoc = assemblyModel as IAssemblyDoc;

                Text = "Cabin Tools - Assembly Configuration Manager";
                Width = 1420;
                Height = 820;
                StartPosition = FormStartPosition.CenterScreen;
                Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

                BuildLayout();
                LoadAssemblyConfigurations();
                ScanComponents();
                CheckRowsFromSolidWorksSelection(false);
            }

            private void BuildLayout()
            {
                TableLayoutPanel root = new TableLayoutPanel();
                root.Dock = DockStyle.Fill;
                root.Padding = new Padding(14);
                root.ColumnCount = 1;
                root.RowCount = 5;
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

                Label heading = new Label();
                heading.Text = "Assembly Configuration Manager";
                heading.Font = new Font(Font.FontFamily, 12F, FontStyle.Bold, GraphicsUnit.Point);
                heading.AutoSize = true;
                heading.Margin = new Padding(0, 0, 0, 10);
                root.Controls.Add(heading, 0, 0);

                FlowLayoutPanel actions = new FlowLayoutPanel();
                actions.AutoSize = true;
                actions.Dock = DockStyle.Top;
                actions.WrapContents = true;
                actions.Margin = new Padding(0, 0, 0, 8);

                scanButton = CreateButton("Scan components", delegate { ScanComponents(); });
                selectionButton = CreateButton("Use SOLIDWORKS selection", delegate { CheckRowsFromSolidWorksSelection(true); });
                checkAllButton = CreateButton("Check / uncheck all", delegate { ToggleAllChecks(); });
                setTargetButton = CreateButton("Set target...", delegate { SetTargetForSelectedOrCheckedRows(); });
                undoButton = CreateButton("Undo last", delegate { UndoLast(); });
                applyButton = CreateButton("Apply", delegate { ApplyButton_Click(); });

                actions.Controls.Add(scanButton);
                actions.Controls.Add(selectionButton);
                actions.Controls.Add(checkAllButton);
                actions.Controls.Add(setTargetButton);
                actions.Controls.Add(undoButton);
                actions.Controls.Add(applyButton);
                root.Controls.Add(actions, 0, 1);

                TableLayoutPanel scopeLayout = new TableLayoutPanel();
                scopeLayout.Dock = DockStyle.Top;
                scopeLayout.AutoSize = true;
                scopeLayout.ColumnCount = 4;
                scopeLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                scopeLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
                scopeLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                scopeLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                scopeLayout.Margin = new Padding(0, 0, 0, 8);

                Label scopeLabel = new Label();
                scopeLabel.Text = "Apply in assembly configurations:";
                scopeLabel.AutoSize = true;
                scopeLabel.Anchor = AnchorStyles.Left;
                scopeLayout.Controls.Add(scopeLabel, 0, 0);

                assemblyScopeBox = new ComboBox();
                assemblyScopeBox.DropDownStyle = ComboBoxStyle.DropDownList;
                assemblyScopeBox.Items.AddRange(new object[] { "Active configuration only", "Checked configurations", "All configurations" });
                assemblyScopeBox.SelectedIndex = 0;
                assemblyScopeBox.Dock = DockStyle.Fill;
                scopeLayout.Controls.Add(assemblyScopeBox, 1, 0);

                Label checklistLabel = new Label();
                checklistLabel.Text = "Configuration checklist:";
                checklistLabel.AutoSize = true;
                checklistLabel.Anchor = AnchorStyles.Left;
                checklistLabel.Margin = new Padding(18, 0, 6, 0);
                scopeLayout.Controls.Add(checklistLabel, 2, 0);

                assemblyConfigList = new CheckedListBox();
                assemblyConfigList.Height = 68;
                assemblyConfigList.CheckOnClick = true;
                assemblyConfigList.MultiColumn = true;
                assemblyConfigList.Dock = DockStyle.Fill;
                scopeLayout.Controls.Add(assemblyConfigList, 3, 0);

                root.Controls.Add(scopeLayout, 0, 2);

                grid = new DataGridView();
                grid.Dock = DockStyle.Fill;
                grid.AllowUserToAddRows = false;
                grid.AllowUserToDeleteRows = false;
                grid.AllowUserToResizeRows = false;
                grid.MultiSelect = true;
                grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
                grid.RowHeadersVisible = false;
                grid.EditMode = DataGridViewEditMode.EditOnEnter;
                grid.AutoGenerateColumns = false;
                grid.CellValueChanged += Grid_CellValueChanged;
                grid.CurrentCellDirtyStateChanged += Grid_CurrentCellDirtyStateChanged;
                grid.CellFormatting += Grid_CellFormatting;
                grid.DataError += delegate { };

                DataGridViewCheckBoxColumn applyColumn = new DataGridViewCheckBoxColumn();
                applyColumn.HeaderText = "Apply";
                applyColumn.Width = 54;
                grid.Columns.Add(applyColumn);

                DataGridViewTextBoxColumn instanceColumn = new DataGridViewTextBoxColumn();
                instanceColumn.HeaderText = "Component";
                instanceColumn.ReadOnly = true;
                instanceColumn.Width = 220;
                grid.Columns.Add(instanceColumn);


                DataGridViewTextBoxColumn currentColumn = new DataGridViewTextBoxColumn();
                currentColumn.HeaderText = "Current configuration";
                currentColumn.ReadOnly = true;
                currentColumn.Width = 220;
                grid.Columns.Add(currentColumn);

                DataGridViewComboBoxColumn targetColumn = new DataGridViewComboBoxColumn();
                targetColumn.HeaderText = "Target configuration";
                targetColumn.Width = 260;
                targetColumn.FlatStyle = FlatStyle.Flat;
                grid.Columns.Add(targetColumn);

                DataGridViewTextBoxColumn availableColumn = new DataGridViewTextBoxColumn();
                availableColumn.HeaderText = "Available configurations";
                availableColumn.ReadOnly = true;
                availableColumn.Width = 250;
                grid.Columns.Add(availableColumn);

                DataGridViewTextBoxColumn statusColumn = new DataGridViewTextBoxColumn();
                statusColumn.HeaderText = "Status";
                statusColumn.ReadOnly = true;
                statusColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
                grid.Columns.Add(statusColumn);

                root.Controls.Add(grid, 0, 3);

                statusLabel = new Label();
                statusLabel.AutoSize = false;
                statusLabel.Dock = DockStyle.Fill;
                statusLabel.Height = 54;
                statusLabel.Padding = new Padding(4, 8, 4, 0);
                statusLabel.Font = new Font(Font.FontFamily, 11F, FontStyle.Bold, GraphicsUnit.Point);
                statusLabel.TextAlign = ContentAlignment.MiddleLeft;
                root.Controls.Add(statusLabel, 0, 4);

                Controls.Add(root);
            }

            private Button CreateButton(string text, EventHandler clickHandler)
            {
                Button button = new Button();
                button.AutoSize = true;
                button.MinimumSize = new Size(126, 30);
                button.Text = text;
                button.Margin = new Padding(0, 0, 10, 6);
                button.Click += clickHandler;
                return button;
            }

            private void LoadAssemblyConfigurations()
            {
                assemblyConfigList.Items.Clear();
                List<string> names = GetConfigurationNames(assemblyModel);
                string active = GetActiveAssemblyConfigurationName();
                foreach (string name in names)
                {
                    bool check = string.Equals(name, active, StringComparison.OrdinalIgnoreCase);
                    assemblyConfigList.Items.Add(name, check);
                }
            }

            private void ScanComponents()
            {
                CaptureUndo("Before scan");
                rows.Clear();
                grid.Rows.Clear();

                if (assemblyDoc == null)
                {
                    SetStatus("No active assembly document found.");
                    return;
                }

                object componentsObject = null;
                try { componentsObject = assemblyDoc.GetComponents(true); } catch { componentsObject = null; }

                object[] componentObjects = componentsObject as object[];
                if (componentObjects == null)
                {
                    SetStatus("Scanned 0 component instance(s).");
                    return;
                }

                foreach (object componentObject in componentObjects)
                {
                    IComponent2 component = componentObject as IComponent2;
                    if (component == null)
                        continue;

                    ComponentConfigRow row = BuildRow(component);
                    rows.Add(row);
                }

                rows.Sort(CompareRowsForGrouping);

                updatingGridInternally = true;
                try
                {
                    foreach (ComponentConfigRow row in rows)
                        AddGridRow(row);
                }
                finally
                {
                    updatingGridInternally = false;
                }

                SetStatus("Scanned " + rows.Count.ToString() + " top-level component(s). Component names match the SOLIDWORKS FeatureManager style: component name, component description, and configuration name.");
            }

            private static int CompareRowsForGrouping(ComponentConfigRow a, ComponentConfigRow b)
            {
                if (a == null && b == null)
                    return 0;
                if (a == null)
                    return -1;
                if (b == null)
                    return 1;

                int byInstance = string.Compare(a.InstanceName, b.InstanceName, StringComparison.OrdinalIgnoreCase);
                if (byInstance != 0)
                    return byInstance;

                return string.Compare(a.CurrentConfiguration, b.CurrentConfiguration, StringComparison.OrdinalIgnoreCase);
            }

            private ComponentConfigRow BuildRow(IComponent2 component)
            {
                ComponentConfigRow row = new ComponentConfigRow();
                row.Component = component;
                row.InstanceName = SafeComponentName(component);
                row.Path = SafeComponentPath(component);
                row.FileName = string.IsNullOrWhiteSpace(row.Path) ? row.InstanceName : Path.GetFileName(row.Path);
                row.ComponentKey = row.InstanceName + "|" + row.Path;
                row.CurrentConfiguration = SafeReferencedConfiguration(component);
                row.TargetConfiguration = row.CurrentConfiguration;
                row.IsSuppressed = IsComponentSuppressed(component);

                ComponentDocumentInfo info = ReadComponentDocumentInfo(component, row.CurrentConfiguration);
                row.ConfigurationNames = info.ConfigurationNames;
                row.Description = info.ComponentDescription;
                if (string.IsNullOrWhiteSpace(row.Description))
                    row.Description = row.FileName;

                if (row.IsSuppressed)
                {
                    row.Status = "Suppressed";
                }
                else if (row.ConfigurationNames.Count == 0)
                {
                    row.Status = "No configurations found. Resolve/open the component and rescan.";
                }
                else
                {
                    row.Status = "Ready";
                }

                return row;
            }

            private void AddGridRow(ComponentConfigRow row)
            {
                int rowIndex = grid.Rows.Add();
                DataGridViewRow gridRow = grid.Rows[rowIndex];
                gridRow.Tag = row;

                gridRow.Cells[ApplyColumnIndex].Value = row.Apply;
                gridRow.Cells[InstanceColumnIndex].Value = BuildFeatureTreeLikeComponentName(row);
                gridRow.Cells[CurrentConfigColumnIndex].Value = row.CurrentConfiguration;
                gridRow.Cells[AvailableConfigColumnIndex].Value = BuildAvailableSummary(row.ConfigurationNames);
                gridRow.Cells[StatusColumnIndex].Value = row.Status;
                gridRow.Cells[InstanceColumnIndex].ToolTipText = BuildComponentTooltip(row);
                gridRow.Cells[AvailableConfigColumnIndex].ToolTipText = string.Join(", ", row.ConfigurationNames.ToArray());

                DataGridViewComboBoxCell targetCell = new DataGridViewComboBoxCell();
                foreach (string configName in row.ConfigurationNames)
                    targetCell.Items.Add(configName);

                if (!string.IsNullOrWhiteSpace(row.TargetConfiguration) && !targetCell.Items.Contains(row.TargetConfiguration))
                    targetCell.Items.Add(row.TargetConfiguration);

                targetCell.Value = row.TargetConfiguration;
                gridRow.Cells[TargetConfigColumnIndex] = targetCell;
            }

            private static string BuildComponentTooltip(ComponentConfigRow row)
            {
                if (row == null)
                    return string.Empty;

                List<string> lines = new List<string>();
                if (!string.IsNullOrWhiteSpace(row.InstanceName))
                    lines.Add("Component name: " + row.InstanceName);
                if (!string.IsNullOrWhiteSpace(row.Description))
                    lines.Add("Component description: " + row.Description);
                if (!string.IsNullOrWhiteSpace(row.FileName))
                    lines.Add("File: " + row.FileName);
                if (!string.IsNullOrWhiteSpace(row.Path))
                    lines.Add(row.Path);

                return string.Join("\r\n", lines.ToArray());
            }

            private static string BuildFeatureTreeLikeComponentName(ComponentConfigRow row)
            {
                if (row == null)
                    return string.Empty;

                string primary = string.IsNullOrWhiteSpace(row.InstanceName) ? "<component>" : row.InstanceName.Trim();
                string description = string.IsNullOrWhiteSpace(row.Description) ? string.Empty : row.Description.Trim();
                string configuration = string.IsNullOrWhiteSpace(row.CurrentConfiguration) ? string.Empty : row.CurrentConfiguration.Trim();

                if (!string.IsNullOrWhiteSpace(description) && !string.IsNullOrWhiteSpace(configuration))
                    return primary + " ('" + description + "' " + configuration + ")";

                if (!string.IsNullOrWhiteSpace(description))
                    return primary + " ('" + description + "')";

                if (!string.IsNullOrWhiteSpace(configuration))
                    return primary + " (" + configuration + ")";

                return primary;
            }

            private static string BuildAvailableSummary(List<string> names)
            {
                if (names == null || names.Count == 0)
                    return string.Empty;

                int take = Math.Min(3, names.Count);
                List<string> visible = new List<string>();
                for (int i = 0; i < take; i++)
                    visible.Add(names[i]);

                string text = string.Join(", ", visible.ToArray());
                if (names.Count > 3)
                    text += ", ...";
                return text;
            }

            private void CheckRowsFromSolidWorksSelection(bool showMessage)
            {
                List<IComponent2> selectedComponents = GetSelectedComponents(assemblyModel);
                if (selectedComponents.Count == 0)
                {
                    if (showMessage)
                        MessageBox.Show("Select one or more components in the SOLIDWORKS assembly first.", "Cabin Tools - Assembly Configuration Manager", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                CaptureUndo("Before SOLIDWORKS selection");

                int matched = 0;
                updatingGridInternally = true;
                try
                {
                    foreach (DataGridViewRow gridRow in grid.Rows)
                    {
                        ComponentConfigRow row = gridRow.Tag as ComponentConfigRow;
                        if (row == null)
                            continue;

                        if (ContainsMatchingComponent(selectedComponents, row.Component))
                        {
                            row.Apply = true;
                            gridRow.Cells[ApplyColumnIndex].Value = true;
                            matched++;
                        }
                    }
                }
                finally
                {
                    updatingGridInternally = false;
                }

                SetStatus("Checked " + matched.ToString() + " selected component row(s) from SOLIDWORKS selection.");
            }

            private void ToggleAllChecks()
            {
                List<DataGridViewRow> targetGridRows = GetSelectedGridRows();
                bool usingSelection = targetGridRows.Count > 1;

                if (!usingSelection)
                {
                    targetGridRows = new List<DataGridViewRow>();
                    foreach (DataGridViewRow gridRow in grid.Rows)
                    {
                        if (!gridRow.IsNewRow && gridRow.Tag is ComponentConfigRow)
                            targetGridRows.Add(gridRow);
                    }
                }

                if (targetGridRows.Count == 0)
                    return;

                CaptureUndo("Before check toggle");

                bool allTargetRowsChecked = true;
                foreach (DataGridViewRow gridRow in targetGridRows)
                {
                    ComponentConfigRow row = gridRow.Tag as ComponentConfigRow;
                    if (row == null || !row.Apply)
                    {
                        allTargetRowsChecked = false;
                        break;
                    }
                }

                bool newValue = !allTargetRowsChecked;
                updatingGridInternally = true;
                try
                {
                    foreach (DataGridViewRow gridRow in targetGridRows)
                    {
                        ComponentConfigRow row = gridRow.Tag as ComponentConfigRow;
                        if (row == null)
                            continue;

                        row.Apply = newValue;
                        gridRow.Cells[ApplyColumnIndex].Value = newValue;
                    }
                }
                finally
                {
                    updatingGridInternally = false;
                }

                string scopeText = usingSelection ? "selected row(s)" : "all rows";
                SetStatus((newValue ? "Checked " : "Unchecked ") + targetGridRows.Count.ToString() + " " + scopeText + ".");
            }

            private List<DataGridViewRow> GetSelectedGridRows()
            {
                List<DataGridViewRow> selectedRows = new List<DataGridViewRow>();

                if (grid == null)
                    return selectedRows;

                foreach (DataGridViewRow row in grid.SelectedRows)
                {
                    if (row == null || row.IsNewRow || selectedRows.Contains(row))
                        continue;
                    selectedRows.Add(row);
                }

                foreach (DataGridViewCell cell in grid.SelectedCells)
                {
                    if (cell == null || cell.RowIndex < 0 || cell.RowIndex >= grid.Rows.Count)
                        continue;

                    DataGridViewRow row = grid.Rows[cell.RowIndex];
                    if (row == null || row.IsNewRow || selectedRows.Contains(row))
                        continue;
                    selectedRows.Add(row);
                }

                return selectedRows;
            }

            private void SetTargetForSelectedOrCheckedRows()
            {
                List<ComponentConfigRow> targetRows = GetRowsForBulkTarget();
                if (targetRows.Count == 0)
                {
                    MessageBox.Show("Check one or more component rows, or select one or more rows in the table first.", "Cabin Tools - Assembly Configuration Manager", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                List<string> options = BuildCommonOrUnionConfigurationList(targetRows);
                if (options.Count == 0)
                {
                    MessageBox.Show("No configuration list is available for the selected rows. Resolve/open the components and rescan.", "Cabin Tools - Assembly Configuration Manager", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                string selectedTarget = PromptForConfiguration(options);
                if (string.IsNullOrWhiteSpace(selectedTarget))
                    return;

                CaptureUndo("Before set target");
                ApplyTargetToRows(targetRows, selectedTarget);
                SetStatus("Set target configuration for selected compatible rows. Click Apply to write the change to the assembly.");
            }

            private void ApplyTargetToRows(List<ComponentConfigRow> targetRows, string selectedTarget)
            {
                int changed = 0;
                updatingGridInternally = true;
                try
                {
                    foreach (DataGridViewRow gridRow in grid.Rows)
                    {
                        ComponentConfigRow row = gridRow.Tag as ComponentConfigRow;
                        if (row == null || !targetRows.Contains(row))
                            continue;

                        if (!ConfigurationExists(row, selectedTarget))
                        {
                            row.Status = "Target not available for this component";
                            gridRow.Cells[StatusColumnIndex].Value = row.Status;
                            continue;
                        }

                        row.TargetConfiguration = selectedTarget;
                        row.Apply = true;
                        gridRow.Cells[ApplyColumnIndex].Value = true;
                        gridRow.Cells[TargetConfigColumnIndex].Value = selectedTarget;
                        gridRow.Cells[StatusColumnIndex].Value = row.Status;
                        changed++;
                    }
                }
                finally
                {
                    updatingGridInternally = false;
                }

                grid.Refresh();
                SetStatus("Target set for " + changed.ToString() + " row(s).");
            }

            private List<ComponentConfigRow> GetRowsForBulkTarget()
            {
                List<ComponentConfigRow> result = new List<ComponentConfigRow>();

                foreach (DataGridViewRow gridRow in grid.SelectedRows)
                {
                    ComponentConfigRow row = gridRow.Tag as ComponentConfigRow;
                    if (row != null && !result.Contains(row))
                        result.Add(row);
                }

                if (result.Count > 0)
                    return result;

                foreach (ComponentConfigRow row in rows)
                {
                    if (row.Apply)
                        result.Add(row);
                }

                return result;
            }

            private void PropagateEditedTarget(ComponentConfigRow sourceRow, string selectedTarget, int editedRowIndex)
            {
                if (sourceRow == null || string.IsNullOrWhiteSpace(selectedTarget))
                    return;

                List<ComponentConfigRow> targets = new List<ComponentConfigRow>();

                if (grid.SelectedRows.Count > 1)
                {
                    foreach (DataGridViewRow selectedGridRow in grid.SelectedRows)
                    {
                        ComponentConfigRow row = selectedGridRow.Tag as ComponentConfigRow;
                        if (row != null && !targets.Contains(row))
                            targets.Add(row);
                    }
                }
                else
                {
                    foreach (ComponentConfigRow row in rows)
                    {
                        if (row.Apply && !targets.Contains(row))
                            targets.Add(row);
                    }
                }

                if (targets.Count <= 1)
                    return;

                CaptureUndo("Before target propagation");
                ApplyTargetToRows(targets, selectedTarget);
            }

            private static List<string> BuildCommonOrUnionConfigurationList(List<ComponentConfigRow> targetRows)
            {
                List<string> common = new List<string>();
                List<string> union = new List<string>();

                if (targetRows == null || targetRows.Count == 0)
                    return common;

                foreach (ComponentConfigRow row in targetRows)
                {
                    foreach (string name in row.ConfigurationNames)
                        AddUnique(union, name);
                }

                if (targetRows.Count == 1)
                    return union;

                foreach (string candidate in union)
                {
                    bool existsInAll = true;
                    foreach (ComponentConfigRow row in targetRows)
                    {
                        if (!ConfigurationExists(row, candidate))
                        {
                            existsInAll = false;
                            break;
                        }
                    }

                    if (existsInAll)
                        common.Add(candidate);
                }

                return common.Count > 0 ? common : union;
            }

            private string PromptForConfiguration(List<string> configurationNames)
            {
                using (Form dialog = new Form())
                {
                    dialog.Text = "Cabin Tools - Set Target Configuration";
                    dialog.StartPosition = FormStartPosition.CenterParent;
                    dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                    dialog.MinimizeBox = false;
                    dialog.MaximizeBox = false;
                    dialog.ShowInTaskbar = false;
                    dialog.ClientSize = new Size(430, 150);

                    Label label = new Label();
                    label.Left = 12;
                    label.Top = 16;
                    label.Width = 405;
                    label.Height = 30;
                    label.Text = "Target configuration";
                    label.TextAlign = ContentAlignment.MiddleCenter;
                    label.Font = new Font(dialog.Font, FontStyle.Bold);
                    dialog.Controls.Add(label);

                    ComboBox combo = new ComboBox();
                    combo.Left = 28;
                    combo.Top = 58;
                    combo.Width = 370;
                    combo.DropDownStyle = ComboBoxStyle.DropDownList;
                    foreach (string name in configurationNames)
                        combo.Items.Add(name);
                    if (combo.Items.Count > 0)
                        combo.SelectedIndex = 0;
                    dialog.Controls.Add(combo);

                    Button okButton = new Button();
                    okButton.Text = "OK";
                    okButton.Left = 228;
                    okButton.Top = 104;
                    okButton.Width = 80;
                    okButton.DialogResult = DialogResult.OK;
                    dialog.Controls.Add(okButton);

                    Button cancelButton = new Button();
                    cancelButton.Text = "Cancel";
                    cancelButton.Left = 318;
                    cancelButton.Top = 104;
                    cancelButton.Width = 80;
                    cancelButton.DialogResult = DialogResult.Cancel;
                    dialog.Controls.Add(cancelButton);

                    dialog.AcceptButton = okButton;
                    dialog.CancelButton = cancelButton;

                    return dialog.ShowDialog(this) == DialogResult.OK ? Convert.ToString(combo.SelectedItem) : string.Empty;
                }
            }

            private void ApplyButton_Click()
            {
                RowScope rowScope = ResolveRowScope("Apply configuration changes to");
                if (rowScope == RowScope.Cancel)
                    return;

                string writeBlockReason = CabinCustomPropertyStore.GetWriteBlockReason(assemblyModel);
                if (!string.IsNullOrWhiteSpace(writeBlockReason))
                {
                    MessageBox.Show(writeBlockReason, "Cabin Tools - Assembly Configuration Manager", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                List<string> targetAssemblyConfigurations = GetTargetAssemblyConfigurations();
                if (targetAssemblyConfigurations.Count == 0)
                {
                    MessageBox.Show("No assembly configurations are selected for applying changes.", "Cabin Tools - Assembly Configuration Manager", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                CaptureUndo("Before apply");

                string originalAssemblyConfiguration = GetActiveAssemblyConfigurationName();
                int updated = 0;
                int skipped = 0;
                int failed = 0;

                foreach (string assemblyConfiguration in targetAssemblyConfigurations)
                {
                    if (!ActivateAssemblyConfiguration(assemblyConfiguration))
                    {
                        failed += CountRowsInScope(rowScope);
                        continue;
                    }

                    Dictionary<string, IComponent2> liveComponents = BuildLiveComponentMap();

                    foreach (DataGridViewRow gridRow in grid.Rows)
                    {
                        ComponentConfigRow row = gridRow.Tag as ComponentConfigRow;
                        if (row == null)
                            continue;

                        if (rowScope == RowScope.SelectedRows && !row.Apply)
                            continue;

                        object value = gridRow.Cells[TargetConfigColumnIndex].Value;
                        row.TargetConfiguration = Convert.ToString(value);

                        IComponent2 liveComponent = null;
                        liveComponents.TryGetValue(row.ComponentKey, out liveComponent);
                        if (liveComponent == null)
                        {
                            row.Status = "Skipped - not found in " + assemblyConfiguration;
                            skipped++;
                            continue;
                        }

                        if (IsComponentSuppressed(liveComponent))
                        {
                            row.Status = "Skipped - suppressed in " + assemblyConfiguration;
                            skipped++;
                        }
                        else if (string.IsNullOrWhiteSpace(row.TargetConfiguration))
                        {
                            row.Status = "Skipped - no target configuration";
                            skipped++;
                        }
                        else if (!ConfigurationExists(row, row.TargetConfiguration))
                        {
                            row.Status = "Failed - target configuration does not exist";
                            failed++;
                        }
                        else if (string.Equals(SafeReferencedConfiguration(liveComponent), row.TargetConfiguration, StringComparison.OrdinalIgnoreCase))
                        {
                            row.Status = "Unchanged";
                            skipped++;
                        }
                        else if (SetReferencedConfiguration(liveComponent, row.TargetConfiguration))
                        {
                            row.CurrentConfiguration = row.TargetConfiguration;
                            row.Status = "Updated in " + assemblyConfiguration;
                            updated++;
                        }
                        else
                        {
                            row.Status = "Failed - SOLIDWORKS rejected configuration change";
                            failed++;
                        }
                    }

                    try { assemblyModel.ForceRebuild3(false); } catch { }
                }

                if (!string.IsNullOrWhiteSpace(originalAssemblyConfiguration))
                    ActivateAssemblyConfiguration(originalAssemblyConfiguration);

                RefreshGridAfterApply();
                SetStatus("Updated " + updated.ToString() + ", skipped " + skipped.ToString() + ", failed " + failed.ToString() + ". Original assembly configuration restored. Save manually after checking.");
                grid.Refresh();
            }

            private void UndoLast()
            {
                if (undoStack.Count == 0)
                {
                    SetStatus("Nothing to undo.");
                    return;
                }

                List<RowSnapshot> snapshots = undoStack.Pop();
                updatingGridInternally = true;
                try
                {
                    foreach (RowSnapshot snapshot in snapshots)
                    {
                        ComponentConfigRow row = FindRowByKey(snapshot.ComponentKey);
                        if (row == null)
                            continue;

                        row.Apply = snapshot.Apply;
                        row.CurrentConfiguration = snapshot.CurrentConfiguration;
                        row.TargetConfiguration = snapshot.TargetConfiguration;
                        row.Status = snapshot.Status;

                        DataGridViewRow gridRow = FindGridRow(row);
                        if (gridRow != null)
                        {
                            gridRow.Cells[ApplyColumnIndex].Value = row.Apply;
                            gridRow.Cells[CurrentConfigColumnIndex].Value = row.CurrentConfiguration;
                            if (gridRow.Cells[TargetConfigColumnIndex] is DataGridViewComboBoxCell && !string.IsNullOrWhiteSpace(row.TargetConfiguration))
                            {
                                DataGridViewComboBoxCell cell = (DataGridViewComboBoxCell)gridRow.Cells[TargetConfigColumnIndex];
                                if (!cell.Items.Contains(row.TargetConfiguration))
                                    cell.Items.Add(row.TargetConfiguration);
                            }
                            gridRow.Cells[TargetConfigColumnIndex].Value = row.TargetConfiguration;
                            gridRow.Cells[StatusColumnIndex].Value = row.Status;
                        }
                    }
                }
                finally
                {
                    updatingGridInternally = false;
                }

                SetStatus("Restored previous target selections. If changes were already applied to SOLIDWORKS, apply the restored targets to write them back.");
                grid.Refresh();
            }

            private void CaptureUndo(string reason)
            {
                if (rows.Count == 0)
                    return;

                List<RowSnapshot> snapshots = new List<RowSnapshot>();
                foreach (ComponentConfigRow row in rows)
                {
                    snapshots.Add(new RowSnapshot
                    {
                        ComponentKey = row.ComponentKey,
                        Apply = row.Apply,
                        CurrentConfiguration = row.CurrentConfiguration,
                        TargetConfiguration = row.TargetConfiguration,
                        Status = row.Status
                    });
                }

                undoStack.Push(snapshots);
                while (undoStack.Count > 20)
                {
                    // Stack has no trim method. Rebuild it and drop the oldest item.
                    List<List<RowSnapshot>> temp = new List<List<RowSnapshot>>(undoStack.ToArray());
                    temp.RemoveAt(temp.Count - 1);
                    undoStack.Clear();
                    for (int i = temp.Count - 1; i >= 0; i--)
                        undoStack.Push(temp[i]);
                }
            }

            private ComponentConfigRow FindRowByKey(string key)
            {
                foreach (ComponentConfigRow row in rows)
                {
                    if (string.Equals(row.ComponentKey, key, StringComparison.OrdinalIgnoreCase))
                        return row;
                }
                return null;
            }

            private DataGridViewRow FindGridRow(ComponentConfigRow row)
            {
                foreach (DataGridViewRow gridRow in grid.Rows)
                {
                    if (object.ReferenceEquals(gridRow.Tag, row))
                        return gridRow;
                }
                return null;
            }

            private void RefreshGridAfterApply()
            {
                foreach (DataGridViewRow gridRow in grid.Rows)
                {
                    ComponentConfigRow row = gridRow.Tag as ComponentConfigRow;
                    if (row == null)
                        continue;

                    gridRow.Cells[CurrentConfigColumnIndex].Value = row.CurrentConfiguration;
                    gridRow.Cells[StatusColumnIndex].Value = row.Status;
                }
            }

            private int CountRowsInScope(RowScope rowScope)
            {
                int count = 0;
                foreach (ComponentConfigRow row in rows)
                {
                    if (rowScope == RowScope.All || row.Apply)
                        count++;
                }
                return count;
            }

            private Dictionary<string, IComponent2> BuildLiveComponentMap()
            {
                Dictionary<string, IComponent2> map = new Dictionary<string, IComponent2>(StringComparer.OrdinalIgnoreCase);
                if (assemblyDoc == null)
                    return map;

                object componentsObject = null;
                try { componentsObject = assemblyDoc.GetComponents(true); } catch { componentsObject = null; }
                object[] componentObjects = componentsObject as object[];
                if (componentObjects == null)
                    return map;

                foreach (object componentObject in componentObjects)
                {
                    IComponent2 component = componentObject as IComponent2;
                    if (component == null)
                        continue;

                    string key = SafeComponentName(component) + "|" + SafeComponentPath(component);
                    if (!map.ContainsKey(key))
                        map.Add(key, component);
                }

                return map;
            }

            private List<string> GetTargetAssemblyConfigurations()
            {
                AssemblyConfigScope scope = GetAssemblyConfigScope();
                List<string> result = new List<string>();

                if (scope == AssemblyConfigScope.Active)
                {
                    AddUnique(result, GetActiveAssemblyConfigurationName());
                    return result;
                }

                if (scope == AssemblyConfigScope.All)
                    return GetConfigurationNames(assemblyModel);

                foreach (object checkedItem in assemblyConfigList.CheckedItems)
                    AddUnique(result, Convert.ToString(checkedItem));

                return result;
            }

            private AssemblyConfigScope GetAssemblyConfigScope()
            {
                string text = Convert.ToString(assemblyScopeBox.SelectedItem);
                if (text.IndexOf("All", StringComparison.OrdinalIgnoreCase) >= 0)
                    return AssemblyConfigScope.All;
                if (text.IndexOf("Checked", StringComparison.OrdinalIgnoreCase) >= 0)
                    return AssemblyConfigScope.Checked;
                return AssemblyConfigScope.Active;
            }

            private string GetActiveAssemblyConfigurationName()
            {
                try
                {
                    ConfigurationManager manager = assemblyModel.ConfigurationManager as ConfigurationManager;
                    if (manager != null && manager.ActiveConfiguration != null)
                        return manager.ActiveConfiguration.Name;
                }
                catch
                {
                }

                return string.Empty;
            }

            private bool ActivateAssemblyConfiguration(string configurationName)
            {
                if (string.IsNullOrWhiteSpace(configurationName))
                    return false;

                try { return assemblyModel.ShowConfiguration2(configurationName); }
                catch { return false; }
            }

            private RowScope ResolveRowScope(string heading)
            {
                int total = rows.Count;
                if (total == 0)
                    return RowScope.Cancel;

                int checkedRows = CountCheckedRows();
                if (checkedRows == total)
                    return RowScope.SelectedRows;

                using (Form dialog = new Form())
                {
                    dialog.Text = "Cabin Tools";
                    dialog.StartPosition = FormStartPosition.CenterParent;
                    dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                    dialog.MinimizeBox = false;
                    dialog.MaximizeBox = false;
                    dialog.ShowInTaskbar = false;
                    dialog.ClientSize = new Size(390, 130);

                    Label label = new Label();
                    label.Left = 12;
                    label.Top = 16;
                    label.Width = 360;
                    label.Height = 34;
                    label.Text = heading;
                    label.TextAlign = ContentAlignment.MiddleCenter;
                    label.Font = new Font(dialog.Font, FontStyle.Bold);
                    dialog.Controls.Add(label);

                    RowScope choice = RowScope.Cancel;

                    Button allButton = new Button();
                    allButton.Text = "All";
                    allButton.Left = 58;
                    allButton.Top = 74;
                    allButton.Width = 92;
                    allButton.DialogResult = DialogResult.OK;
                    allButton.Click += delegate { choice = RowScope.All; };
                    dialog.Controls.Add(allButton);

                    Button selectedButton = new Button();
                    selectedButton.Text = "Selected Rows";
                    selectedButton.Left = 166;
                    selectedButton.Top = 74;
                    selectedButton.Width = 116;
                    selectedButton.Enabled = checkedRows > 0;
                    selectedButton.DialogResult = DialogResult.OK;
                    selectedButton.Click += delegate { choice = RowScope.SelectedRows; };
                    dialog.Controls.Add(selectedButton);

                    Button cancelButton = new Button();
                    cancelButton.Text = "Cancel";
                    cancelButton.Left = 298;
                    cancelButton.Top = 74;
                    cancelButton.Width = 82;
                    cancelButton.DialogResult = DialogResult.Cancel;
                    cancelButton.Click += delegate { choice = RowScope.Cancel; };
                    dialog.Controls.Add(cancelButton);

                    dialog.AcceptButton = checkedRows > 0 ? selectedButton : allButton;
                    dialog.CancelButton = cancelButton;
                    dialog.ShowDialog(this);
                    return choice;
                }
            }

            private int CountCheckedRows()
            {
                int checkedRows = 0;
                foreach (ComponentConfigRow row in rows)
                {
                    if (row.Apply)
                        checkedRows++;
                }
                return checkedRows;
            }

            private void Grid_CurrentCellDirtyStateChanged(object sender, EventArgs e)
            {
                if (grid.IsCurrentCellDirty)
                    grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }

            private void Grid_CellValueChanged(object sender, DataGridViewCellEventArgs e)
            {
                if (updatingGridInternally)
                    return;

                if (e.RowIndex < 0 || e.RowIndex >= grid.Rows.Count)
                    return;

                ComponentConfigRow row = grid.Rows[e.RowIndex].Tag as ComponentConfigRow;
                if (row == null)
                    return;

                if (e.ColumnIndex == ApplyColumnIndex)
                {
                    try { row.Apply = Convert.ToBoolean(grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value); } catch { row.Apply = false; }
                }
                else if (e.ColumnIndex == TargetConfigColumnIndex)
                {
                    string newTarget = Convert.ToString(grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value);
                    CaptureUndo("Before target edit");
                    row.TargetConfiguration = newTarget;
                    row.Apply = true;

                    updatingGridInternally = true;
                    try { grid.Rows[e.RowIndex].Cells[ApplyColumnIndex].Value = true; }
                    finally { updatingGridInternally = false; }

                    PropagateEditedTarget(row, newTarget, e.RowIndex);
                }
            }

            private void Grid_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
            {
                if (e.RowIndex < 0 || e.RowIndex >= grid.Rows.Count)
                    return;

                ComponentConfigRow row = grid.Rows[e.RowIndex].Tag as ComponentConfigRow;
                if (row == null)
                    return;

                if (row.Status.StartsWith("Failed", StringComparison.OrdinalIgnoreCase))
                {
                    grid.Rows[e.RowIndex].DefaultCellStyle.BackColor = Color.MistyRose;
                }
                else if (row.Status.StartsWith("Skipped", StringComparison.OrdinalIgnoreCase) || row.Status.StartsWith("No", StringComparison.OrdinalIgnoreCase))
                {
                    grid.Rows[e.RowIndex].DefaultCellStyle.BackColor = Color.LemonChiffon;
                }
                else
                {
                    grid.Rows[e.RowIndex].DefaultCellStyle.BackColor = GetGroupColor(row.FileName);
                }
            }

            private Color GetGroupColor(string fileName)
            {
                int groupIndex = 0;
                string previous = null;
                foreach (ComponentConfigRow row in rows)
                {
                    if (previous == null || !string.Equals(previous, row.FileName, StringComparison.OrdinalIgnoreCase))
                    {
                        groupIndex++;
                        previous = row.FileName;
                    }

                    if (string.Equals(fileName, row.FileName, StringComparison.OrdinalIgnoreCase))
                        break;
                }

                return groupIndex % 2 == 0 ? Color.White : Color.FromArgb(255, 253, 222);
            }

            private void SetStatus(string text)
            {
                if (statusLabel != null)
                    statusLabel.Text = text;
            }

            private ComponentDocumentInfo ReadComponentDocumentInfo(IComponent2 component, string referencedConfiguration)
            {
                ComponentDocumentInfo info = new ComponentDocumentInfo();
                if (component == null)
                    return info;

                IModelDoc2 model = null;
                bool openedTemporarily = false;
                string openedTitle = string.Empty;

                try { model = component.GetModelDoc2() as IModelDoc2; } catch { model = null; }

                if (model == null)
                {
                    TryResolveComponent(component);
                    try { model = component.GetModelDoc2() as IModelDoc2; } catch { model = null; }
                }

                if (model == null)
                {
                    string path = SafeComponentPath(component);
                    if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    {
                        int errors = 0;
                        int warnings = 0;
                        try
                        {
                            model = swApp.OpenDoc6(path, GetDocumentTypeFromPath(path), (int)swOpenDocOptions_e.swOpenDocOptions_Silent, string.Empty, ref errors, ref warnings) as IModelDoc2;
                            openedTemporarily = model != null;
                            if (model != null)
                                openedTitle = model.GetTitle();
                        }
                        catch
                        {
                            model = null;
                        }
                    }
                }

                if (model != null)
                {
                    info.ConfigurationNames = GetConfigurationNames(model);
                    info.ComponentDescription = GetBestComponentDescription(model, referencedConfiguration);
                }

                if (openedTemporarily && !string.IsNullOrWhiteSpace(openedTitle))
                {
                    try { swApp.CloseDoc(openedTitle); } catch { }
                }

                return info;
            }

            private static int GetDocumentTypeFromPath(string path)
            {
                string ext = Path.GetExtension(path) ?? string.Empty;
                if (ext.Equals(".sldasm", StringComparison.OrdinalIgnoreCase))
                    return (int)swDocumentTypes_e.swDocASSEMBLY;
                if (ext.Equals(".slddrw", StringComparison.OrdinalIgnoreCase))
                    return (int)swDocumentTypes_e.swDocDRAWING;
                return (int)swDocumentTypes_e.swDocPART;
            }

            private static void TryResolveComponent(IComponent2 component)
            {
                if (component == null)
                    return;

                try
                {
                    object rawComponent = component;
                    rawComponent.GetType().InvokeMember("SetSuppression2", BindingFlags.InvokeMethod, null, rawComponent, new object[] { (int)swComponentSuppressionState_e.swComponentResolved });
                }
                catch
                {
                }
            }

            private static string GetBestComponentDescription(IModelDoc2 model, string configurationName)
            {
                if (model == null)
                    return string.Empty;

                string value = ReadConfigurationDescription(model, configurationName);
                if (!string.IsNullOrWhiteSpace(value))
                    return value;

                value = ReadCustomProperty(model, configurationName, "Description");
                if (!string.IsNullOrWhiteSpace(value))
                    return value;

                value = ReadCustomProperty(model, configurationName, "DESCRIPTION");
                if (!string.IsNullOrWhiteSpace(value))
                    return value;

                value = ReadCustomProperty(model, string.Empty, "Description");
                if (!string.IsNullOrWhiteSpace(value))
                    return value;

                value = ReadCustomProperty(model, string.Empty, "DESCRIPTION");
                if (!string.IsNullOrWhiteSpace(value))
                    return value;

                return string.Empty;
            }

            private static string ReadConfigurationDescription(IModelDoc2 model, string configurationName)
            {
                if (model == null || string.IsNullOrWhiteSpace(configurationName))
                    return string.Empty;

                try
                {
                    object configuration = model.GetConfigurationByName(configurationName);
                    if (configuration == null)
                        return string.Empty;

                    object value = configuration.GetType().InvokeMember("Description", BindingFlags.GetProperty, null, configuration, null);
                    return value == null ? string.Empty : Convert.ToString(value);
                }
                catch
                {
                    return string.Empty;
                }
            }

            private static string ReadCustomProperty(IModelDoc2 model, string configurationName, string propertyName)
            {
                if (model == null || string.IsNullOrWhiteSpace(propertyName))
                    return string.Empty;

                CustomPropertyManager manager = null;
                try
                {
                    object extension = model.Extension;
                    string config = string.IsNullOrWhiteSpace(configurationName) ? string.Empty : configurationName;
                    manager = extension.GetType().InvokeMember("CustomPropertyManager", BindingFlags.GetProperty, null, extension, new object[] { config }) as CustomPropertyManager;
                }
                catch
                {
                    manager = null;
                }

                if (manager == null)
                    return string.Empty;

                string value = string.Empty;
                string resolved = string.Empty;
                bool wasResolved = false;
                try
                {
                    manager.Get5(propertyName, false, out value, out resolved, out wasResolved);
                    if (!string.IsNullOrWhiteSpace(resolved))
                        return resolved;
                    return value ?? string.Empty;
                }
                catch
                {
                    return string.Empty;
                }
            }

            private static string SafeComponentName(IComponent2 component)
            {
                if (component == null)
                    return string.Empty;
                try { return component.Name2 ?? string.Empty; } catch { return string.Empty; }
            }

            private static string SafeComponentPath(IComponent2 component)
            {
                if (component == null)
                    return string.Empty;
                try { return component.GetPathName() ?? string.Empty; } catch { return string.Empty; }
            }

            private static string GetTopLevelComponentName(IComponent2 component)
            {
                string name = SafeComponentName(component);
                if (string.IsNullOrWhiteSpace(name))
                    return string.Empty;
                int slash = name.IndexOf('/');
                if (slash > 0)
                    return name.Substring(0, slash);
                return name;
            }

            private static string SafeReferencedConfiguration(IComponent2 component)
            {
                if (component == null)
                    return string.Empty;
                try { return component.ReferencedConfiguration ?? string.Empty; } catch { return string.Empty; }
            }

            private static bool IsComponentSuppressed(IComponent2 component)
            {
                if (component == null)
                    return true;

                try
                {
                    int state = component.GetSuppression();
                    return state == (int)swComponentSuppressionState_e.swComponentSuppressed;
                }
                catch
                {
                    return false;
                }
            }

            private static List<string> GetSelectedComponentKeys(IModelDoc2 model)
            {
                List<string> keys = new List<string>();
                foreach (IComponent2 component in GetSelectedComponents(model))
                {
                    string topLevelName = GetTopLevelComponentName(component);
                    string path = SafeComponentPath(component);
                    if (!string.IsNullOrWhiteSpace(topLevelName))
                        AddUnique(keys, topLevelName + "|" + path);
                    AddUnique(keys, SafeComponentName(component) + "|" + path);
                }
                return keys;
            }

            private static List<IComponent2> GetSelectedComponents(IModelDoc2 model)
            {
                List<IComponent2> components = new List<IComponent2>();
                if (model == null)
                    return components;

                ISelectionMgr selectionManager = null;
                try { selectionManager = model.SelectionManager as ISelectionMgr; } catch { selectionManager = null; }
                if (selectionManager == null)
                    return components;

                int count = 0;
                try { count = selectionManager.GetSelectedObjectCount2(-1); } catch { count = 0; }

                for (int i = 1; i <= count; i++)
                {
                    IComponent2 component = null;
                    try { component = selectionManager.GetSelectedObjectsComponent4(i, -1) as IComponent2; } catch { component = null; }
                    if (component == null)
                    {
                        object selectedObject = null;
                        try { selectedObject = selectionManager.GetSelectedObject6(i, -1); } catch { selectedObject = null; }
                        component = selectedObject as IComponent2;
                    }

                    if (component == null)
                        continue;

                    bool exists = false;
                    foreach (IComponent2 existing in components)
                    {
                        if (ContainsMatchingComponent(new List<IComponent2> { existing }, component))
                        {
                            exists = true;
                            break;
                        }
                    }

                    if (!exists)
                        components.Add(component);
                }

                return components;
            }

            private static bool ContainsMatchingComponent(List<IComponent2> selectedComponents, IComponent2 candidate)
            {
                if (candidate == null)
                    return false;

                string candidateName = SafeComponentName(candidate);
                string candidatePath = SafeComponentPath(candidate);

                foreach (IComponent2 selected in selectedComponents)
                {
                    if (selected == null)
                        continue;

                    if (object.ReferenceEquals(selected, candidate))
                        return true;

                    string selectedName = SafeComponentName(selected);
                    string selectedPath = SafeComponentPath(selected);

                    if (string.Equals(selectedName, candidateName, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(selectedPath, candidatePath, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                return false;
            }

            private static List<string> GetConfigurationNames(IComponent2 component)
            {
                List<string> names = new List<string>();
                if (component == null)
                    return names;

                IModelDoc2 referencedModel = null;
                try { referencedModel = component.GetModelDoc2() as IModelDoc2; } catch { referencedModel = null; }
                if (referencedModel == null)
                    return names;

                return GetConfigurationNames(referencedModel);
            }

            private static List<string> GetConfigurationNames(IModelDoc2 model)
            {
                List<string> names = new List<string>();
                if (model == null)
                    return names;

                object configObject = null;
                try { configObject = model.GetConfigurationNames(); } catch { configObject = null; }

                string[] stringArray = configObject as string[];
                if (stringArray != null)
                {
                    foreach (string name in stringArray)
                        AddUnique(names, name);
                    return names;
                }

                object[] objectArray = configObject as object[];
                if (objectArray != null)
                {
                    foreach (object item in objectArray)
                        AddUnique(names, Convert.ToString(item));
                }

                return names;
            }

            private static void AddUnique(List<string> values, string value)
            {
                if (string.IsNullOrWhiteSpace(value))
                    return;

                foreach (string existing in values)
                {
                    if (string.Equals(existing, value, StringComparison.OrdinalIgnoreCase))
                        return;
                }

                values.Add(value);
            }

            private static bool ConfigurationExists(ComponentConfigRow row, string configurationName)
            {
                if (row == null || string.IsNullOrWhiteSpace(configurationName))
                    return false;

                foreach (string name in row.ConfigurationNames)
                {
                    if (string.Equals(name, configurationName, StringComparison.OrdinalIgnoreCase))
                        return true;
                }

                return false;
            }

            private static bool SetReferencedConfiguration(IComponent2 component, string targetConfiguration)
            {
                if (component == null || string.IsNullOrWhiteSpace(targetConfiguration))
                    return false;

                try
                {
                    component.ReferencedConfiguration = targetConfiguration;
                    return true;
                }
                catch
                {
                }

                try
                {
                    object rawComponent = component;
                    rawComponent.GetType().InvokeMember("ReferencedConfiguration", BindingFlags.SetProperty, null, rawComponent, new object[] { targetConfiguration });
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        private sealed class ComponentDocumentInfo
        {
            public List<string> ConfigurationNames = new List<string>();
            public string ComponentDescription = string.Empty;
        }

        private sealed class ComponentConfigRow
        {
            public bool Apply;
            public IComponent2 Component;
            public string InstanceName = string.Empty;
            public string Path = string.Empty;
            public string FileName = string.Empty;
            public string ComponentKey = string.Empty;
            public string Description = string.Empty;
            public string CurrentConfiguration = string.Empty;
            public string TargetConfiguration = string.Empty;
            public bool IsSuppressed;
            public string Status = string.Empty;
            public List<string> ConfigurationNames = new List<string>();
        }

        private sealed class RowSnapshot
        {
            public string ComponentKey = string.Empty;
            public bool Apply;
            public string CurrentConfiguration = string.Empty;
            public string TargetConfiguration = string.Empty;
            public string Status = string.Empty;
        }
    }
}
