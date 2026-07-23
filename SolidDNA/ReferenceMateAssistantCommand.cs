using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace SolidDNA
{
    /// <summary>
    /// Reference Mate Assistant V1.
    ///
    /// Practical V1 scope:
    /// - Active assembly only.
    /// - One selected moving component.
    /// - Scans component reference planes and sketch segments.
    /// - Scans top-level assembly reference planes and sketch segments.
    /// - Basic Sketch feature name is configurable and pinned at the top.
    /// - Creates Coincident, Parallel, Perpendicular, Distance, Angle, and Symmetry mates through SOLIDWORKS selection + AddMate reflection.
    /// - Create and Continue keeps the same moving component and clears only reference picks.
    ///
    /// Notes:
    /// - Full visual hover highlighting and temporary transparency are deliberately conservative in this WinForms V1.
    /// - Display state is not permanently modified.
    /// </summary>
    internal static class ReferenceMateAssistantCommand
    {
        private const string SettingsFolderName = "CabinTools";
        private const string SettingsFileName = "ReferenceMateAssistant.settings.txt";

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

                using (ReferenceMateAssistantForm form = new ReferenceMateAssistantForm(modelDoc))
                {
                    form.ShowDialog();
                }
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

        internal static string SettingsPath
        {
            get
            {
                string root = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.CommonApplicationData), SettingsFolderName, "Settings");
                Directory.CreateDirectory(root);
                return Path.Combine(root, SettingsFileName);
            }
        }

        internal static string LoadBasicSketchFeatureName()
        {
            try
            {
                string path = SettingsPath;
                if (!File.Exists(path))
                    return "Basic Sketch";

                foreach (string line in File.ReadAllLines(path))
                {
                    if (line.StartsWith("BasicSketchFeatureName=", StringComparison.OrdinalIgnoreCase))
                    {
                        string value = line.Substring("BasicSketchFeatureName=".Length).Trim();
                        return string.IsNullOrWhiteSpace(value) ? "Basic Sketch" : value;
                    }
                }
            }
            catch
            {
            }

            return "Basic Sketch";
        }

        internal static void SaveBasicSketchFeatureName(string featureName)
        {
            try
            {
                File.WriteAllText(SettingsPath, "BasicSketchFeatureName=" + (featureName ?? "Basic Sketch").Trim(), Encoding.UTF8);
            }
            catch
            {
            }
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
            SketchPoint
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
            public bool FromBasicSketch;

            public override string ToString()
            {
                return DisplayName;
            }
        }

        private sealed class ReferenceMateAssistantForm : Form
        {
            private readonly IModelDoc2 assemblyModel;
            private readonly IAssemblyDoc assemblyDoc;
            private IComponent2 movingComponent;
            private IComponent2 symmetryComponentB;
            private readonly List<ReferenceDescriptor> componentReferences = new List<ReferenceDescriptor>();
            private readonly List<ReferenceDescriptor> componentBReferences = new List<ReferenceDescriptor>();
            private readonly List<ReferenceDescriptor> assemblyReferences = new List<ReferenceDescriptor>();

            private Label componentLabel;
            private Label componentBLabel;
            private TextBox basicSketchNameTextBox;
            private ComboBox componentReferenceBox;
            private ComboBox componentBReferenceBox;
            private ComboBox assemblyReferenceBox;
            private ComboBox mateTypeBox;
            private TextBox distanceTextBox;
            private TextBox angleTextBox;
            private CheckBox flipCheckBox;
            private CheckBox lockRotationCheckBox;
            private Label statusLabel;
            private Button createButton;
            private Button createContinueButton;

            public ReferenceMateAssistantForm(IModelDoc2 assemblyModel)
            {
                this.assemblyModel = assemblyModel;
                this.assemblyDoc = assemblyModel as IAssemblyDoc;

                Text = "Cabin Tools - Reference Mate Assistant V1";
                Width = 1180;
                Height = 740;
                StartPosition = FormStartPosition.CenterScreen;
                Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

                BuildLayout();
                LoadInitialSelection();
                RefreshReferenceLists();
            }

            private void BuildLayout()
            {
                Label help = new Label();
                help.Text = "Select one moving component, choose one component reference, choose one top-level assembly / Basic Sketch reference, then create the mate. Use Create and Continue for repeated mating.";
                help.Left = 12;
                help.Top = 12;
                help.Width = 1120;
                help.Height = 24;
                Controls.Add(help);

                GroupBox movingGroup = new GroupBox();
                movingGroup.Text = "1 - Moving component";
                movingGroup.Left = 12;
                movingGroup.Top = 44;
                movingGroup.Width = 550;
                movingGroup.Height = 122;
                Controls.Add(movingGroup);

                componentLabel = new Label();
                componentLabel.Left = 12;
                componentLabel.Top = 28;
                componentLabel.Width = 420;
                componentLabel.Height = 22;
                componentLabel.Text = "No moving component loaded";
                movingGroup.Controls.Add(componentLabel);

                Button useSelection = new Button();
                useSelection.Text = "Use selected component";
                useSelection.Left = 12;
                useSelection.Top = 58;
                useSelection.Width = 170;
                useSelection.Click += delegate { LoadInitialSelection(); RefreshReferenceLists(); };
                movingGroup.Controls.Add(useSelection);

                Button resolveButton = new Button();
                resolveButton.Text = "Resolve";
                resolveButton.Left = 192;
                resolveButton.Top = 58;
                resolveButton.Width = 90;
                resolveButton.Click += delegate { ResolveMovingComponent(); RefreshReferenceLists(); };
                movingGroup.Controls.Add(resolveButton);

                Button highlightComp = new Button();
                highlightComp.Text = "Highlight component";
                highlightComp.Left = 292;
                highlightComp.Top = 58;
                highlightComp.Width = 140;
                highlightComp.Click += delegate { SelectObject(movingComponent, false); };
                movingGroup.Controls.Add(highlightComp);

                GroupBox basicGroup = new GroupBox();
                basicGroup.Text = "2 - Basic Sketch source";
                basicGroup.Left = 580;
                basicGroup.Top = 44;
                basicGroup.Width = 570;
                basicGroup.Height = 122;
                Controls.Add(basicGroup);

                Label basicLabel = new Label();
                basicLabel.Text = "Basic Sketch feature name:";
                basicLabel.Left = 12;
                basicLabel.Top = 30;
                basicLabel.Width = 170;
                basicGroup.Controls.Add(basicLabel);

                basicSketchNameTextBox = new TextBox();
                basicSketchNameTextBox.Left = 186;
                basicSketchNameTextBox.Top = 26;
                basicSketchNameTextBox.Width = 230;
                basicSketchNameTextBox.Text = LoadBasicSketchFeatureName();
                basicGroup.Controls.Add(basicSketchNameTextBox);

                Button saveBasicButton = new Button();
                saveBasicButton.Text = "Save + rescan";
                saveBasicButton.Left = 426;
                saveBasicButton.Top = 25;
                saveBasicButton.Width = 120;
                saveBasicButton.Click += delegate { SaveBasicSketchFeatureName(basicSketchNameTextBox.Text); RefreshReferenceLists(); };
                basicGroup.Controls.Add(saveBasicButton);

                Label note = new Label();
                note.Text = "Matching Basic Sketch segments are pinned at the top of the master-reference list.";
                note.Left = 12;
                note.Top = 64;
                note.Width = 520;
                note.Height = 22;
                basicGroup.Controls.Add(note);

                GroupBox refsGroup = new GroupBox();
                refsGroup.Text = "3 - References";
                refsGroup.Left = 12;
                refsGroup.Top = 178;
                refsGroup.Width = 1138;
                refsGroup.Height = 222;
                Controls.Add(refsGroup);

                Label compRefLabel = new Label();
                compRefLabel.Text = "Component reference:";
                compRefLabel.Left = 12;
                compRefLabel.Top = 32;
                compRefLabel.Width = 160;
                refsGroup.Controls.Add(compRefLabel);

                componentReferenceBox = new ComboBox();
                componentReferenceBox.Left = 180;
                componentReferenceBox.Top = 28;
                componentReferenceBox.Width = 760;
                componentReferenceBox.DropDownStyle = ComboBoxStyle.DropDownList;
                refsGroup.Controls.Add(componentReferenceBox);

                Button highlightCompRef = new Button();
                highlightCompRef.Text = "Highlight";
                highlightCompRef.Left = 954;
                highlightCompRef.Top = 27;
                highlightCompRef.Width = 90;
                highlightCompRef.Click += delegate { HighlightSelected(componentReferenceBox); };
                refsGroup.Controls.Add(highlightCompRef);

                Label masterRefLabel = new Label();
                masterRefLabel.Text = "Master reference:";
                masterRefLabel.Left = 12;
                masterRefLabel.Top = 76;
                masterRefLabel.Width = 160;
                refsGroup.Controls.Add(masterRefLabel);

                assemblyReferenceBox = new ComboBox();
                assemblyReferenceBox.Left = 180;
                assemblyReferenceBox.Top = 72;
                assemblyReferenceBox.Width = 760;
                assemblyReferenceBox.DropDownStyle = ComboBoxStyle.DropDownList;
                refsGroup.Controls.Add(assemblyReferenceBox);

                Button highlightMasterRef = new Button();
                highlightMasterRef.Text = "Highlight";
                highlightMasterRef.Left = 954;
                highlightMasterRef.Top = 71;
                highlightMasterRef.Width = 90;
                highlightMasterRef.Click += delegate { HighlightSelected(assemblyReferenceBox); };
                refsGroup.Controls.Add(highlightMasterRef);

                Label mateLabel = new Label();
                mateLabel.Text = "Mate type:";
                mateLabel.Left = 12;
                mateLabel.Top = 122;
                mateLabel.Width = 160;
                refsGroup.Controls.Add(mateLabel);

                mateTypeBox = new ComboBox();
                mateTypeBox.Left = 180;
                mateTypeBox.Top = 118;
                mateTypeBox.Width = 180;
                mateTypeBox.DropDownStyle = ComboBoxStyle.DropDownList;
                mateTypeBox.Items.AddRange(new object[] { "Coincident", "Parallel", "Perpendicular", "Distance", "Angle", "Symmetry" });
                mateTypeBox.SelectedIndex = 0;
                mateTypeBox.SelectedIndexChanged += delegate { UpdateSymmetryControls(); };
                refsGroup.Controls.Add(mateTypeBox);

                Label distanceLabel = new Label();
                distanceLabel.Text = "Distance mm:";
                distanceLabel.Left = 380;
                distanceLabel.Top = 122;
                distanceLabel.Width = 90;
                refsGroup.Controls.Add(distanceLabel);

                distanceTextBox = new TextBox();
                distanceTextBox.Left = 470;
                distanceTextBox.Top = 118;
                distanceTextBox.Width = 90;
                distanceTextBox.Text = "0";
                refsGroup.Controls.Add(distanceTextBox);

                Label angleLabel = new Label();
                angleLabel.Text = "Angle deg:";
                angleLabel.Left = 580;
                angleLabel.Top = 122;
                angleLabel.Width = 80;
                refsGroup.Controls.Add(angleLabel);

                angleTextBox = new TextBox();
                angleTextBox.Left = 660;
                angleTextBox.Top = 118;
                angleTextBox.Width = 90;
                angleTextBox.Text = "0";
                refsGroup.Controls.Add(angleTextBox);

                flipCheckBox = new CheckBox();
                flipCheckBox.Text = "Flip";
                flipCheckBox.Left = 780;
                flipCheckBox.Top = 120;
                flipCheckBox.Width = 70;
                refsGroup.Controls.Add(flipCheckBox);

                lockRotationCheckBox = new CheckBox();
                lockRotationCheckBox.Text = "Lock rotation";
                lockRotationCheckBox.Left = 860;
                lockRotationCheckBox.Top = 120;
                lockRotationCheckBox.Width = 120;
                refsGroup.Controls.Add(lockRotationCheckBox);

                componentBLabel = new Label();
                componentBLabel.Text = "Symmetry component B:";
                componentBLabel.Left = 12;
                componentBLabel.Top = 166;
                componentBLabel.Width = 160;
                refsGroup.Controls.Add(componentBLabel);

                componentBReferenceBox = new ComboBox();
                componentBReferenceBox.Left = 180;
                componentBReferenceBox.Top = 162;
                componentBReferenceBox.Width = 760;
                componentBReferenceBox.DropDownStyle = ComboBoxStyle.DropDownList;
                refsGroup.Controls.Add(componentBReferenceBox);

                Button useSecondSelection = new Button();
                useSecondSelection.Text = "Use 2nd selected component";
                useSecondSelection.Left = 954;
                useSecondSelection.Top = 161;
                useSecondSelection.Width = 160;
                useSecondSelection.Click += delegate { LoadSecondSelectedComponent(); RefreshComponentBReferences(); };
                refsGroup.Controls.Add(useSecondSelection);

                GroupBox actions = new GroupBox();
                actions.Text = "4 - Actions";
                actions.Left = 12;
                actions.Top = 412;
                actions.Width = 1138;
                actions.Height = 112;
                Controls.Add(actions);

                Button clearSel = new Button();
                clearSel.Text = "Clear SOLIDWORKS selection";
                clearSel.Left = 12;
                clearSel.Top = 30;
                clearSel.Width = 190;
                clearSel.Click += delegate { ClearSelection(); };
                actions.Controls.Add(clearSel);

                Button previewButton = new Button();
                previewButton.Text = "Preview selection";
                previewButton.Left = 212;
                previewButton.Top = 30;
                previewButton.Width = 140;
                previewButton.Click += delegate { PreviewMateSelectionOnly(); };
                actions.Controls.Add(previewButton);

                createButton = new Button();
                createButton.Text = "Create";
                createButton.Left = 372;
                createButton.Top = 30;
                createButton.Width = 120;
                createButton.Click += delegate { CreateMate(false); };
                actions.Controls.Add(createButton);

                createContinueButton = new Button();
                createContinueButton.Text = "Create and Continue";
                createContinueButton.Left = 502;
                createContinueButton.Top = 30;
                createContinueButton.Width = 180;
                createContinueButton.Click += delegate { CreateMate(true); };
                actions.Controls.Add(createContinueButton);

                Button closeButton = new Button();
                closeButton.Text = "Close";
                closeButton.Left = 692;
                closeButton.Top = 30;
                closeButton.Width = 120;
                closeButton.Click += delegate { Close(); };
                actions.Controls.Add(closeButton);

                statusLabel = new Label();
                statusLabel.Left = 12;
                statusLabel.Top = 542;
                statusLabel.Width = 1120;
                statusLabel.Height = 120;
                statusLabel.BorderStyle = BorderStyle.FixedSingle;
                statusLabel.Text = "Ready";
                Controls.Add(statusLabel);
            }

            private void LoadInitialSelection()
            {
                List<IComponent2> selected = GetSelectedComponents(assemblyModel);
                if (selected.Count == 1)
                {
                    movingComponent = selected[0];
                    componentLabel.Text = "Moving component: " + SafeComponentName(movingComponent);
                }
                else if (selected.Count > 1)
                {
                    movingComponent = selected[0];
                    symmetryComponentB = selected[1];
                    componentLabel.Text = "Moving component: " + SafeComponentName(movingComponent) + "  (first selected)";
                    componentBLabel.Text = "Symmetry component B: " + SafeComponentName(symmetryComponentB) + "  (second selected)";
                }
                else
                {
                    componentLabel.Text = "No selected component. Select one component in the assembly, then click Use selected component.";
                }
            }

            private void LoadSecondSelectedComponent()
            {
                List<IComponent2> selected = GetSelectedComponents(assemblyModel);
                if (selected.Count >= 2)
                {
                    symmetryComponentB = selected[1];
                    componentBLabel.Text = "Symmetry component B: " + SafeComponentName(symmetryComponentB);
                }
                else
                {
                    statusLabel.Text = "Select two components in the assembly before using the second selected component.";
                }
            }

            private void ResolveMovingComponent()
            {
                if (movingComponent == null)
                    return;

                try
                {
                    ((object)movingComponent).GetType().InvokeMember("SetSuppression2", BindingFlags.InvokeMethod, null, movingComponent, new object[] { (int)swComponentSuppressionState_e.swComponentResolved });
                }
                catch
                {
                }
            }

            private void RefreshReferenceLists()
            {
                componentReferences.Clear();
                assemblyReferences.Clear();

                if (movingComponent != null)
                    ScanComponentReferences(movingComponent, componentReferences, "Component");

                ScanAssemblyReferences(assemblyModel, assemblyReferences, basicSketchNameTextBox == null ? "Basic Sketch" : basicSketchNameTextBox.Text);

                FillCombo(componentReferenceBox, componentReferences);
                FillCombo(assemblyReferenceBox, assemblyReferences);
                RefreshComponentBReferences();

                statusLabel.Text = "Loaded " + componentReferences.Count + " component references and " + assemblyReferences.Count + " master references.";
            }

            private void RefreshComponentBReferences()
            {
                componentBReferences.Clear();
                if (symmetryComponentB != null)
                    ScanComponentReferences(symmetryComponentB, componentBReferences, "Component B");
                FillCombo(componentBReferenceBox, componentBReferences);
                if (symmetryComponentB != null)
                    componentBLabel.Text = "Symmetry component B: " + SafeComponentName(symmetryComponentB);
            }

            private static void FillCombo(ComboBox combo, List<ReferenceDescriptor> refs)
            {
                combo.Items.Clear();
                foreach (ReferenceDescriptor reference in refs)
                    combo.Items.Add(reference);
                if (combo.Items.Count > 0)
                    combo.SelectedIndex = 0;
            }

            private void UpdateSymmetryControls()
            {
                bool symmetry = string.Equals(Convert.ToString(mateTypeBox.SelectedItem), "Symmetry", StringComparison.OrdinalIgnoreCase);
                componentBReferenceBox.Enabled = symmetry;
                componentBLabel.Enabled = symmetry;
            }

            private void HighlightSelected(ComboBox combo)
            {
                ReferenceDescriptor reference = combo.SelectedItem as ReferenceDescriptor;
                if (reference == null)
                    return;
                ClearSelection();
                SelectReference(reference, false);
                statusLabel.Text = "Highlighted: " + reference.DisplayName;
            }

            private void PreviewMateSelectionOnly()
            {
                ClearSelection();
                bool ok = SelectMateReferences();
                statusLabel.Text = ok ? "References selected in SOLIDWORKS. This V1 preview shows the selection set only; mate is not committed until Create." : "Could not select all required references.";
            }

            private void CreateMate(bool keepOpen)
            {
                string writeBlockReason = CabinCustomPropertyStore.GetWriteBlockReason(assemblyModel);
                if (!string.IsNullOrWhiteSpace(writeBlockReason))
                {
                    ShowMessage(writeBlockReason, MessageBoxIcon.Warning);
                    return;
                }

                if (!SelectMateReferences())
                {
                    statusLabel.Text = "Mate creation cancelled: could not select the required references.";
                    return;
                }

                int errorCode;
                object mateObject;
                if (TryCreateMate(out errorCode, out mateObject))
                {
                    try { assemblyModel.ForceRebuild3(false); } catch { }
                    statusLabel.Text = "Mate created. Error status: " + errorCode;
                    if (keepOpen)
                    {
                        ClearReferenceChoicesAfterCreate();
                    }
                    else
                    {
                        Close();
                    }
                }
                else
                {
                    statusLabel.Text = "SOLIDWORKS did not create the mate. Error status: " + errorCode + ". Check entity compatibility and reference selection.";
                }
            }

            private void ClearReferenceChoicesAfterCreate()
            {
                if (componentReferenceBox.Items.Count > 0)
                    componentReferenceBox.SelectedIndex = -1;
                if (assemblyReferenceBox.Items.Count > 0)
                    assemblyReferenceBox.SelectedIndex = -1;
                if (componentBReferenceBox.Items.Count > 0)
                    componentBReferenceBox.SelectedIndex = -1;
                ClearSelection();
            }

            private bool SelectMateReferences()
            {
                ReferenceDescriptor compRef = componentReferenceBox.SelectedItem as ReferenceDescriptor;
                ReferenceDescriptor asmRef = assemblyReferenceBox.SelectedItem as ReferenceDescriptor;
                ReferenceDescriptor compBRef = componentBReferenceBox.SelectedItem as ReferenceDescriptor;
                string mateType = Convert.ToString(mateTypeBox.SelectedItem);

                if (compRef == null || asmRef == null)
                    return false;

                ClearSelection();

                bool okA = SelectReference(compRef, false);
                bool okB = true;
                if (string.Equals(mateType, "Symmetry", StringComparison.OrdinalIgnoreCase))
                {
                    if (compBRef == null)
                        return false;
                    okB = SelectReference(compBRef, true);
                }
                bool okC = SelectReference(asmRef, true);

                return okA && okB && okC;
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

                    // COM RCW can hide overload details. Try InvokeMember as a fallback.
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
                for (int i = parameters.Length - 1; i >= 0; i--)
                {
                    string name = parameters[i].Name ?? string.Empty;
                    if (name.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("status", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        try { return Convert.ToInt32(args[i]); } catch { return 0; }
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

            private static int GetMateType(string mateTypeName)
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
                if (double.TryParse(text.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out value))
                    return value;

                if (double.TryParse(text, out value))
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

                if (reference.OwnerComponent != null)
                {
                    // Select the owner component first so component-context reference selections have a better chance to bind.
                    SelectObject(reference.OwnerComponent, append);
                    append = true;
                }

                return SelectObject(reference.Selectable ?? reference.Feature ?? reference.SketchObject, append);
            }

            private bool SelectObject(object selectable, bool append)
            {
                if (selectable == null)
                    return false;

                try
                {
                    object result = selectable.GetType().InvokeMember("Select4", BindingFlags.InvokeMethod, null, selectable, new object[] { append, null });
                    return result is bool ? (bool)result : true;
                }
                catch
                {
                }

                try
                {
                    object result = selectable.GetType().InvokeMember("Select2", BindingFlags.InvokeMethod, null, selectable, new object[] { append, -1 });
                    return result is bool ? (bool)result : true;
                }
                catch
                {
                }

                try
                {
                    object result = selectable.GetType().InvokeMember("Select", BindingFlags.InvokeMethod, null, selectable, new object[] { append });
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
                if (component != null && !ContainsComponent(result, component))
                    result.Add(component);
            }

            return result;
        }

        internal static void ScanComponentReferences(IComponent2 component, List<ReferenceDescriptor> output, string labelPrefix)
        {
            if (component == null || output == null)
                return;

            IModelDoc2 componentModel = null;
            try { componentModel = component.GetModelDoc2() as IModelDoc2; } catch { componentModel = null; }
            if (componentModel == null)
                return;

            Feature root = null;
            try { root = componentModel.FirstFeature() as Feature; } catch { root = null; }
            TraverseReferenceFeatures(root, false, output, component, componentModel, labelPrefix, string.Empty, false);
        }

        internal static void ScanAssemblyReferences(IModelDoc2 assemblyModel, List<ReferenceDescriptor> output, string basicSketchName)
        {
            if (assemblyModel == null || output == null)
                return;

            List<ReferenceDescriptor> basicSketchRefs = new List<ReferenceDescriptor>();
            List<ReferenceDescriptor> normalRefs = new List<ReferenceDescriptor>();

            Feature root = null;
            try { root = assemblyModel.FirstFeature() as Feature; } catch { root = null; }
            TraverseReferenceFeatures(root, false, normalRefs, null, assemblyModel, "Assembly", basicSketchName ?? "Basic Sketch", false);

            foreach (ReferenceDescriptor reference in normalRefs)
            {
                if (reference.FromBasicSketch)
                    basicSketchRefs.Add(reference);
            }

            foreach (ReferenceDescriptor reference in basicSketchRefs)
                output.Add(reference);

            foreach (ReferenceDescriptor reference in normalRefs)
            {
                if (!reference.FromBasicSketch)
                    output.Add(reference);
            }
        }

        private static void TraverseReferenceFeatures(Feature feature, bool subFeature, List<ReferenceDescriptor> output, IComponent2 ownerComponent, IModelDoc2 ownerDocument, string labelPrefix, string basicSketchName, bool parentIsBasicSketch)
        {
            Feature current = feature;
            while (current != null)
            {
                string typeName = SafeFeatureTypeName(current);
                string featureName = SafeFeatureName(current);
                bool isBasicSketch = parentIsBasicSketch || (!string.IsNullOrWhiteSpace(basicSketchName) && string.Equals(featureName, basicSketchName, StringComparison.OrdinalIgnoreCase));

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
                        OwnerDocument = ownerDocument,
                        FromBasicSketch = isBasicSketch
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
                        OwnerDocument = ownerDocument,
                        FromBasicSketch = isBasicSketch
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
                        OwnerDocument = ownerDocument,
                        FromBasicSketch = isBasicSketch
                    });
                }
                else if (IsSketchType(typeName))
                {
                    output.Add(new ReferenceDescriptor
                    {
                        DisplayName = (isBasicSketch ? "Basic Sketch" : labelPrefix + " Sketch") + " - " + featureName,
                        Category = "Sketches",
                        Kind = ReferenceKind.Sketch,
                        Feature = current,
                        Selectable = current,
                        OwnerComponent = ownerComponent,
                        OwnerDocument = ownerDocument,
                        FromBasicSketch = isBasicSketch
                    });

                    AddSketchSegmentReferences(current, output, ownerComponent, ownerDocument, labelPrefix, featureName, isBasicSketch);
                }

                Feature sub = null;
                try { sub = current.GetFirstSubFeature() as Feature; } catch { sub = null; }
                if (sub != null)
                    TraverseReferenceFeatures(sub, true, output, ownerComponent, ownerDocument, labelPrefix, basicSketchName, isBasicSketch);

                try { current = subFeature ? current.GetNextSubFeature() as Feature : current.GetNextFeature() as Feature; }
                catch { current = null; }
            }
        }

        private static void AddSketchSegmentReferences(Feature sketchFeature, List<ReferenceDescriptor> output, IComponent2 ownerComponent, IModelDoc2 ownerDocument, string labelPrefix, string sketchName, bool isBasicSketch)
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

                    string geometry = isLine ? DescribeLine(segment) : "Arc";
                    string name = isLine ? "Line " + lineNumber.ToString("00") : "Arc " + arcNumber.ToString("00");
                    if (isLine) lineNumber++; else arcNumber++;

                    output.Add(new ReferenceDescriptor
                    {
                        DisplayName = (isBasicSketch ? "Basic Sketch " : labelPrefix + " ") + name + " - " + geometry + " - " + sketchName,
                        Category = isLine ? "Sketch lines" : "Sketch arcs",
                        Kind = isLine ? ReferenceKind.SketchLine : ReferenceKind.SketchArc,
                        Feature = sketchFeature,
                        SketchObject = segment,
                        Selectable = segment,
                        OwnerComponent = ownerComponent,
                        OwnerDocument = ownerDocument,
                        FromBasicSketch = isBasicSketch
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
                        DisplayName = (isBasicSketch ? "Basic Sketch " : labelPrefix + " ") + "Point " + pointNumber.ToString("00") + " - " + sketchName,
                        Category = "Sketch points",
                        Kind = ReferenceKind.SketchPoint,
                        Feature = sketchFeature,
                        SketchObject = point,
                        Selectable = point,
                        OwnerComponent = ownerComponent,
                        OwnerDocument = ownerDocument,
                        FromBasicSketch = isBasicSketch
                    });
                    pointNumber++;
                }
            }
        }

        private static string DescribeLine(object segment)
        {
            try
            {
                object start = segment.GetType().InvokeMember("GetStartPoint2", BindingFlags.InvokeMethod, null, segment, null);
                object end = segment.GetType().InvokeMember("GetEndPoint2", BindingFlags.InvokeMethod, null, segment, null);
                double sx = GetPointCoordinate(start, "X");
                double sy = GetPointCoordinate(start, "Y");
                double sz = GetPointCoordinate(start, "Z");
                double ex = GetPointCoordinate(end, "X");
                double ey = GetPointCoordinate(end, "Y");
                double ez = GetPointCoordinate(end, "Z");
                double dx = ex - sx;
                double dy = ey - sy;
                double dz = ez - sz;
                double lengthMm = Math.Sqrt(dx * dx + dy * dy + dz * dz) * 1000.0;
                string orientation = Math.Abs(dx) >= Math.Abs(dy) && Math.Abs(dx) >= Math.Abs(dz) ? "Horizontal-ish" : "Vertical/angled";
                return orientation + " - " + Math.Round(lengthMm, 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + " mm";
            }
            catch
            {
                return "Line";
            }
        }

        private static double GetPointCoordinate(object point, string propertyName)
        {
            if (point == null)
                return 0.0;
            try
            {
                object value = point.GetType().InvokeMember(propertyName, BindingFlags.GetProperty, null, point, null);
                return value == null ? 0.0 : Convert.ToDouble(value);
            }
            catch
            {
                return 0.0;
            }
        }

        private static bool IsReferencePlaneType(string typeName)
        {
            return string.Equals(typeName, "RefPlane", StringComparison.OrdinalIgnoreCase) ||
                   typeName.IndexOf("Plane", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsReferenceAxisType(string typeName)
        {
            return typeName.IndexOf("Axis", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsReferencePointType(string typeName)
        {
            return typeName.IndexOf("Point", StringComparison.OrdinalIgnoreCase) >= 0 &&
                   typeName.IndexOf("Sketch", StringComparison.OrdinalIgnoreCase) < 0;
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

        internal static bool ContainsComponent(List<IComponent2> components, IComponent2 candidate)
        {
            if (components == null || candidate == null)
                return false;

            string candidateName = SafeComponentName(candidate);
            foreach (IComponent2 component in components)
            {
                if (object.ReferenceEquals(component, candidate))
                    return true;
                if (string.Equals(SafeComponentName(component), candidateName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
