using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace SolidDNA
{
    /// <summary>
    /// Wall Panel Configuration Manager V1.
    ///
    /// Practical V1 scope:
    /// - Active assembly only.
    /// - Selected panels, all recognised panels, all with same reinforcement, or all instances of same file.
    /// - Direct component ReferencedConfiguration backend.
    /// - Multi-assembly-configuration scope by activating assembly configurations and applying direct component configuration changes.
    /// - Preview before Apply and rollback of last applied direct changes.
    ///
    /// Deliberately not included in this first coded release:
    /// - Editing assembly cuts.
    /// - Handedness logic.
    /// - Creating missing panel configurations.
    /// - Full design-table cell editing backend. Design table presence is detected and reported as a warning.
    /// </summary>
    internal static class WallPanelConfigurationManagerCommand
    {
        private const string DefaultPanelTypeProperty = "PANEL_TYPE";
        private const string DefaultPanelTypeValue = "Wall Panel";
        private const string WidthProperty = "PANEL_WIDTH";
        private const string LengthProperty = "PANEL_LENGTH";
        private const string ReinforcementProperty = "REINFORCEMENT_SCHEME";
        private const string ConfigurationKeyProperty = "CONFIGURATION_KEY";

        public static void ShowWallPanelManagerForm()
        {
            try
            {
                IModelDoc2 modelDoc = CabinCustomPropertyStore.GetActiveModelDocument();

                if (modelDoc == null)
                {
                    ShowMessage("Open an assembly before running Wall Panel Configuration Manager.", MessageBoxIcon.Warning);
                    return;
                }

                if (modelDoc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
                {
                    ShowMessage("Wall Panel Configuration Manager works only with an active assembly document.", MessageBoxIcon.Warning);
                    return;
                }

                using (WallPanelManagerForm form = new WallPanelManagerForm(modelDoc))
                {
                    form.ShowDialog();
                }
            }
            catch (Exception ex)
            {
                ShowMessage("Cabin Tools could not open Wall Panel Configuration Manager.\r\n\r\n" + ex.Message, MessageBoxIcon.Error);
            }
        }

        private static void ShowMessage(string message, MessageBoxIcon icon)
        {
            MessageBox.Show(message, "Cabin Tools - Wall Panel Configuration Manager", MessageBoxButtons.OK, icon);
        }

        internal sealed class PanelInstance
        {
            public IComponent2 Component;
            public string ComponentName = string.Empty;
            public string ComponentPath = string.Empty;
            public string PartPath = string.Empty;
            public string CurrentConfiguration = string.Empty;
            public string CurrentWidth = string.Empty;
            public string CurrentLength = string.Empty;
            public string CurrentReinforcement = string.Empty;
            public bool Recognised;
            public bool Selected;
            public bool Suppressed;
            public string RecognitionMessage = string.Empty;
            public List<PanelConfiguration> Catalogue = new List<PanelConfiguration>();
        }

        internal sealed class PanelConfiguration
        {
            public string Name = string.Empty;
            public string Width = string.Empty;
            public string Length = string.Empty;
            public string Reinforcement = string.Empty;
            public string Key = string.Empty;
        }

        internal sealed class ChangePlanItem
        {
            public string AssemblyConfiguration = string.Empty;
            public PanelInstance Panel;
            public string OriginalConfiguration = string.Empty;
            public string TargetConfiguration = string.Empty;
            public string Status = string.Empty;
            public string Message = string.Empty;
        }

        internal sealed class AppliedChange
        {
            public string AssemblyConfiguration = string.Empty;
            public string ComponentName = string.Empty;
            public string PartPath = string.Empty;
            public string OriginalConfiguration = string.Empty;
            public string TargetConfiguration = string.Empty;
        }

        private sealed class WallPanelManagerForm : Form
        {
            private readonly IModelDoc2 assemblyModel;
            private readonly IAssemblyDoc assemblyDoc;
            private readonly List<PanelInstance> allPanels = new List<PanelInstance>();
            private readonly List<ChangePlanItem> currentPlan = new List<ChangePlanItem>();
            private readonly List<AppliedChange> lastAppliedChanges = new List<AppliedChange>();

            private DataGridView panelGrid;
            private DataGridView previewGrid;
            private ComboBox selectionModeBox;
            private ComboBox changeTypeBox;
            private ComboBox targetConfigurationBox;
            private ComboBox targetReinforcementBox;
            private ComboBox scopeBox;
            private CheckedListBox assemblyConfigChecklist;
            private TextBox includeConfigPatternTextBox;
            private TextBox excludeConfigPatternTextBox;
            private CheckBox preserveWidthCheckBox;
            private CheckBox preserveLengthCheckBox;
            private CheckBox allowDirectOverrideCheckBox;
            private Label statusLabel;

            public WallPanelManagerForm(IModelDoc2 assemblyModel)
            {
                this.assemblyModel = assemblyModel;
                this.assemblyDoc = assemblyModel as IAssemblyDoc;

                Text = "Cabin Tools - Wall Panel Configuration Manager V1";
                Width = 1450;
                Height = 860;
                StartPosition = FormStartPosition.CenterScreen;
                Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

                BuildLayout();
                LoadAssemblyConfigurations();
                ScanPanels();
                FillTargetControls();
                BuildPanelGrid();
                BuildPreviewGrid();
            }

            private void BuildLayout()
            {
                Label help = new Label();
                help.Text = "Change wall panel referenced configurations in bulk. Assembly cuts are not edited. Preview before Apply. Width and length are preserved when a matching target configuration exists.";
                help.Left = 12;
                help.Top = 10;
                help.Width = 1380;
                help.Height = 24;
                Controls.Add(help);

                GroupBox options = new GroupBox();
                options.Text = "Options";
                options.Left = 12;
                options.Top = 42;
                options.Width = 1410;
                options.Height = 144;
                Controls.Add(options);

                Label selectionLabel = new Label();
                selectionLabel.Text = "Target panels:";
                selectionLabel.Left = 12;
                selectionLabel.Top = 30;
                selectionLabel.Width = 100;
                options.Controls.Add(selectionLabel);

                selectionModeBox = new ComboBox();
                selectionModeBox.Left = 115;
                selectionModeBox.Top = 26;
                selectionModeBox.Width = 250;
                selectionModeBox.DropDownStyle = ComboBoxStyle.DropDownList;
                selectionModeBox.Items.AddRange(new object[] { "Selected panels only", "All recognised wall panels", "All panels with same reinforcement as checked", "All instances of same panel file as checked" });
                selectionModeBox.SelectedIndex = 0;
                selectionModeBox.SelectedIndexChanged += delegate { ApplySelectionExpansionMode(); };
                options.Controls.Add(selectionModeBox);

                Label changeLabel = new Label();
                changeLabel.Text = "Change type:";
                changeLabel.Left = 390;
                changeLabel.Top = 30;
                changeLabel.Width = 90;
                options.Controls.Add(changeLabel);

                changeTypeBox = new ComboBox();
                changeTypeBox.Left = 480;
                changeTypeBox.Top = 26;
                changeTypeBox.Width = 185;
                changeTypeBox.DropDownStyle = ComboBoxStyle.DropDownList;
                changeTypeBox.Items.AddRange(new object[] { "Reinforcement scheme", "Exact configuration" });
                changeTypeBox.SelectedIndex = 0;
                options.Controls.Add(changeTypeBox);

                Label schemeLabel = new Label();
                schemeLabel.Text = "Target reinforcement:";
                schemeLabel.Left = 685;
                schemeLabel.Top = 30;
                schemeLabel.Width = 140;
                options.Controls.Add(schemeLabel);

                targetReinforcementBox = new ComboBox();
                targetReinforcementBox.Left = 825;
                targetReinforcementBox.Top = 26;
                targetReinforcementBox.Width = 170;
                targetReinforcementBox.DropDownStyle = ComboBoxStyle.DropDown;
                options.Controls.Add(targetReinforcementBox);

                Label exactLabel = new Label();
                exactLabel.Text = "Exact config:";
                exactLabel.Left = 1015;
                exactLabel.Top = 30;
                exactLabel.Width = 90;
                options.Controls.Add(exactLabel);

                targetConfigurationBox = new ComboBox();
                targetConfigurationBox.Left = 1105;
                targetConfigurationBox.Top = 26;
                targetConfigurationBox.Width = 280;
                targetConfigurationBox.DropDownStyle = ComboBoxStyle.DropDown;
                options.Controls.Add(targetConfigurationBox);

                Label scopeLabel = new Label();
                scopeLabel.Text = "Assembly config scope:";
                scopeLabel.Left = 12;
                scopeLabel.Top = 72;
                scopeLabel.Width = 145;
                options.Controls.Add(scopeLabel);

                scopeBox = new ComboBox();
                scopeBox.Left = 160;
                scopeBox.Top = 68;
                scopeBox.Width = 205;
                scopeBox.DropDownStyle = ComboBoxStyle.DropDownList;
                scopeBox.Items.AddRange(new object[] { "Active configuration only", "Checked configurations", "All configurations" });
                scopeBox.SelectedIndex = 0;
                options.Controls.Add(scopeBox);

                Label includeLabel = new Label();
                includeLabel.Text = "Include pattern:";
                includeLabel.Left = 390;
                includeLabel.Top = 72;
                includeLabel.Width = 105;
                options.Controls.Add(includeLabel);

                includeConfigPatternTextBox = new TextBox();
                includeConfigPatternTextBox.Left = 500;
                includeConfigPatternTextBox.Top = 68;
                includeConfigPatternTextBox.Width = 160;
                options.Controls.Add(includeConfigPatternTextBox);

                Label excludeLabel = new Label();
                excludeLabel.Text = "Exclude pattern:";
                excludeLabel.Left = 685;
                excludeLabel.Top = 72;
                excludeLabel.Width = 105;
                options.Controls.Add(excludeLabel);

                excludeConfigPatternTextBox = new TextBox();
                excludeConfigPatternTextBox.Left = 795;
                excludeConfigPatternTextBox.Top = 68;
                excludeConfigPatternTextBox.Width = 160;
                options.Controls.Add(excludeConfigPatternTextBox);

                preserveWidthCheckBox = new CheckBox();
                preserveWidthCheckBox.Text = "Preserve width";
                preserveWidthCheckBox.Left = 980;
                preserveWidthCheckBox.Top = 70;
                preserveWidthCheckBox.Width = 120;
                preserveWidthCheckBox.Checked = true;
                options.Controls.Add(preserveWidthCheckBox);

                preserveLengthCheckBox = new CheckBox();
                preserveLengthCheckBox.Text = "Preserve length";
                preserveLengthCheckBox.Left = 1110;
                preserveLengthCheckBox.Top = 70;
                preserveLengthCheckBox.Width = 120;
                preserveLengthCheckBox.Checked = true;
                options.Controls.Add(preserveLengthCheckBox);

                allowDirectOverrideCheckBox = new CheckBox();
                allowDirectOverrideCheckBox.Text = "Allow direct override if design table exists";
                allowDirectOverrideCheckBox.Left = 12;
                allowDirectOverrideCheckBox.Top = 108;
                allowDirectOverrideCheckBox.Width = 280;
                allowDirectOverrideCheckBox.Checked = false;
                options.Controls.Add(allowDirectOverrideCheckBox);

                Button rescanButton = new Button();
                rescanButton.Text = "Rescan";
                rescanButton.Left = 315;
                rescanButton.Top = 104;
                rescanButton.Width = 90;
                rescanButton.Click += delegate { ScanPanels(); FillTargetControls(); BuildPanelGrid(); BuildPreviewGrid(); };
                options.Controls.Add(rescanButton);

                Button previewButton = new Button();
                previewButton.Text = "Preview Changes";
                previewButton.Left = 420;
                previewButton.Top = 104;
                previewButton.Width = 130;
                previewButton.Click += delegate { PreviewChanges(); };
                options.Controls.Add(previewButton);

                Button applyButton = new Button();
                applyButton.Text = "Apply";
                applyButton.Left = 565;
                applyButton.Top = 104;
                applyButton.Width = 90;
                applyButton.Click += delegate { ApplyChanges(); };
                options.Controls.Add(applyButton);

                Button rollbackButton = new Button();
                rollbackButton.Text = "Rollback Last";
                rollbackButton.Left = 670;
                rollbackButton.Top = 104;
                rollbackButton.Width = 120;
                rollbackButton.Click += delegate { RollbackLast(); };
                options.Controls.Add(rollbackButton);

                Button closeButton = new Button();
                closeButton.Text = "Close";
                closeButton.Left = 805;
                closeButton.Top = 104;
                closeButton.Width = 90;
                closeButton.Click += delegate { Close(); };
                options.Controls.Add(closeButton);

                Label configsLabel = new Label();
                configsLabel.Text = "Assembly configurations:";
                configsLabel.Left = 12;
                configsLabel.Top = 196;
                configsLabel.Width = 200;
                Controls.Add(configsLabel);

                assemblyConfigChecklist = new CheckedListBox();
                assemblyConfigChecklist.Left = 12;
                assemblyConfigChecklist.Top = 220;
                assemblyConfigChecklist.Width = 300;
                assemblyConfigChecklist.Height = 220;
                assemblyConfigChecklist.CheckOnClick = true;
                Controls.Add(assemblyConfigChecklist);

                panelGrid = new DataGridView();
                panelGrid.Left = 322;
                panelGrid.Top = 196;
                panelGrid.Width = 1100;
                panelGrid.Height = 245;
                panelGrid.AllowUserToAddRows = false;
                panelGrid.AllowUserToDeleteRows = false;
                panelGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
                panelGrid.MultiSelect = true;
                panelGrid.CellContentClick += delegate(object sender, DataGridViewCellEventArgs e) { panelGrid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
                Controls.Add(panelGrid);

                Label previewLabel = new Label();
                previewLabel.Text = "Preview / result plan:";
                previewLabel.Left = 12;
                previewLabel.Top = 455;
                previewLabel.Width = 200;
                Controls.Add(previewLabel);

                previewGrid = new DataGridView();
                previewGrid.Left = 12;
                previewGrid.Top = 480;
                previewGrid.Width = 1410;
                previewGrid.Height = 260;
                previewGrid.AllowUserToAddRows = false;
                previewGrid.AllowUserToDeleteRows = false;
                previewGrid.ReadOnly = true;
                previewGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
                Controls.Add(previewGrid);

                statusLabel = new Label();
                statusLabel.Left = 12;
                statusLabel.Top = 755;
                statusLabel.Width = 1410;
                statusLabel.Height = 44;
                statusLabel.BorderStyle = BorderStyle.FixedSingle;
                statusLabel.Text = "Ready";
                Controls.Add(statusLabel);
            }

            private void LoadAssemblyConfigurations()
            {
                assemblyConfigChecklist.Items.Clear();
                string active = SafeActiveConfigurationName(assemblyModel);
                foreach (string name in GetConfigurationNames(assemblyModel))
                {
                    int index = assemblyConfigChecklist.Items.Add(name);
                    assemblyConfigChecklist.SetItemChecked(index, string.Equals(name, active, StringComparison.OrdinalIgnoreCase));
                }
            }

            private void ScanPanels()
            {
                allPanels.Clear();

                List<IComponent2> selectedComponents = ReferenceMateAssistantCommand.GetSelectedComponents(assemblyModel);
                List<IComponent2> components = GetAllComponents();

                foreach (IComponent2 component in components)
                {
                    PanelInstance panel = CreatePanelInstance(component, selectedComponents);
                    if (panel != null)
                        allPanels.Add(panel);
                }

                if (HasDesignTable(assemblyModel))
                {
                    statusLabel.Text = "Warning: assembly design table detected. This V1 uses direct component configuration changes unless you explicitly allow override. Full design-table cell editing is planned as a later backend.";
                }
                else
                {
                    statusLabel.Text = "Scanned " + allPanels.Count + " component(s). Recognised wall panels: " + CountRecognisedPanels();
                }
            }

            private int CountRecognisedPanels()
            {
                int count = 0;
                foreach (PanelInstance p in allPanels)
                    if (p.Recognised) count++;
                return count;
            }

            private PanelInstance CreatePanelInstance(IComponent2 component, List<IComponent2> selectedComponents)
            {
                if (component == null)
                    return null;

                IModelDoc2 partModel = null;
                try { partModel = component.GetModelDoc2() as IModelDoc2; } catch { partModel = null; }
                if (partModel == null)
                    return null;

                int partType = 0;
                try { partType = partModel.GetType(); } catch { partType = 0; }
                if (partType != (int)swDocumentTypes_e.swDocPART)
                    return null;

                PanelInstance panel = new PanelInstance();
                panel.Component = component;
                panel.ComponentName = SafeComponentName(component);
                panel.ComponentPath = SafeComponentPath(component);
                panel.PartPath = SafeModelPath(partModel);
                panel.CurrentConfiguration = SafeReferencedConfiguration(component);
                panel.Selected = ContainsComponent(selectedComponents, component);
                panel.Suppressed = IsSuppressed(component);

                ICustomPropertyManager propertyManager = ConfigurationPartNumberCommand.GetConfigurationPropertyManager(partModel, panel.CurrentConfiguration);
                string panelType = ConfigurationPartNumberCommand.ReadTextProperty(propertyManager, DefaultPanelTypeProperty);
                panel.CurrentWidth = NormalizeNumber(ConfigurationPartNumberCommand.ReadTextProperty(propertyManager, WidthProperty));
                panel.CurrentLength = NormalizeNumber(ConfigurationPartNumberCommand.ReadTextProperty(propertyManager, LengthProperty));
                panel.CurrentReinforcement = NormalizeReinforcement(ConfigurationPartNumberCommand.ReadTextProperty(propertyManager, ReinforcementProperty));

                if (string.IsNullOrWhiteSpace(panel.CurrentWidth) || string.IsNullOrWhiteSpace(panel.CurrentReinforcement))
                {
                    ParseConfigurationName(panel.CurrentConfiguration, ref panel.CurrentWidth, ref panel.CurrentLength, ref panel.CurrentReinforcement);
                }

                panel.Recognised = IsRecognisedPanel(partModel, panel, panelType);
                panel.RecognitionMessage = panel.Recognised ? "Recognised" : "Not recognised";
                panel.Catalogue = BuildCatalogue(partModel);

                return panel;
            }

            private bool IsRecognisedPanel(IModelDoc2 partModel, PanelInstance panel, string panelType)
            {
                if (string.Equals(panelType, DefaultPanelTypeValue, StringComparison.OrdinalIgnoreCase))
                    return true;

                string path = panel.PartPath ?? string.Empty;
                string fileName = Path.GetFileNameWithoutExtension(path) ?? string.Empty;
                string configuration = panel.CurrentConfiguration ?? string.Empty;

                if (fileName.IndexOf("wall", StringComparison.OrdinalIgnoreCase) >= 0 && fileName.IndexOf("panel", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                if (!string.IsNullOrWhiteSpace(panel.CurrentWidth) && !string.IsNullOrWhiteSpace(panel.CurrentReinforcement) && configuration.IndexOf("S", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                return false;
            }

            private List<PanelConfiguration> BuildCatalogue(IModelDoc2 partModel)
            {
                List<PanelConfiguration> result = new List<PanelConfiguration>();
                foreach (string configName in GetConfigurationNames(partModel))
                {
                    PanelConfiguration descriptor = new PanelConfiguration();
                    descriptor.Name = configName;

                    ICustomPropertyManager propertyManager = ConfigurationPartNumberCommand.GetConfigurationPropertyManager(partModel, configName);
                    descriptor.Width = NormalizeNumber(ConfigurationPartNumberCommand.ReadTextProperty(propertyManager, WidthProperty));
                    descriptor.Length = NormalizeNumber(ConfigurationPartNumberCommand.ReadTextProperty(propertyManager, LengthProperty));
                    descriptor.Reinforcement = NormalizeReinforcement(ConfigurationPartNumberCommand.ReadTextProperty(propertyManager, ReinforcementProperty));
                    descriptor.Key = ConfigurationPartNumberCommand.ReadTextProperty(propertyManager, ConfigurationKeyProperty);

                    if (string.IsNullOrWhiteSpace(descriptor.Width) || string.IsNullOrWhiteSpace(descriptor.Reinforcement))
                    {
                        string width = descriptor.Width;
                        string length = descriptor.Length;
                        string reinforcement = descriptor.Reinforcement;
                        ParseConfigurationName(configName, ref width, ref length, ref reinforcement);
                        descriptor.Width = width;
                        descriptor.Length = length;
                        descriptor.Reinforcement = reinforcement;
                    }

                    result.Add(descriptor);
                }
                return result;
            }

            private void BuildPanelGrid()
            {
                panelGrid.Columns.Clear();
                panelGrid.Rows.Clear();

                DataGridViewCheckBoxColumn apply = new DataGridViewCheckBoxColumn();
                apply.Name = "Apply";
                apply.HeaderText = "Apply";
                apply.Width = 52;
                panelGrid.Columns.Add(apply);

                AddTextColumn(panelGrid, "Component", "Component instance", 220, true);
                AddTextColumn(panelGrid, "Part", "Part file", 260, true);
                AddTextColumn(panelGrid, "Config", "Current panel configuration", 220, true);
                AddTextColumn(panelGrid, "Width", "Width", 75, true);
                AddTextColumn(panelGrid, "Length", "Length", 75, true);
                AddTextColumn(panelGrid, "Reinf", "Reinforcement", 110, true);
                AddTextColumn(panelGrid, "Status", "Status", 180, true);

                foreach (PanelInstance panel in allPanels)
                {
                    int index = panelGrid.Rows.Add(
                        panel.Selected && panel.Recognised,
                        panel.ComponentName,
                        Path.GetFileName(panel.PartPath),
                        panel.CurrentConfiguration,
                        panel.CurrentWidth,
                        panel.CurrentLength,
                        panel.CurrentReinforcement,
                        panel.RecognitionMessage);

                    panelGrid.Rows[index].Tag = panel;
                    if (!panel.Recognised)
                        panelGrid.Rows[index].DefaultCellStyle.BackColor = Color.MistyRose;
                    else if (panel.Selected)
                        panelGrid.Rows[index].DefaultCellStyle.BackColor = Color.Honeydew;
                }
            }

            private void BuildPreviewGrid()
            {
                previewGrid.Columns.Clear();
                previewGrid.Rows.Clear();
                AddTextColumn(previewGrid, "AsmConfig", "Assembly configuration", 180, true);
                AddTextColumn(previewGrid, "Component", "Component instance", 220, true);
                AddTextColumn(previewGrid, "Original", "Current panel configuration", 230, true);
                AddTextColumn(previewGrid, "Target", "Target panel configuration", 230, true);
                AddTextColumn(previewGrid, "Status", "Status", 130, true);
                AddTextColumn(previewGrid, "Message", "Message", 520, true);
            }

            private static void AddTextColumn(DataGridView grid, string name, string header, int width, bool readOnly)
            {
                DataGridViewTextBoxColumn column = new DataGridViewTextBoxColumn();
                column.Name = name;
                column.HeaderText = header;
                column.Width = width;
                column.ReadOnly = readOnly;
                grid.Columns.Add(column);
            }

            private void FillTargetControls()
            {
                List<string> schemes = new List<string>();
                List<string> configs = new List<string>();

                foreach (PanelInstance panel in allPanels)
                {
                    if (!string.IsNullOrWhiteSpace(panel.CurrentReinforcement) && !ContainsIgnoreCase(schemes, panel.CurrentReinforcement))
                        schemes.Add(panel.CurrentReinforcement);

                    foreach (PanelConfiguration cfg in panel.Catalogue)
                    {
                        if (!string.IsNullOrWhiteSpace(cfg.Reinforcement) && !ContainsIgnoreCase(schemes, cfg.Reinforcement))
                            schemes.Add(cfg.Reinforcement);
                        if (!string.IsNullOrWhiteSpace(cfg.Name) && !ContainsIgnoreCase(configs, cfg.Name))
                            configs.Add(cfg.Name);
                    }
                }

                schemes.Sort(StringComparer.OrdinalIgnoreCase);
                configs.Sort(StringComparer.OrdinalIgnoreCase);

                targetReinforcementBox.Items.Clear();
                foreach (string scheme in schemes)
                    targetReinforcementBox.Items.Add(scheme);
                if (targetReinforcementBox.Items.Count > 0)
                    targetReinforcementBox.SelectedIndex = 0;

                targetConfigurationBox.Items.Clear();
                foreach (string config in configs)
                    targetConfigurationBox.Items.Add(config);
                if (targetConfigurationBox.Items.Count > 0)
                    targetConfigurationBox.SelectedIndex = 0;
            }

            private void ApplySelectionExpansionMode()
            {
                string mode = Convert.ToString(selectionModeBox.SelectedItem);
                List<PanelInstance> checkedPanels = GetCheckedPanels();

                if (string.Equals(mode, "Selected panels only", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (DataGridViewRow row in panelGrid.Rows)
                    {
                        PanelInstance p = row.Tag as PanelInstance;
                        if (p != null)
                            row.Cells["Apply"].Value = p.Selected && p.Recognised;
                    }
                }
                else if (string.Equals(mode, "All recognised wall panels", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (DataGridViewRow row in panelGrid.Rows)
                    {
                        PanelInstance p = row.Tag as PanelInstance;
                        if (p != null)
                            row.Cells["Apply"].Value = p.Recognised;
                    }
                }
                else if (string.Equals(mode, "All panels with same reinforcement as checked", StringComparison.OrdinalIgnoreCase))
                {
                    HashSet<string> schemes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (PanelInstance p in checkedPanels)
                        if (!string.IsNullOrWhiteSpace(p.CurrentReinforcement)) schemes.Add(p.CurrentReinforcement);
                    foreach (DataGridViewRow row in panelGrid.Rows)
                    {
                        PanelInstance p = row.Tag as PanelInstance;
                        row.Cells["Apply"].Value = p != null && p.Recognised && schemes.Contains(p.CurrentReinforcement);
                    }
                }
                else if (string.Equals(mode, "All instances of same panel file as checked", StringComparison.OrdinalIgnoreCase))
                {
                    HashSet<string> paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (PanelInstance p in checkedPanels)
                        if (!string.IsNullOrWhiteSpace(p.PartPath)) paths.Add(p.PartPath);
                    foreach (DataGridViewRow row in panelGrid.Rows)
                    {
                        PanelInstance p = row.Tag as PanelInstance;
                        row.Cells["Apply"].Value = p != null && p.Recognised && paths.Contains(p.PartPath);
                    }
                }
            }

            private List<PanelInstance> GetCheckedPanels()
            {
                List<PanelInstance> result = new List<PanelInstance>();
                panelGrid.EndEdit();
                foreach (DataGridViewRow row in panelGrid.Rows)
                {
                    bool apply = false;
                    try { apply = Convert.ToBoolean(row.Cells["Apply"].Value); } catch { apply = false; }
                    PanelInstance panel = row.Tag as PanelInstance;
                    if (apply && panel != null && panel.Recognised)
                        result.Add(panel);
                }
                return result;
            }

            private List<string> GetTargetAssemblyConfigurations()
            {
                List<string> result = new List<string>();
                string scope = Convert.ToString(scopeBox.SelectedItem);
                string includePattern = includeConfigPatternTextBox.Text ?? string.Empty;
                string excludePattern = excludeConfigPatternTextBox.Text ?? string.Empty;

                if (string.Equals(scope, "Active configuration only", StringComparison.OrdinalIgnoreCase))
                {
                    string active = SafeActiveConfigurationName(assemblyModel);
                    if (!string.IsNullOrWhiteSpace(active))
                        result.Add(active);
                    return result;
                }

                if (string.Equals(scope, "Checked configurations", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (object item in assemblyConfigChecklist.CheckedItems)
                    {
                        string name = Convert.ToString(item);
                        if (ConfigurationNameAllowed(name, includePattern, excludePattern))
                            result.Add(name);
                    }
                    return result;
                }

                foreach (string name in GetConfigurationNames(assemblyModel))
                {
                    if (ConfigurationNameAllowed(name, includePattern, excludePattern))
                        result.Add(name);
                }
                return result;
            }

            private bool ConfigurationNameAllowed(string name, string includePattern, string excludePattern)
            {
                if (string.IsNullOrWhiteSpace(name))
                    return false;
                if (!string.IsNullOrWhiteSpace(includePattern) && name.IndexOf(includePattern, StringComparison.OrdinalIgnoreCase) < 0)
                    return false;
                if (!string.IsNullOrWhiteSpace(excludePattern) && name.IndexOf(excludePattern, StringComparison.OrdinalIgnoreCase) >= 0)
                    return false;
                return true;
            }

            private void PreviewChanges()
            {
                currentPlan.Clear();
                previewGrid.Rows.Clear();

                List<PanelInstance> panels = GetCheckedPanels();
                List<string> asmConfigs = GetTargetAssemblyConfigurations();

                if (panels.Count == 0)
                {
                    statusLabel.Text = "No recognised checked panels.";
                    return;
                }
                if (asmConfigs.Count == 0)
                {
                    statusLabel.Text = "No target assembly configurations.";
                    return;
                }

                string originalActive = SafeActiveConfigurationName(assemblyModel);
                bool designTableExists = HasDesignTable(assemblyModel);
                if (designTableExists && !allowDirectOverrideCheckBox.Checked)
                {
                    statusLabel.Text = "Design table detected. Preview is allowed, but Apply is blocked unless direct override is allowed. A later backend should edit the design table cells.";
                }

                foreach (string asmConfig in asmConfigs)
                {
                    try { assemblyModel.ShowConfiguration2(asmConfig); } catch { }
                    try { assemblyModel.ForceRebuild3(false); } catch { }

                    foreach (PanelInstance originalPanel in panels)
                    {
                        PanelInstance activePanel = FindActivePanelInstance(originalPanel);
                        ChangePlanItem item = BuildChangeItem(asmConfig, activePanel ?? originalPanel);
                        currentPlan.Add(item);
                        AddPlanRow(item);
                    }
                }

                try { if (!string.IsNullOrWhiteSpace(originalActive)) assemblyModel.ShowConfiguration2(originalActive); } catch { }
                try { assemblyModel.ForceRebuild3(false); } catch { }

                statusLabel.Text = "Preview built: " + currentPlan.Count + " item(s).";
            }

            private ChangePlanItem BuildChangeItem(string assemblyConfiguration, PanelInstance panel)
            {
                ChangePlanItem item = new ChangePlanItem();
                item.AssemblyConfiguration = assemblyConfiguration;
                item.Panel = panel;
                item.OriginalConfiguration = panel == null ? string.Empty : panel.CurrentConfiguration;

                if (panel == null || panel.Component == null)
                {
                    item.Status = "ERROR";
                    item.Message = "Component not found in this assembly configuration.";
                    return item;
                }

                if (panel.Suppressed)
                {
                    item.Status = "SKIPPED";
                    item.Message = "Component is suppressed.";
                    return item;
                }

                string changeType = Convert.ToString(changeTypeBox.SelectedItem);
                if (string.Equals(changeType, "Exact configuration", StringComparison.OrdinalIgnoreCase))
                {
                    string target = Convert.ToString(targetConfigurationBox.Text).Trim();
                    item.TargetConfiguration = target;
                    if (string.IsNullOrWhiteSpace(target))
                    {
                        item.Status = "ERROR";
                        item.Message = "No target configuration selected.";
                    }
                    else if (!PanelHasConfiguration(panel, target))
                    {
                        item.Status = "MISSING TARGET CONFIGURATION";
                        item.Message = "The target configuration does not exist in this panel part.";
                    }
                    else
                    {
                        item.Status = string.Equals(panel.CurrentConfiguration, target, StringComparison.OrdinalIgnoreCase) ? "UNCHANGED" : "READY";
                        item.Message = "Exact configuration mode.";
                    }
                    return item;
                }

                string targetReinforcement = NormalizeReinforcement(Convert.ToString(targetReinforcementBox.Text));
                PanelConfiguration targetConfig = FindMatchingConfiguration(panel, targetReinforcement, preserveWidthCheckBox.Checked, preserveLengthCheckBox.Checked);
                if (targetConfig == null)
                {
                    item.Status = "MISSING TARGET CONFIGURATION";
                    item.Message = "No configuration found preserving width/length with reinforcement " + targetReinforcement + ".";
                }
                else
                {
                    item.TargetConfiguration = targetConfig.Name;
                    item.Status = string.Equals(panel.CurrentConfiguration, item.TargetConfiguration, StringComparison.OrdinalIgnoreCase) ? "UNCHANGED" : "READY";
                    item.Message = "Target reinforcement: " + targetReinforcement;
                }
                return item;
            }

            private PanelConfiguration FindMatchingConfiguration(PanelInstance panel, string targetReinforcement, bool preserveWidth, bool preserveLength)
            {
                if (panel == null || string.IsNullOrWhiteSpace(targetReinforcement))
                    return null;

                foreach (PanelConfiguration cfg in panel.Catalogue)
                {
                    if (!string.Equals(cfg.Reinforcement, targetReinforcement, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (preserveWidth && !StringValueMatches(cfg.Width, panel.CurrentWidth))
                        continue;
                    if (preserveLength && !string.IsNullOrWhiteSpace(panel.CurrentLength) && !StringValueMatches(cfg.Length, panel.CurrentLength))
                        continue;
                    return cfg;
                }

                // Fallback: replace first S-number token in the configuration name.
                string replacement = ReplaceReinforcementToken(panel.CurrentConfiguration, targetReinforcement);
                if (!string.IsNullOrWhiteSpace(replacement) && PanelHasConfiguration(panel, replacement))
                {
                    PanelConfiguration fallback = new PanelConfiguration();
                    fallback.Name = replacement;
                    fallback.Width = panel.CurrentWidth;
                    fallback.Length = panel.CurrentLength;
                    fallback.Reinforcement = targetReinforcement;
                    return fallback;
                }

                return null;
            }

            private static string ReplaceReinforcementToken(string configName, string targetReinforcement)
            {
                if (string.IsNullOrWhiteSpace(configName) || string.IsNullOrWhiteSpace(targetReinforcement))
                    return string.Empty;

                Match m = Regex.Match(configName, @"(?<![A-Za-z0-9])S\s*\d+", RegexOptions.IgnoreCase);
                if (m.Success)
                    return configName.Substring(0, m.Index) + targetReinforcement + configName.Substring(m.Index + m.Length);

                return string.Empty;
            }

            private bool PanelHasConfiguration(PanelInstance panel, string configName)
            {
                if (panel == null || string.IsNullOrWhiteSpace(configName))
                    return false;
                foreach (PanelConfiguration cfg in panel.Catalogue)
                    if (string.Equals(cfg.Name, configName, StringComparison.OrdinalIgnoreCase))
                        return true;
                return false;
            }

            private void AddPlanRow(ChangePlanItem item)
            {
                int index = previewGrid.Rows.Add(item.AssemblyConfiguration, item.Panel == null ? string.Empty : item.Panel.ComponentName, item.OriginalConfiguration, item.TargetConfiguration, item.Status, item.Message);
                if (string.Equals(item.Status, "READY", StringComparison.OrdinalIgnoreCase))
                    previewGrid.Rows[index].DefaultCellStyle.BackColor = Color.Honeydew;
                else if (string.Equals(item.Status, "UNCHANGED", StringComparison.OrdinalIgnoreCase))
                    previewGrid.Rows[index].DefaultCellStyle.BackColor = Color.LightYellow;
                else if (item.Status.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase) || item.Status.IndexOf("MISSING", StringComparison.OrdinalIgnoreCase) >= 0)
                    previewGrid.Rows[index].DefaultCellStyle.BackColor = Color.MistyRose;
            }

            private void ApplyChanges()
            {
                string writeBlockReason = CabinCustomPropertyStore.GetWriteBlockReason(assemblyModel);
                if (!string.IsNullOrWhiteSpace(writeBlockReason))
                {
                    ShowMessage(writeBlockReason, MessageBoxIcon.Warning);
                    return;
                }

                if (HasDesignTable(assemblyModel) && !allowDirectOverrideCheckBox.Checked)
                {
                    ShowMessage("This assembly appears to have a design table. V1 detected that risk and blocked direct override. Enable 'Allow direct override if design table exists' only if you intentionally want to test direct component changes.", MessageBoxIcon.Warning);
                    return;
                }

                if (currentPlan.Count == 0)
                    PreviewChanges();

                lastAppliedChanges.Clear();
                string originalActive = SafeActiveConfigurationName(assemblyModel);
                int changed = 0;
                int skipped = 0;
                int failed = 0;

                foreach (ChangePlanItem item in currentPlan)
                {
                    if (!string.Equals(item.Status, "READY", StringComparison.OrdinalIgnoreCase))
                    {
                        skipped++;
                        continue;
                    }

                    try
                    {
                        assemblyModel.ShowConfiguration2(item.AssemblyConfiguration);
                        assemblyModel.ForceRebuild3(false);

                        PanelInstance activePanel = FindActivePanelInstance(item.Panel);
                        if (activePanel == null || activePanel.Component == null)
                        {
                            item.Status = "ERROR";
                            item.Message = "Component could not be found during apply.";
                            failed++;
                            continue;
                        }

                        string before = SafeReferencedConfiguration(activePanel.Component);
                        SetReferencedConfiguration(activePanel.Component, item.TargetConfiguration);
                        string after = SafeReferencedConfiguration(activePanel.Component);

                        AppliedChange applied = new AppliedChange();
                        applied.AssemblyConfiguration = item.AssemblyConfiguration;
                        applied.ComponentName = activePanel.ComponentName;
                        applied.PartPath = activePanel.PartPath;
                        applied.OriginalConfiguration = before;
                        applied.TargetConfiguration = item.TargetConfiguration;
                        lastAppliedChanges.Add(applied);

                        if (string.Equals(after, item.TargetConfiguration, StringComparison.OrdinalIgnoreCase))
                        {
                            item.Status = "APPLIED";
                            item.Message = "Changed from " + before + " to " + after + ".";
                            changed++;
                        }
                        else
                        {
                            item.Status = "ERROR";
                            item.Message = "SOLIDWORKS did not report the expected referenced configuration after apply. Current: " + after;
                            failed++;
                        }
                    }
                    catch (Exception ex)
                    {
                        item.Status = "ERROR";
                        item.Message = ex.Message;
                        failed++;
                    }
                }

                try { if (!string.IsNullOrWhiteSpace(originalActive)) assemblyModel.ShowConfiguration2(originalActive); } catch { }
                try { assemblyModel.ForceRebuild3(false); } catch { }

                BuildPreviewGrid();
                foreach (ChangePlanItem item in currentPlan)
                    AddPlanRow(item);

                string reportPath = WriteReport("WallPanelApply", BuildReportText(currentPlan));
                statusLabel.Text = "Apply complete. Changed: " + changed + ", skipped: " + skipped + ", failed: " + failed + ". Report: " + reportPath;
            }

            private void RollbackLast()
            {
                if (lastAppliedChanges.Count == 0)
                {
                    statusLabel.Text = "No previous applied direct changes to roll back.";
                    return;
                }

                string originalActive = SafeActiveConfigurationName(assemblyModel);
                int restored = 0;
                int failed = 0;

                foreach (AppliedChange change in lastAppliedChanges)
                {
                    try
                    {
                        assemblyModel.ShowConfiguration2(change.AssemblyConfiguration);
                        assemblyModel.ForceRebuild3(false);
                        IComponent2 component = FindComponentByNameAndPath(change.ComponentName, change.PartPath);
                        if (component == null)
                        {
                            failed++;
                            continue;
                        }
                        SetReferencedConfiguration(component, change.OriginalConfiguration);
                        restored++;
                    }
                    catch
                    {
                        failed++;
                    }
                }

                try { if (!string.IsNullOrWhiteSpace(originalActive)) assemblyModel.ShowConfiguration2(originalActive); } catch { }
                try { assemblyModel.ForceRebuild3(false); } catch { }
                lastAppliedChanges.Clear();
                statusLabel.Text = "Rollback complete. Restored: " + restored + ", failed: " + failed + ".";
                ScanPanels();
                BuildPanelGrid();
            }

            private PanelInstance FindActivePanelInstance(PanelInstance original)
            {
                if (original == null)
                    return null;
                IComponent2 component = FindComponentByNameAndPath(original.ComponentName, original.PartPath);
                if (component == null)
                    return null;
                return CreatePanelInstance(component, new List<IComponent2>());
            }

            private IComponent2 FindComponentByNameAndPath(string componentName, string partPath)
            {
                foreach (IComponent2 component in GetAllComponents())
                {
                    if (!string.Equals(SafeComponentName(component), componentName, StringComparison.OrdinalIgnoreCase))
                        continue;
                    IModelDoc2 model = null;
                    try { model = component.GetModelDoc2() as IModelDoc2; } catch { model = null; }
                    string modelPath = SafeModelPath(model);
                    if (string.IsNullOrWhiteSpace(partPath) || string.Equals(modelPath, partPath, StringComparison.OrdinalIgnoreCase))
                        return component;
                }
                return null;
            }

            private List<IComponent2> GetAllComponents()
            {
                List<IComponent2> result = new List<IComponent2>();
                if (assemblyDoc == null)
                    return result;

                object componentsObject = null;
                try { componentsObject = assemblyDoc.GetComponents(false); } catch { componentsObject = null; }
                Array componentsArray = componentsObject as Array;
                if (componentsArray == null)
                    return result;

                foreach (object componentObject in componentsArray)
                {
                    IComponent2 component = componentObject as IComponent2;
                    if (component != null)
                        result.Add(component);
                }
                return result;
            }
        }

        internal static List<string> GetConfigurationNames(IModelDoc2 modelDoc)
        {
            List<string> result = new List<string>();
            if (modelDoc == null)
                return result;
            object namesObject = null;
            try { namesObject = modelDoc.GetConfigurationNames(); } catch { namesObject = null; }
            Array namesArray = namesObject as Array;
            if (namesArray == null)
                return result;
            foreach (object nameObject in namesArray)
            {
                string name = nameObject == null ? string.Empty : Convert.ToString(nameObject);
                if (!string.IsNullOrWhiteSpace(name) && !ContainsIgnoreCase(result, name))
                    result.Add(name);
            }
            return result;
        }

        internal static string SafeActiveConfigurationName(IModelDoc2 modelDoc)
        {
            if (modelDoc == null)
                return string.Empty;
            try
            {
                IConfiguration config = modelDoc.GetActiveConfiguration() as IConfiguration;
                return config == null ? string.Empty : (config.Name ?? string.Empty);
            }
            catch { return string.Empty; }
        }

        internal static bool HasDesignTable(IModelDoc2 modelDoc)
        {
            if (modelDoc == null)
                return false;
            Feature feature = null;
            try { feature = modelDoc.FirstFeature() as Feature; } catch { feature = null; }
            while (feature != null)
            {
                string typeName = ReferenceMateAssistantCommand.SafeFeatureTypeName(feature);
                string featureName = ReferenceMateAssistantCommand.SafeFeatureName(feature);
                if (typeName.IndexOf("Design", StringComparison.OrdinalIgnoreCase) >= 0 && typeName.IndexOf("Table", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                if (featureName.IndexOf("Design Table", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                try { feature = feature.GetNextFeature() as Feature; } catch { feature = null; }
            }
            return false;
        }

        internal static bool IsSuppressed(IComponent2 component)
        {
            if (component == null)
                return true;
            try
            {
                object result = ((object)component).GetType().InvokeMember("IsSuppressed", BindingFlags.InvokeMethod, null, component, null);
                if (result is bool) return (bool)result;
            }
            catch { }
            return false;
        }

        internal static string SafeComponentName(IComponent2 component)
        {
            return ReferenceMateAssistantCommand.SafeComponentName(component);
        }

        internal static string SafeComponentPath(IComponent2 component)
        {
            if (component == null)
                return string.Empty;
            try { return component.GetPathName() ?? string.Empty; } catch { return string.Empty; }
        }

        internal static string SafeModelPath(IModelDoc2 modelDoc)
        {
            if (modelDoc == null)
                return string.Empty;
            try { return modelDoc.GetPathName() ?? string.Empty; } catch { return string.Empty; }
        }

        internal static string SafeReferencedConfiguration(IComponent2 component)
        {
            if (component == null)
                return string.Empty;
            try { return component.ReferencedConfiguration ?? string.Empty; } catch { return string.Empty; }
        }

        internal static void SetReferencedConfiguration(IComponent2 component, string configurationName)
        {
            if (component == null || string.IsNullOrWhiteSpace(configurationName))
                return;
            component.ReferencedConfiguration = configurationName;
        }

        internal static bool ContainsComponent(List<IComponent2> components, IComponent2 candidate)
        {
            return ReferenceMateAssistantCommand.ContainsComponent(components, candidate);
        }

        internal static bool ContainsIgnoreCase(IList<string> values, string value)
        {
            return ConfigurationPartNumberCommand.ContainsIgnoreCase(values, value);
        }

        internal static bool StringValueMatches(string a, string b)
        {
            string na = NormalizeNumber(a);
            string nb = NormalizeNumber(b);
            if (string.IsNullOrWhiteSpace(na) && string.IsNullOrWhiteSpace(nb))
                return true;
            return string.Equals(na, nb, StringComparison.OrdinalIgnoreCase);
        }

        internal static string NormalizeNumber(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            string text = value.Trim().Replace(',', '.');
            double number;
            if (double.TryParse(text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out number))
                return Math.Round(number, 3).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            return text;
        }

        internal static string NormalizeReinforcement(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            string text = value.Trim().Replace(" ", string.Empty).ToUpperInvariant();
            Match m = Regex.Match(text, @"S\d+");
            if (m.Success)
                return m.Value;
            return text;
        }

        internal static void ParseConfigurationName(string configName, ref string width, ref string length, ref string reinforcement)
        {
            if (string.IsNullOrWhiteSpace(configName))
                return;

            string text = configName.Trim();
            Match scheme = Regex.Match(text, @"(?<![A-Za-z0-9])S\s*\d+", RegexOptions.IgnoreCase);
            if (scheme.Success && string.IsNullOrWhiteSpace(reinforcement))
                reinforcement = NormalizeReinforcement(scheme.Value);

            Match size = Regex.Match(text, @"(?<w>\d+(?:[\.,]\d+)?)\s*[xX]\s*(?<l>\d+(?:[\.,]\d+)?)");
            if (size.Success)
            {
                if (string.IsNullOrWhiteSpace(width))
                    width = NormalizeNumber(size.Groups["w"].Value);
                if (string.IsNullOrWhiteSpace(length))
                    length = NormalizeNumber(size.Groups["l"].Value);
                return;
            }

            Match firstNumber = Regex.Match(text, @"(?<!\d)(\d{2,4})(?!\d)");
            if (firstNumber.Success && string.IsNullOrWhiteSpace(width))
                width = NormalizeNumber(firstNumber.Groups[1].Value);
        }

        internal static string BuildReportText(List<ChangePlanItem> plan)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Cabin Tools - Wall Panel Configuration Manager Report");
            sb.AppendLine("Created: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();
            sb.AppendLine("AssemblyConfiguration\tComponent\tOriginalConfiguration\tTargetConfiguration\tStatus\tMessage");
            foreach (ChangePlanItem item in plan)
            {
                sb.AppendLine(
                    (item.AssemblyConfiguration ?? string.Empty) + "\t" +
                    (item.Panel == null ? string.Empty : item.Panel.ComponentName) + "\t" +
                    (item.OriginalConfiguration ?? string.Empty) + "\t" +
                    (item.TargetConfiguration ?? string.Empty) + "\t" +
                    (item.Status ?? string.Empty) + "\t" +
                    (item.Message ?? string.Empty));
            }
            return sb.ToString();
        }

        internal static string WriteReport(string prefix, string content)
        {
            try
            {
                string root = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments), "CabinTools", "WallPanelReports");
                Directory.CreateDirectory(root);
                string path = Path.Combine(root, prefix + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
                File.WriteAllText(path, content ?? string.Empty, Encoding.UTF8);
                return path;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
