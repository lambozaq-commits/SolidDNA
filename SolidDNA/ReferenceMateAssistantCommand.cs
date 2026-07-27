using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace SolidDNA
{
    /// <summary>
    /// Reference Mate Assistant.
    ///
    /// Workflow:
    /// - Open the assistant from an assembly.
    /// - Choose Component 1 and optionally Component 2 from the dropdowns, or select components in SOLIDWORKS and use the selection button.
    /// - Choose references from dropdowns or select geometry directly in SOLIDWORKS and press Use selected.
    /// - Create the mate.
    ///
    /// The form is modeless so the user can keep selecting geometry in SOLIDWORKS while the assistant remains open.
    /// </summary>
    internal static class ReferenceMateAssistantCommand
    {
        private static readonly List<Form> OpenForms = new List<Form>();

        public static void ShowReferenceMateAssistantForm()
        {
            try
            {
                IModelDoc2 modelDoc = CabinCustomPropertyStore.GetActiveModelDocument();

                if (modelDoc == null)
                {
                    ShowMessage("Open an assembly before running Reference Mate Assistant.", MessageBoxIcon.Warning);
                    return;
                }

                if (modelDoc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
                {
                    ShowMessage("Reference Mate Assistant works only with an active assembly document.", MessageBoxIcon.Warning);
                    return;
                }

                ReferenceMateAssistantForm form = new ReferenceMateAssistantForm(modelDoc);
                OpenForms.Add(form);
                form.FormClosed += delegate
                {
                    OpenForms.Remove(form);
                    try { form.Dispose(); } catch { }
                };
                form.Show();
            }
            catch (Exception ex)
            {
                ShowMessage("Cabin Tools could not open Reference Mate Assistant.\r\n\r\n" + ex.Message, MessageBoxIcon.Error);
            }
        }

        private static void ShowMessage(string message, MessageBoxIcon icon)
        {
            MessageBox.Show(message, "Cabin Tools - Reference Mate Assistant", MessageBoxButtons.OK, icon);
        }

        internal enum ReferenceKind
        {
            Unknown,
            Plane,
            Axis,
            Point,
            Sketch,
            SketchLine,
            SketchArc,
            SketchPoint,
            Face,
            Edge,
            Vertex
        }

        internal sealed class ReferenceDescriptor
        {
            public string DisplayName = string.Empty;
            public string Category = string.Empty;
            public ReferenceKind Kind = ReferenceKind.Unknown;
            public object Selectable;
            public Feature Feature;
            public object SketchObject;
            public IComponent2 OwnerComponent;
            public IModelDoc2 OwnerDocument;
            public bool FromDirectSelection;

            public override string ToString()
            {
                return DisplayName;
            }
        }

        internal sealed class ComponentDescriptor
        {
            public IComponent2 Component;
            public string InstanceName = string.Empty;
            public string FileName = string.Empty;
            public string Description = string.Empty;

            public override string ToString()
            {
                if (string.IsNullOrWhiteSpace(Description))
                    return InstanceName;
                return InstanceName + " — " + Description;
            }
        }

        private sealed class ReferenceMateAssistantForm : Form
        {
            private readonly IModelDoc2 assemblyModel;
            private readonly IAssemblyDoc assemblyDoc;
            private readonly List<ComponentDescriptor> componentChoices = new List<ComponentDescriptor>();
            private readonly List<ReferenceDescriptor> component1References = new List<ReferenceDescriptor>();
            private readonly List<ReferenceDescriptor> component2References = new List<ReferenceDescriptor>();
            private readonly List<ReferenceDescriptor> assemblyReferences = new List<ReferenceDescriptor>();

            private IComponent2 component1;
            private IComponent2 component2;

            private ComboBox component1Box;
            private ComboBox component2Box;
            private ComboBox component1ReferenceBox;
            private ComboBox component2ReferenceBox;
            private ComboBox assemblyReferenceBox;
            private ComboBox mateTypeBox;
            private TextBox distanceTextBox;
            private TextBox angleTextBox;
            private CheckBox flipCheckBox;
            private CheckBox lockRotationCheckBox;
            private Label statusLabel;

            private bool loadingControls;

            public ReferenceMateAssistantForm(IModelDoc2 assemblyModel)
            {
                this.assemblyModel = assemblyModel;
                this.assemblyDoc = assemblyModel as IAssemblyDoc;

                Text = "Cabin Tools - Reference Mate Assistant";
                Width = 1180;
                Height = 640;
                StartPosition = FormStartPosition.CenterScreen;
                Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

                BuildLayout();
                ScanComponents();
                LoadComponentsFromSolidWorksSelection(false);
                RefreshReferenceLists();
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
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

                Label heading = new Label();
                heading.Text = "Reference Mate Assistant";
                heading.Font = new Font(Font.FontFamily, 12F, FontStyle.Bold, GraphicsUnit.Point);
                heading.AutoSize = true;
                heading.Margin = new Padding(0, 0, 0, 10);
                root.Controls.Add(heading, 0, 0);

                GroupBox componentGroup = new GroupBox();
                componentGroup.Text = "1 - Components";
                componentGroup.Dock = DockStyle.Top;
                componentGroup.AutoSize = true;
                componentGroup.Padding = new Padding(10);

                TableLayoutPanel compLayout = new TableLayoutPanel();
                compLayout.Dock = DockStyle.Fill;
                compLayout.AutoSize = true;
                compLayout.ColumnCount = 6;
                compLayout.RowCount = 2;
                compLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                compLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
                compLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                compLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                compLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                compLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

                compLayout.Controls.Add(CreateLabel("Component 1:"), 0, 0);
                component1Box = new ComboBox();
                component1Box.DropDownStyle = ComboBoxStyle.DropDownList;
                component1Box.Dock = DockStyle.Fill;
                component1Box.SelectedIndexChanged += delegate { ComponentComboChanged(); };
                compLayout.Controls.Add(component1Box, 1, 0);
                compLayout.Controls.Add(CreateButton("Show", delegate { ShowComponent(component1); }, 80), 2, 0);
                compLayout.Controls.Add(CreateButton("Use selected", delegate { UseSelectedComponentFor(1); }, 112), 3, 0);

                compLayout.Controls.Add(CreateLabel("Component 2:"), 0, 1);
                component2Box = new ComboBox();
                component2Box.DropDownStyle = ComboBoxStyle.DropDownList;
                component2Box.Dock = DockStyle.Fill;
                component2Box.SelectedIndexChanged += delegate { ComponentComboChanged(); };
                compLayout.Controls.Add(component2Box, 1, 1);
                compLayout.Controls.Add(CreateButton("Show", delegate { ShowComponent(component2); }, 80), 2, 1);
                compLayout.Controls.Add(CreateButton("Use selected", delegate { UseSelectedComponentFor(2); }, 112), 3, 1);

                Button useSwSelection = CreateButton("Use SOLIDWORKS selection", delegate { LoadComponentsFromSolidWorksSelection(true); RefreshReferenceLists(); }, 190);
                compLayout.SetRowSpan(useSwSelection, 2);
                compLayout.Controls.Add(useSwSelection, 4, 0);

                Button rescanComponents = CreateButton("Rescan", delegate { ScanComponents(); RefreshReferenceLists(); }, 90);
                compLayout.SetRowSpan(rescanComponents, 2);
                compLayout.Controls.Add(rescanComponents, 5, 0);

                componentGroup.Controls.Add(compLayout);
                root.Controls.Add(componentGroup, 0, 1);

                GroupBox referenceGroup = new GroupBox();
                referenceGroup.Text = "2 - References";
                referenceGroup.Dock = DockStyle.Top;
                referenceGroup.AutoSize = true;
                referenceGroup.Padding = new Padding(10);

                TableLayoutPanel refLayout = new TableLayoutPanel();
                refLayout.Dock = DockStyle.Fill;
                refLayout.AutoSize = true;
                refLayout.ColumnCount = 4;
                refLayout.RowCount = 3;
                refLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                refLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                refLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                refLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

                refLayout.Controls.Add(CreateLabel("Component 1 reference:"), 0, 0);
                component1ReferenceBox = CreateReferenceBox();
                refLayout.Controls.Add(component1ReferenceBox, 1, 0);
                refLayout.Controls.Add(CreateButton("Show", delegate { HighlightSelected(component1ReferenceBox); }, 72), 2, 0);
                refLayout.Controls.Add(CreateButton("Use selected", delegate { UseSelectedReferenceFor(component1ReferenceBox, component1References, "Component 1"); }, 106), 3, 0);

                refLayout.Controls.Add(CreateLabel("Component 2 reference:"), 0, 1);
                component2ReferenceBox = CreateReferenceBox();
                refLayout.Controls.Add(component2ReferenceBox, 1, 1);
                refLayout.Controls.Add(CreateButton("Show", delegate { HighlightSelected(component2ReferenceBox); }, 72), 2, 1);
                refLayout.Controls.Add(CreateButton("Use selected", delegate { UseSelectedReferenceFor(component2ReferenceBox, component2References, "Component 2"); }, 106), 3, 1);

                refLayout.Controls.Add(CreateLabel("Assembly / selected sketch reference:"), 0, 2);
                assemblyReferenceBox = CreateReferenceBox();
                refLayout.Controls.Add(assemblyReferenceBox, 1, 2);
                refLayout.Controls.Add(CreateButton("Show", delegate { HighlightSelected(assemblyReferenceBox); }, 72), 2, 2);
                refLayout.Controls.Add(CreateButton("Use selected", delegate { UseSelectedReferenceFor(assemblyReferenceBox, assemblyReferences, "Assembly"); }, 106), 3, 2);

                referenceGroup.Controls.Add(refLayout);
                root.Controls.Add(referenceGroup, 0, 2);

                GroupBox mateGroup = new GroupBox();
                mateGroup.Text = "3 - Mate";
                mateGroup.Dock = DockStyle.Top;
                mateGroup.AutoSize = true;
                mateGroup.Padding = new Padding(10);

                FlowLayoutPanel mateLayout = new FlowLayoutPanel();
                mateLayout.Dock = DockStyle.Top;
                mateLayout.AutoSize = true;
                mateLayout.WrapContents = true;

                mateLayout.Controls.Add(CreateLabel("Mate type:"));
                mateTypeBox = new ComboBox();
                mateTypeBox.Width = 160;
                mateTypeBox.DropDownStyle = ComboBoxStyle.DropDownList;
                mateTypeBox.Items.AddRange(new object[] { "Coincident", "Parallel", "Perpendicular", "Distance", "Angle", "Symmetry" });
                mateTypeBox.SelectedIndex = 0;
                mateTypeBox.SelectedIndexChanged += delegate { UpdateSymmetryStatus(); };
                mateLayout.Controls.Add(mateTypeBox);

                mateLayout.Controls.Add(CreateLabel("Distance mm:"));
                distanceTextBox = new TextBox();
                distanceTextBox.Width = 80;
                distanceTextBox.Text = "0";
                mateLayout.Controls.Add(distanceTextBox);

                mateLayout.Controls.Add(CreateLabel("Angle deg:"));
                angleTextBox = new TextBox();
                angleTextBox.Width = 80;
                angleTextBox.Text = "0";
                mateLayout.Controls.Add(angleTextBox);

                flipCheckBox = new CheckBox();
                flipCheckBox.Text = "Flip";
                flipCheckBox.AutoSize = true;
                mateLayout.Controls.Add(flipCheckBox);

                lockRotationCheckBox = new CheckBox();
                lockRotationCheckBox.Text = "Lock rotation";
                lockRotationCheckBox.AutoSize = true;
                mateLayout.Controls.Add(lockRotationCheckBox);

                mateLayout.Controls.Add(CreateButton("Create mate", delegate { CreateMate(); }, 120));
                mateLayout.Controls.Add(CreateButton("Close", delegate { Close(); }, 90));

                mateGroup.Controls.Add(mateLayout);
                root.Controls.Add(mateGroup, 0, 3);

                statusLabel = new Label();
                statusLabel.Dock = DockStyle.Fill;
                statusLabel.BorderStyle = BorderStyle.FixedSingle;
                statusLabel.Padding = new Padding(8);
                statusLabel.Font = new Font(Font.FontFamily, 10F, FontStyle.Bold, GraphicsUnit.Point);
                statusLabel.Text = "Ready";
                root.Controls.Add(statusLabel, 0, 4);

                Controls.Add(root);
            }

            private Label CreateLabel(string text)
            {
                Label label = new Label();
                label.Text = text;
                label.AutoSize = true;
                label.Anchor = AnchorStyles.Left;
                label.Margin = new Padding(0, 4, 8, 4);
                return label;
            }

            private Button CreateButton(string text, EventHandler clickHandler, int width)
            {
                Button button = new Button();
                button.Text = text;
                button.Width = width;
                button.Height = 28;
                button.Margin = new Padding(4, 2, 4, 2);
                button.Click += clickHandler;
                return button;
            }

            private ComboBox CreateReferenceBox()
            {
                ComboBox box = new ComboBox();
                box.DropDownStyle = ComboBoxStyle.DropDownList;
                box.Dock = DockStyle.Fill;
                return box;
            }

            private void ScanComponents()
            {
                componentChoices.Clear();

                object componentsObject = null;
                try { componentsObject = assemblyDoc.GetComponents(false); } catch { componentsObject = null; }
                object[] componentObjects = componentsObject as object[];
                if (componentObjects != null)
                {
                    foreach (object componentObject in componentObjects)
                    {
                        IComponent2 component = componentObject as IComponent2;
                        if (component == null)
                            continue;

                        ComponentDescriptor descriptor = new ComponentDescriptor();
                        descriptor.Component = component;
                        descriptor.InstanceName = SafeComponentName(component);
                        string path = SafeComponentPath(component);
                        descriptor.FileName = string.IsNullOrWhiteSpace(path) ? descriptor.InstanceName : Path.GetFileName(path);
                        descriptor.Description = ReadComponentDescription(component);
                        if (string.IsNullOrWhiteSpace(descriptor.Description))
                            descriptor.Description = descriptor.FileName;
                        componentChoices.Add(descriptor);
                    }
                }

                componentChoices.Sort(CompareComponentDescriptors);
                FillComponentBoxes();
            }

            private static int CompareComponentDescriptors(ComponentDescriptor a, ComponentDescriptor b)
            {
                int byFile = string.Compare(a.FileName, b.FileName, StringComparison.OrdinalIgnoreCase);
                if (byFile != 0)
                    return byFile;
                return string.Compare(a.InstanceName, b.InstanceName, StringComparison.OrdinalIgnoreCase);
            }

            private void FillComponentBoxes()
            {
                loadingControls = true;
                try
                {
                    component1Box.Items.Clear();
                    component2Box.Items.Clear();
                    component2Box.Items.Add("<None - use assembly / selected sketch reference>");

                    foreach (ComponentDescriptor descriptor in componentChoices)
                    {
                        component1Box.Items.Add(descriptor);
                        component2Box.Items.Add(descriptor);
                    }

                    if (component1Box.Items.Count > 0 && component1Box.SelectedIndex < 0)
                        component1Box.SelectedIndex = 0;
                    if (component2Box.SelectedIndex < 0)
                        component2Box.SelectedIndex = 0;
                }
                finally
                {
                    loadingControls = false;
                }

                ComponentComboChanged();
            }

            private void ComponentComboChanged()
            {
                if (loadingControls)
                    return;

                ComponentDescriptor c1 = component1Box.SelectedItem as ComponentDescriptor;
                component1 = c1 == null ? null : c1.Component;

                ComponentDescriptor c2 = component2Box.SelectedItem as ComponentDescriptor;
                component2 = c2 == null ? null : c2.Component;

                RefreshReferenceLists();
            }

            private void LoadComponentsFromSolidWorksSelection(bool showMessage)
            {
                List<IComponent2> selected = GetSelectedComponents(assemblyModel);
                if (selected.Count == 0)
                {
                    if (showMessage)
                        statusLabel.Text = "Select one or two components in SOLIDWORKS, then click Use SOLIDWORKS selection. Use the row-specific Use selected buttons when setting only Component 1 or Component 2.";
                    return;
                }

                loadingControls = true;
                try
                {
                    SelectComponentInCombo(component1Box, selected[0], false);
                    if (selected.Count >= 2)
                    {
                        SelectComponentInCombo(component2Box, selected[1], true);
                        statusLabel.Text = "Component 1 and Component 2 loaded from SOLIDWORKS selection.";
                    }
                    else if (showMessage)
                    {
                        statusLabel.Text = "One component loaded as Component 1. To load Component 2 separately, select it in SOLIDWORKS and click Use selected on the Component 2 row.";
                    }
                }
                finally
                {
                    loadingControls = false;
                }

                ComponentComboChanged();
            }

            private void UseSelectedComponentFor(int componentSlot)
            {
                List<IComponent2> selected = GetSelectedComponents(assemblyModel);
                if (selected.Count == 0)
                {
                    statusLabel.Text = "Select a component in SOLIDWORKS first, then click Use selected on Component 1 or Component 2.";
                    return;
                }

                IComponent2 selectedComponent = selected[0];
                loadingControls = true;
                try
                {
                    if (componentSlot == 1)
                    {
                        SelectComponentInCombo(component1Box, selectedComponent, false);
                        statusLabel.Text = "Loaded Component 1 from SOLIDWORKS selection.";
                    }
                    else
                    {
                        SelectComponentInCombo(component2Box, selectedComponent, true);
                        statusLabel.Text = "Loaded Component 2 from SOLIDWORKS selection.";
                    }
                }
                finally
                {
                    loadingControls = false;
                }

                ComponentComboChanged();
            }

            private void SelectComponentInCombo(ComboBox box, IComponent2 component, bool component2BoxMode)
            {
                if (component == null || box == null)
                    return;

                EnsureComponentInChoices(component);

                for (int i = 0; i < box.Items.Count; i++)
                {
                    ComponentDescriptor descriptor = box.Items[i] as ComponentDescriptor;
                    if (descriptor == null)
                        continue;

                    if (ComponentsMatch(descriptor.Component, component))
                    {
                        box.SelectedIndex = i;
                        return;
                    }
                }

                FillComponentBoxes();

                for (int i = 0; i < box.Items.Count; i++)
                {
                    ComponentDescriptor descriptor = box.Items[i] as ComponentDescriptor;
                    if (descriptor == null)
                        continue;

                    if (ComponentsMatch(descriptor.Component, component))
                    {
                        box.SelectedIndex = i;
                        return;
                    }
                }

                if (component2BoxMode && box.Items.Count > 0)
                    box.SelectedIndex = 0;
            }

            private void EnsureComponentInChoices(IComponent2 component)
            {
                if (component == null)
                    return;

                foreach (ComponentDescriptor existing in componentChoices)
                {
                    if (ComponentsMatch(existing.Component, component))
                        return;
                }

                ComponentDescriptor descriptor = new ComponentDescriptor();
                descriptor.Component = component;
                descriptor.InstanceName = SafeComponentName(component);
                string path = SafeComponentPath(component);
                descriptor.FileName = string.IsNullOrWhiteSpace(path) ? descriptor.InstanceName : Path.GetFileName(path);
                descriptor.Description = ReadComponentDescription(component);
                if (string.IsNullOrWhiteSpace(descriptor.Description))
                    descriptor.Description = descriptor.FileName;

                componentChoices.Add(descriptor);
                componentChoices.Sort(CompareComponentDescriptors);
            }

            private void RefreshReferenceLists()
            {
                component1References.Clear();
                component2References.Clear();
                assemblyReferences.Clear();

                if (component1 != null)
                    ScanComponentReferences(component1, component1References, "Component 1");
                if (component2 != null)
                    ScanComponentReferences(component2, component2References, "Component 2");
                ScanAssemblyReferences(assemblyModel, assemblyReferences);

                FillCombo(component1ReferenceBox, component1References);
                FillCombo(component2ReferenceBox, component2References);
                FillCombo(assemblyReferenceBox, assemblyReferences);

                statusLabel.Text = "Loaded " + componentChoices.Count + " component instance(s), " + component1References.Count + " component-1 reference(s), " + component2References.Count + " component-2 reference(s), and " + assemblyReferences.Count + " assembly reference(s). Component reference dropdowns load resolved component reference planes/geometry automatically. Sketch lines can still be selected directly in SOLIDWORKS and loaded with Use selected.";
            }

            private static void FillCombo(ComboBox combo, List<ReferenceDescriptor> refs)
            {
                combo.Items.Clear();
                foreach (ReferenceDescriptor reference in refs)
                    combo.Items.Add(reference);
                if (combo.Items.Count > 0)
                    combo.SelectedIndex = 0;
            }

            private void ShowComponent(IComponent2 component)
            {
                if (component == null)
                {
                    statusLabel.Text = "No component selected to show.";
                    return;
                }

                ClearSelection();
                try
                {
                    component.Select4(false, null, false);
                    assemblyModel.ViewZoomToSelection();
                    statusLabel.Text = "Selected in SOLIDWORKS: " + SafeComponentName(component);
                }
                catch
                {
                    statusLabel.Text = "Could not show selected component.";
                }
            }

            private void UpdateSymmetryStatus()
            {
                string mateType = Convert.ToString(mateTypeBox.SelectedItem);
                if (string.Equals(mateType, "Symmetry", StringComparison.OrdinalIgnoreCase))
                    statusLabel.Text = "Symmetry requires Component 1 reference, Component 2 reference, and one assembly plane or selected sketch line/plane as the symmetry reference.";
            }

            private void HighlightSelected(ComboBox combo)
            {
                ReferenceDescriptor reference = combo.SelectedItem as ReferenceDescriptor;
                if (reference == null)
                {
                    statusLabel.Text = "No reference selected.";
                    return;
                }

                ClearSelection();
                SelectReference(reference, false);
                try { assemblyModel.ViewZoomToSelection(); } catch { }
                statusLabel.Text = "Selected in SOLIDWORKS: " + reference.DisplayName;
            }

            private void UseSelectedReferenceFor(ComboBox combo, List<ReferenceDescriptor> referenceList, string targetName)
            {
                ReferenceDescriptor selectedReference = GetFirstSelectedReferenceFromSolidWorks();
                if (selectedReference == null)
                {
                    statusLabel.Text = "Select a plane, face, edge, sketch line, point, or other mate reference in SOLIDWORKS first, then click Use selected.";
                    return;
                }

                if (string.Equals(targetName, "Component 1", StringComparison.OrdinalIgnoreCase) && selectedReference.OwnerComponent != null)
                {
                    SelectComponentInCombo(component1Box, selectedReference.OwnerComponent, false);
                    component1 = selectedReference.OwnerComponent;
                    component1References.Clear();
                    ScanComponentReferences(component1, component1References, "Component 1");
                    FillCombo(component1ReferenceBox, component1References);
                    referenceList = component1References;
                    combo = component1ReferenceBox;
                }
                else if (string.Equals(targetName, "Component 2", StringComparison.OrdinalIgnoreCase) && selectedReference.OwnerComponent != null)
                {
                    SelectComponentInCombo(component2Box, selectedReference.OwnerComponent, true);
                    component2 = selectedReference.OwnerComponent;
                    component2References.Clear();
                    ScanComponentReferences(component2, component2References, "Component 2");
                    FillCombo(component2ReferenceBox, component2References);
                    referenceList = component2References;
                    combo = component2ReferenceBox;
                }

                selectedReference.DisplayName = targetName + " selected - " + selectedReference.DisplayName;
                selectedReference.FromDirectSelection = true;
                referenceList.Add(selectedReference);
                combo.Items.Add(selectedReference);
                combo.SelectedItem = selectedReference;
                statusLabel.Text = "Loaded selected reference: " + selectedReference.DisplayName;
            }

            private ReferenceDescriptor GetFirstSelectedReferenceFromSolidWorks()
            {
                ISelectionMgr selectionManager = null;
                try { selectionManager = assemblyModel.SelectionManager as ISelectionMgr; } catch { selectionManager = null; }
                if (selectionManager == null)
                    return null;

                int count = 0;
                try { count = selectionManager.GetSelectedObjectCount2(-1); } catch { count = 0; }

                for (int i = 1; i <= count; i++)
                {
                    object selectedObject = null;
                    try { selectedObject = selectionManager.GetSelectedObject6(i, -1); } catch { selectedObject = null; }
                    if (selectedObject == null)
                        continue;

                    if (selectedObject is IComponent2)
                        continue;

                    IComponent2 ownerComponent = null;
                    try { ownerComponent = selectionManager.GetSelectedObjectsComponent4(i, -1) as IComponent2; } catch { ownerComponent = null; }

                    int selectType = 0;
                    try { selectType = selectionManager.GetSelectedObjectType3(i, -1); } catch { selectType = 0; }

                    string typeName = string.Empty;
                    try { typeName = Enum.GetName(typeof(swSelectType_e), selectType) ?? selectType.ToString(); } catch { typeName = selectType.ToString(); }

                    return new ReferenceDescriptor
                    {
                        DisplayName = typeName,
                        Category = "Selected in SOLIDWORKS",
                        Kind = GuessReferenceKind(typeName),
                        Selectable = selectedObject,
                        SketchObject = selectedObject,
                        OwnerComponent = ownerComponent,
                        OwnerDocument = assemblyModel,
                        FromDirectSelection = true
                    };
                }

                return null;
            }

            private static ReferenceKind GuessReferenceKind(string selectTypeName)
            {
                if (string.IsNullOrWhiteSpace(selectTypeName))
                    return ReferenceKind.Unknown;
                if (selectTypeName.IndexOf("DATUMPLANE", StringComparison.OrdinalIgnoreCase) >= 0)
                    return ReferenceKind.Plane;
                if (selectTypeName.IndexOf("FACE", StringComparison.OrdinalIgnoreCase) >= 0)
                    return ReferenceKind.Face;
                if (selectTypeName.IndexOf("AXIS", StringComparison.OrdinalIgnoreCase) >= 0)
                    return ReferenceKind.Axis;
                if (selectTypeName.IndexOf("POINT", StringComparison.OrdinalIgnoreCase) >= 0)
                    return ReferenceKind.SketchPoint;
                if (selectTypeName.IndexOf("VERTEX", StringComparison.OrdinalIgnoreCase) >= 0)
                    return ReferenceKind.Vertex;
                if (selectTypeName.IndexOf("SKETCHSEG", StringComparison.OrdinalIgnoreCase) >= 0)
                    return ReferenceKind.SketchLine;
                if (selectTypeName.IndexOf("EDGE", StringComparison.OrdinalIgnoreCase) >= 0)
                    return ReferenceKind.Edge;
                return ReferenceKind.Unknown;
            }

            private void CreateMate()
            {
                string writeBlockReason = CabinCustomPropertyStore.GetWriteBlockReason(assemblyModel);
                if (!string.IsNullOrWhiteSpace(writeBlockReason))
                {
                    ShowMessage(writeBlockReason, MessageBoxIcon.Warning);
                    return;
                }

                if (!SelectMateReferences())
                {
                    statusLabel.Text = "Mate creation cancelled: select the required references first.";
                    return;
                }

                int errorCode;
                object mateObject;
                if (TryCreateMate(out errorCode, out mateObject))
                {
                    try { assemblyModel.ForceRebuild3(false); } catch { }
                    statusLabel.Text = "Mate created. SOLIDWORKS error status: " + errorCode.ToString();
                    ClearSelection();
                }
                else
                {
                    statusLabel.Text = "SOLIDWORKS did not create the mate. Error status: " + errorCode.ToString() + ". Check entity compatibility.";
                }
            }

            private bool SelectMateReferences()
            {
                ReferenceDescriptor comp1Ref = component1ReferenceBox.SelectedItem as ReferenceDescriptor;
                ReferenceDescriptor comp2Ref = component2ReferenceBox.SelectedItem as ReferenceDescriptor;
                ReferenceDescriptor asmRef = assemblyReferenceBox.SelectedItem as ReferenceDescriptor;
                string mateType = Convert.ToString(mateTypeBox.SelectedItem);

                if (comp1Ref == null)
                    return false;

                ClearSelection();
                bool okA = SelectReference(comp1Ref, false);

                if (string.Equals(mateType, "Symmetry", StringComparison.OrdinalIgnoreCase))
                {
                    if (comp2Ref == null || asmRef == null)
                        return false;
                    return okA && SelectReference(comp2Ref, true) && SelectReference(asmRef, true);
                }

                if (comp2Ref != null)
                    return okA && SelectReference(comp2Ref, true);

                if (asmRef == null)
                    return false;

                return okA && SelectReference(asmRef, true);
            }

            private bool TryCreateMate(out int errorCode, out object mateObject)
            {
                errorCode = 0;
                mateObject = null;

                if (assemblyDoc == null)
                    return false;

                string mateTypeName = Convert.ToString(mateTypeBox.SelectedItem);
                int mateType = GetMateType(mateTypeName);
                int align = flipCheckBox.Checked ? (int)swMateAlign_e.swMateAlignANTI_ALIGNED : (int)swMateAlign_e.swMateAlignALIGNED;

                double distanceMeters = ParseDouble(distanceTextBox.Text) / 1000.0;
                double angleRadians = ParseDouble(angleTextBox.Text) * Math.PI / 180.0;

                object[] addMate5Arguments = new object[]
                {
                    mateType,
                    align,
                    flipCheckBox.Checked,
                    distanceMeters,
                    0.0,
                    0.0,
                    1.0,
                    1.0,
                    angleRadians,
                    0.0,
                    0.0,
                    false,
                    lockRotationCheckBox.Checked,
                    0,
                    0.0,
                    errorCode
                };

                if (TryInvokeMateMethod("AddMate5", addMate5Arguments, out mateObject, out errorCode))
                    return mateObject != null && errorCode == 0;

                object[] addMate3Arguments = new object[]
                {
                    mateType,
                    align,
                    flipCheckBox.Checked,
                    distanceMeters,
                    0.0,
                    0.0,
                    1.0,
                    1.0,
                    angleRadians,
                    0.0,
                    0.0,
                    false,
                    errorCode
                };

                if (TryInvokeMateMethod("AddMate3", addMate3Arguments, out mateObject, out errorCode))
                    return mateObject != null && errorCode == 0;

                return false;
            }

            private bool TryInvokeMateMethod(string methodName, object[] preferredArguments, out object mateObject, out int errorCode)
            {
                mateObject = null;
                errorCode = -1;

                try
                {
                    object rawAssembly = assemblyDoc;
                    MethodInfo[] methods = rawAssembly.GetType().GetMethods();
                    foreach (MethodInfo method in methods)
                    {
                        if (!string.Equals(method.Name, methodName, StringComparison.OrdinalIgnoreCase))
                            continue;

                        ParameterInfo[] parameters = method.GetParameters();
                        if (parameters.Length <= 0)
                            continue;

                        object[] args = BuildMateArgumentsForParameters(parameters, preferredArguments);
                        try
                        {
                            mateObject = method.Invoke(rawAssembly, args);
                            errorCode = ExtractErrorStatus(parameters, args);
                            return true;
                        }
                        catch
                        {
                        }
                    }

                    try
                    {
                        object[] args = preferredArguments;
                        mateObject = rawAssembly.GetType().InvokeMember(methodName, BindingFlags.InvokeMethod, null, rawAssembly, args);
                        errorCode = ExtractLastInt(args, 0);
                        return true;
                    }
                    catch
                    {
                        return false;
                    }
                }
                catch
                {
                    return false;
                }
            }

            private static object[] BuildMateArgumentsForParameters(ParameterInfo[] parameters, object[] preferredArguments)
            {
                object[] result = new object[parameters.Length];
                for (int i = 0; i < result.Length; i++)
                {
                    if (i < preferredArguments.Length)
                    {
                        result[i] = preferredArguments[i];
                        continue;
                    }

                    Type parameterType = parameters[i].ParameterType;
                    Type realType = parameterType.IsByRef ? parameterType.GetElementType() : parameterType;
                    if (realType == typeof(int)) result[i] = 0;
                    else if (realType == typeof(bool)) result[i] = false;
                    else if (realType == typeof(double)) result[i] = 0.0;
                    else if (realType == typeof(string)) result[i] = string.Empty;
                    else result[i] = null;
                }

                return result;
            }

            private static int ExtractErrorStatus(ParameterInfo[] parameters, object[] args)
            {
                if (parameters == null || args == null)
                    return 0;

                for (int i = parameters.Length - 1; i >= 0; i--)
                {
                    string name = parameters[i].Name ?? string.Empty;
                    if (name.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("status", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        try { return Convert.ToInt32(args[i]); } catch { }
                    }
                }

                return ExtractLastInt(args, 0);
            }

            private static int ExtractLastInt(object[] args, int fallback)
            {
                if (args == null)
                    return fallback;

                for (int i = args.Length - 1; i >= 0; i--)
                {
                    if (args[i] is int)
                        return (int)args[i];
                }

                return fallback;
            }

            private int GetMateType(string mateTypeName)
            {
                if (string.Equals(mateTypeName, "Parallel", StringComparison.OrdinalIgnoreCase))
                    return (int)swMateType_e.swMatePARALLEL;
                if (string.Equals(mateTypeName, "Perpendicular", StringComparison.OrdinalIgnoreCase))
                    return (int)swMateType_e.swMatePERPENDICULAR;
                if (string.Equals(mateTypeName, "Distance", StringComparison.OrdinalIgnoreCase))
                    return (int)swMateType_e.swMateDISTANCE;
                if (string.Equals(mateTypeName, "Angle", StringComparison.OrdinalIgnoreCase))
                    return (int)swMateType_e.swMateANGLE;
                if (string.Equals(mateTypeName, "Symmetry", StringComparison.OrdinalIgnoreCase))
                    return (int)swMateType_e.swMateSYMMETRIC;
                return (int)swMateType_e.swMateCOINCIDENT;
            }

            private static double ParseDouble(string text)
            {
                if (string.IsNullOrWhiteSpace(text))
                    return 0.0;

                double value;
                if (double.TryParse(text.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value))
                    return value;

                return 0.0;
            }

            private void ClearSelection()
            {
                try { assemblyModel.ClearSelection2(true); } catch { }
            }

            private bool SelectReference(ReferenceDescriptor reference, bool append)
            {
                if (reference == null)
                    return false;

                object selectable = reference.Selectable ?? reference.SketchObject ?? reference.Feature;
                if (selectable == null)
                    return false;

                if (selectable is Feature)
                {
                    try { return ((Feature)selectable).Select2(append, -1); }
                    catch { }
                }

                try
                {
                    object result = selectable.GetType().InvokeMember("Select", BindingFlags.InvokeMethod, null, selectable, new object[] { append });
                    return result is bool ? (bool)result : true;
                }
                catch
                {
                }

                try
                {
                    object result = selectable.GetType().InvokeMember("Select4", BindingFlags.InvokeMethod, null, selectable, new object[] { append, null });
                    return result is bool ? (bool)result : true;
                }
                catch
                {
                    return false;
                }
            }
        }

        internal static List<IComponent2> GetSelectedComponents(IModelDoc2 modelDoc)
        {
            List<IComponent2> result = new List<IComponent2>();
            if (modelDoc == null)
                return result;

            ISelectionMgr selectionManager = null;
            try { selectionManager = modelDoc.SelectionManager as ISelectionMgr; } catch { selectionManager = null; }
            if (selectionManager == null)
                return result;

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

                if (component != null && !ContainsComponent(result, component))
                    result.Add(component);
            }

            return result;
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

        internal static void ScanComponentReferences(IComponent2 component, List<ReferenceDescriptor> output, string labelPrefix)
        {
            if (component == null || output == null)
                return;

            IModelDoc2 componentModel = null;
            try { componentModel = component.GetModelDoc2() as IModelDoc2; } catch { componentModel = null; }

            if (componentModel == null)
            {
                TryResolveComponent(component);
                try { componentModel = component.GetModelDoc2() as IModelDoc2; } catch { componentModel = null; }
            }

            if (componentModel == null)
                return;

            Feature root = null;
            try { root = componentModel.FirstFeature() as Feature; } catch { root = null; }
            TraverseReferenceFeatures(root, false, output, component, componentModel, labelPrefix, false);
        }

        internal static void ScanAssemblyReferences(IModelDoc2 assemblyModel, List<ReferenceDescriptor> output)
        {
            if (assemblyModel == null || output == null)
                return;

            List<ReferenceDescriptor> scanned = new List<ReferenceDescriptor>();
            Feature root = null;
            try { root = assemblyModel.FirstFeature() as Feature; } catch { root = null; }
            TraverseReferenceFeatures(root, false, scanned, null, assemblyModel, "Assembly", false);

            foreach (ReferenceDescriptor reference in scanned)
            {
                if (reference == null)
                    continue;
                if (reference.Kind == ReferenceKind.Plane || IsOriginReference(reference))
                    output.Add(reference);
            }
        }

        private static bool IsOriginReference(ReferenceDescriptor reference)
        {
            if (reference == null || string.IsNullOrWhiteSpace(reference.DisplayName))
                return false;
            return reference.DisplayName.IndexOf("Origin", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void TraverseReferenceFeatures(Feature feature, bool subFeature, List<ReferenceDescriptor> output, IComponent2 ownerComponent, IModelDoc2 ownerDocument, string labelPrefix, bool includeSketchGeometry)
        {
            Feature current = feature;
            while (current != null)
            {
                string typeName = SafeFeatureTypeName(current);
                string featureName = SafeFeatureName(current);

                if (IsReferencePlaneType(typeName))
                {
                    output.Add(new ReferenceDescriptor
                    {
                        DisplayName = labelPrefix + " Plane - " + featureName,
                        Category = "Planes",
                        Kind = ReferenceKind.Plane,
                        Feature = current,
                        Selectable = current,
                        OwnerComponent = ownerComponent,
                        OwnerDocument = ownerDocument
                    });
                }
                else if (IsReferenceAxisType(typeName))
                {
                    output.Add(new ReferenceDescriptor
                    {
                        DisplayName = labelPrefix + " Axis - " + featureName,
                        Category = "Axes",
                        Kind = ReferenceKind.Axis,
                        Feature = current,
                        Selectable = current,
                        OwnerComponent = ownerComponent,
                        OwnerDocument = ownerDocument
                    });
                }
                else if (IsReferencePointType(typeName))
                {
                    output.Add(new ReferenceDescriptor
                    {
                        DisplayName = labelPrefix + " Point - " + featureName,
                        Category = "Points",
                        Kind = ReferenceKind.Point,
                        Feature = current,
                        Selectable = current,
                        OwnerComponent = ownerComponent,
                        OwnerDocument = ownerDocument
                    });
                }
                else if (includeSketchGeometry && IsSketchType(typeName))
                {
                    output.Add(new ReferenceDescriptor
                    {
                        DisplayName = labelPrefix + " Sketch - " + featureName,
                        Category = "Sketches",
                        Kind = ReferenceKind.Sketch,
                        Feature = current,
                        Selectable = current,
                        OwnerComponent = ownerComponent,
                        OwnerDocument = ownerDocument
                    });

                    AddSketchSegmentReferences(current, output, ownerComponent, ownerDocument, labelPrefix, featureName);
                }

                Feature sub = null;
                try { sub = current.GetFirstSubFeature() as Feature; } catch { sub = null; }
                if (sub != null)
                    TraverseReferenceFeatures(sub, true, output, ownerComponent, ownerDocument, labelPrefix, includeSketchGeometry);

                try { current = subFeature ? current.GetNextSubFeature() as Feature : current.GetNextFeature() as Feature; }
                catch { current = null; }
            }
        }

        private static void AddSketchSegmentReferences(Feature sketchFeature, List<ReferenceDescriptor> output, IComponent2 ownerComponent, IModelDoc2 ownerDocument, string labelPrefix, string sketchName)
        {
            object sketchObject = null;
            try { sketchObject = sketchFeature.GetSpecificFeature2(); } catch { sketchObject = null; }
            if (sketchObject == null)
                return;

            object segmentsObject = null;
            try { segmentsObject = sketchObject.GetType().InvokeMember("GetSketchSegments", BindingFlags.InvokeMethod, null, sketchObject, null); } catch { segmentsObject = null; }
            Array segments = segmentsObject as Array;
            if (segments != null)
            {
                int lineNumber = 1;
                int arcNumber = 1;
                foreach (object segment in segments)
                {
                    if (segment == null)
                        continue;

                    string typeText = SafeSketchSegmentType(segment);
                    bool isLine = typeText.IndexOf("line", StringComparison.OrdinalIgnoreCase) >= 0;
                    bool isArc = typeText.IndexOf("arc", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!isLine && !isArc)
                        continue;

                    string name = isLine ? "Line " + lineNumber.ToString("00") : "Arc " + arcNumber.ToString("00");
                    if (isLine) lineNumber++; else arcNumber++;

                    output.Add(new ReferenceDescriptor
                    {
                        DisplayName = labelPrefix + " " + name + " - " + sketchName,
                        Category = isLine ? "Sketch lines" : "Sketch arcs",
                        Kind = isLine ? ReferenceKind.SketchLine : ReferenceKind.SketchArc,
                        Feature = sketchFeature,
                        SketchObject = segment,
                        Selectable = segment,
                        OwnerComponent = ownerComponent,
                        OwnerDocument = ownerDocument
                    });
                }
            }

            object pointsObject = null;
            try { pointsObject = sketchObject.GetType().InvokeMember("GetSketchPoints2", BindingFlags.InvokeMethod, null, sketchObject, null); } catch { pointsObject = null; }
            Array points = pointsObject as Array;
            if (points != null)
            {
                int pointNumber = 1;
                foreach (object point in points)
                {
                    if (point == null)
                        continue;

                    output.Add(new ReferenceDescriptor
                    {
                        DisplayName = labelPrefix + " Point " + pointNumber.ToString("00") + " - " + sketchName,
                        Category = "Sketch points",
                        Kind = ReferenceKind.SketchPoint,
                        Feature = sketchFeature,
                        SketchObject = point,
                        Selectable = point,
                        OwnerComponent = ownerComponent,
                        OwnerDocument = ownerDocument
                    });
                    pointNumber++;
                }
            }
        }

        private static bool IsReferencePlaneType(string typeName)
        {
            return string.Equals(typeName, "RefPlane", StringComparison.OrdinalIgnoreCase) || typeName.IndexOf("Plane", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsReferenceAxisType(string typeName)
        {
            return typeName.IndexOf("Axis", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsReferencePointType(string typeName)
        {
            return typeName.IndexOf("Point", StringComparison.OrdinalIgnoreCase) >= 0 && typeName.IndexOf("Sketch", StringComparison.OrdinalIgnoreCase) < 0;
        }

        private static bool IsSketchType(string typeName)
        {
            return string.Equals(typeName, "ProfileFeature", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(typeName, "3DProfileFeature", StringComparison.OrdinalIgnoreCase) ||
                   typeName.IndexOf("Sketch", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string SafeSketchSegmentType(object segment)
        {
            if (segment == null)
                return string.Empty;

            try
            {
                object typeObject = segment.GetType().InvokeMember("GetType", BindingFlags.InvokeMethod, null, segment, null);
                return typeObject == null ? string.Empty : Convert.ToString(typeObject);
            }
            catch
            {
                return segment.GetType().Name;
            }
        }

        internal static string SafeFeatureName(Feature feature)
        {
            if (feature == null)
                return string.Empty;
            try { return feature.Name ?? string.Empty; } catch { return string.Empty; }
        }

        internal static string SafeFeatureTypeName(Feature feature)
        {
            if (feature == null)
                return string.Empty;
            try { return feature.GetTypeName2() ?? string.Empty; } catch { return string.Empty; }
        }

        internal static string SafeComponentName(IComponent2 component)
        {
            if (component == null)
                return string.Empty;
            try { return component.Name2 ?? string.Empty; } catch { return string.Empty; }
        }

        internal static string SafeComponentPath(IComponent2 component)
        {
            if (component == null)
                return string.Empty;
            try { return component.GetPathName() ?? string.Empty; } catch { return string.Empty; }
        }

        internal static bool ContainsComponent(List<IComponent2> components, IComponent2 candidate)
        {
            if (components == null || candidate == null)
                return false;

            foreach (IComponent2 component in components)
            {
                if (ComponentsMatch(component, candidate))
                    return true;
            }
            return false;
        }

        private static bool ComponentsMatch(IComponent2 a, IComponent2 b)
        {
            if (a == null || b == null)
                return false;
            if (object.ReferenceEquals(a, b))
                return true;

            return string.Equals(SafeComponentName(a), SafeComponentName(b), StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(SafeComponentPath(a), SafeComponentPath(b), StringComparison.OrdinalIgnoreCase);
        }

        private static string ReadComponentDescription(IComponent2 component)
        {
            if (component == null)
                return string.Empty;

            IModelDoc2 model = null;
            try { model = component.GetModelDoc2() as IModelDoc2; } catch { model = null; }
            if (model == null)
                return string.Empty;

            string configName = string.Empty;
            try { configName = component.ReferencedConfiguration ?? string.Empty; } catch { configName = string.Empty; }

            string value = ReadConfigurationDescription(model, configName);
            if (!string.IsNullOrWhiteSpace(value))
                return value;

            value = ReadCustomProperty(model, configName, "Description");
            if (!string.IsNullOrWhiteSpace(value))
                return value;
            value = ReadCustomProperty(model, configName, "DESCRIPTION");
            if (!string.IsNullOrWhiteSpace(value))
                return value;
            value = ReadCustomProperty(model, string.Empty, "Description");
            if (!string.IsNullOrWhiteSpace(value))
                return value;
            return ReadCustomProperty(model, string.Empty, "DESCRIPTION");
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
    }
}
