using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using CADBooster.SolidDna;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

using SwEnvironment = CADBooster.SolidDna.SolidWorksEnvironment;
using SwBalloonOptions = SolidWorks.Interop.sldworks.BalloonOptions;
using SwComponent = SolidWorks.Interop.sldworks.Component2;
using SwSelectData = SolidWorks.Interop.sldworks.SelectData;
using SwView = SolidWorks.Interop.sldworks.View;

namespace SolidDNA
{
    /// <summary>
    /// Cabin Tools - Auto Arrange Balloons V1.
    ///
    /// Scope of this first implementation:
    /// - Active SOLIDWORKS drawing only.
    /// - One selected drawing view only.
    /// - Uses visible drawing-view components and visible drawing-view edges.
    /// - Creates BOM balloons by selecting a visible edge and calling InsertBOMBalloon2.
    /// - Does not use magnetic lines or SOLIDWORKS Auto Balloon.
    /// - Places created balloons outside the selected drawing-view boundary.
    /// - Uses deterministic left/right/top/bottom side placement in sheet coordinates.
    ///
    /// Important V1 limitation:
    /// - This version deliberately groups visible nested geometry back to the top-level
    ///   component instance/file/configuration. This is intended for top-level BOM room-layout
    ///   drawings where the drawing BOM items are top-level parts/subassemblies.
    /// - Existing balloons are not deleted. The command avoids destructive behaviour.
    /// - Run on a copied or checked-out drawing until the V1 behaviour has been verified with your templates.
    /// </summary>
    internal static class AutoArrangeBalloonsCommand
    {
        private const double MillimetresToMetres = 0.001;
        private const double DefaultBalloonSpacing = 10.0 * MillimetresToMetres;
        private const double DefaultViewOffset = 25.0 * MillimetresToMetres;
        private const double MinimumVisibleEdgeLength = 1.0 * MillimetresToMetres;

        public static void RunAutoArrangeBalloons()
        {
            ISldWorks swApp = null;

            try
            {
                swApp = SwEnvironment.Application.UnsafeObject as ISldWorks;
            }
            catch
            {
                swApp = null;
            }

            if (swApp == null)
            {
                MessageBox.Show(
                    "Could not connect to SOLIDWORKS.",
                    "Cabin Tools - Auto Arrange Balloons",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            IModelDoc2 model = swApp.ActiveDoc as IModelDoc2;

            if (model == null || model.GetType() != (int)swDocumentTypes_e.swDocDRAWING)
            {
                MessageBox.Show(
                    "Open a drawing before running Auto Arrange Balloons.",
                    "Cabin Tools - Auto Arrange Balloons",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            IDrawingDoc drawing = model as IDrawingDoc;

            if (drawing == null)
            {
                MessageBox.Show(
                    "The active document could not be read as a SOLIDWORKS drawing.",
                    "Cabin Tools - Auto Arrange Balloons",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            AutoBalloonReport report = new AutoBalloonReport();

            try
            {
                SwView selectedView = GetSelectedDrawingView(model);

                if (selectedView == null || !IsModelDrawingView(selectedView))
                {
                    MessageBox.Show(
                        "Select one drawing view before running Auto Arrange Balloons.",
                        "Cabin Tools - Auto Arrange Balloons",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                if (!DrawingHasBomTable(drawing))
                {
                    DialogResult bomWarning = MessageBox.Show(
                        "No BOM table was detected on the drawing.\r\n\r\n" +
                        "The command can still create SOLIDWORKS BOM balloons, but BOM-controlled item numbering may not be available.\r\n\r\n" +
                        "Continue?",
                        "Cabin Tools - Auto Arrange Balloons",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning);

                    if (bomWarning != DialogResult.Yes)
                        return;
                }

                ViewBox viewBox = GetViewBox(selectedView);
                List<BalloonCandidate> candidates = ResolveVisibleBalloonCandidates(swApp, selectedView, report);

                if (candidates.Count == 0)
                {
                    ShowReport(
                        "Auto Arrange Balloons did not find visible balloonable components in the selected view.",
                        report);
                    return;
                }

                List<BalloonCandidate> created = CreateMissingBalloons(model, selectedView, candidates, report);
                ArrangeCreatedBalloons(created, viewBox);

                try
                {
                    model.ClearSelection2(true);
                    model.EditRebuild3();
                }
                catch
                {
                    // Rebuild is useful but should not hide a completed balloon operation.
                }

                ShowReport(
                    "Auto Arrange Balloons completed.",
                    report);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Auto Arrange Balloons failed.\r\n\r\n" + ex.Message,
                    "Cabin Tools - Auto Arrange Balloons",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private static SwView GetSelectedDrawingView(IModelDoc2 model)
        {
            ISelectionMgr selectionManager = null;

            try
            {
                selectionManager = model.SelectionManager as ISelectionMgr;
            }
            catch
            {
                selectionManager = null;
            }

            if (selectionManager == null)
                return null;

            int selectedCount = 0;

            try
            {
                selectedCount = selectionManager.GetSelectedObjectCount2(-1);
            }
            catch
            {
                selectedCount = 0;
            }

            for (int index = 1; index <= selectedCount; index++)
            {
                try
                {
                    SwView view = selectionManager.GetSelectedObjectsDrawingView2(index, -1) as SwView;

                    if (view != null && IsModelDrawingView(view))
                        return view;
                }
                catch
                {
                    // Continue to the next selected object.
                }
            }

            return null;
        }

        private static bool IsModelDrawingView(SwView view)
        {
            if (view == null)
                return false;

            try
            {
                return view.ReferencedDocument != null;
            }
            catch
            {
                return false;
            }
        }

        private static bool DrawingHasBomTable(IDrawingDoc drawing)
        {
            if (drawing == null)
                return false;

            try
            {
                object[] sheetViews = ToObjectArray(drawing.GetViews());

                foreach (object sheetViewObject in sheetViews)
                {
                    object[] viewsOnSheet = ToObjectArray(sheetViewObject);

                    if (viewsOnSheet.Length == 0)
                        continue;

                    SwView sheetView = viewsOnSheet[0] as SwView;

                    if (sheetView == null)
                        continue;

                    object[] tables = ToObjectArray(sheetView.GetTableAnnotations());

                    foreach (object tableObject in tables)
                    {
                        ITableAnnotation table = tableObject as ITableAnnotation;

                        if (table == null)
                            continue;

                        if (table.Type == (int)swTableAnnotationType_e.swTableAnnotation_BillOfMaterials)
                            return true;
                    }
                }
            }
            catch
            {
                // If table scanning fails, do not block the command. Balloon creation itself
                // is still handled by SOLIDWORKS through InsertBOMBalloon2.
            }

            return false;
        }

        private static ViewBox GetViewBox(SwView view)
        {
            double[] fallback = new double[]
            {
                0.05,
                0.05,
                0.25,
                0.20
            };

            try
            {
                double[] outline = ToDoubleArray(view.GetOutline());

                if (outline.Length >= 4)
                {
                    return new ViewBox(
                        Math.Min(outline[0], outline[2]),
                        Math.Min(outline[1], outline[3]),
                        Math.Max(outline[0], outline[2]),
                        Math.Max(outline[1], outline[3]));
                }
            }
            catch
            {
                // Use fallback below.
            }

            return new ViewBox(
                fallback[0],
                fallback[1],
                fallback[2],
                fallback[3]);
        }

        private static List<BalloonCandidate> ResolveVisibleBalloonCandidates(
            ISldWorks swApp,
            SwView view,
            AutoBalloonReport report)
        {
            Dictionary<string, BalloonCandidate> candidatesByTopLevelBomItem =
                new Dictionary<string, BalloonCandidate>(
                    StringComparer.OrdinalIgnoreCase);

            object[] visibleComponents;

            try
            {
                visibleComponents = ToObjectArray(view.GetVisibleComponents());
            }
            catch (Exception ex)
            {
                report.SkippedMessages.Add("Could not read visible components: " + ex.Message);
                return new List<BalloonCandidate>();
            }

            foreach (object componentObject in visibleComponents)
            {
                SwComponent visibleComponent = componentObject as SwComponent;

                if (visibleComponent == null)
                    continue;

                if (IsComponentSuppressed(visibleComponent))
                {
                    report.SkippedSuppressed++;
                    continue;
                }

                SwComponent topLevelComponent =
                    GetTopLevelComponent(visibleComponent);

                if (topLevelComponent == null)
                    continue;

                if (IsComponentSuppressed(topLevelComponent))
                {
                    report.SkippedSuppressed++;
                    continue;
                }

                string bomKey = GetTopLevelBomIdentityKey(topLevelComponent);

                if (string.IsNullOrWhiteSpace(bomKey))
                    bomKey = SafeComponentName(topLevelComponent);

                if (string.IsNullOrWhiteSpace(bomKey))
                    continue;

                EdgeSelection edgeSelection =
                    GetBestVisibleEdge(swApp, view, visibleComponent);

                if (edgeSelection == null)
                {
                    // Do not report every nested component here. A top-level BOM item
                    // may still get a valid visible edge from another child component.
                    continue;
                }

                BalloonCandidate existingCandidate;

                if (!candidatesByTopLevelBomItem.TryGetValue(
                        bomKey,
                        out existingCandidate))
                {
                    candidatesByTopLevelBomItem.Add(
                        bomKey,
                        new BalloonCandidate
                        {
                            Component = topLevelComponent,
                            ComponentKey = bomKey,
                            ComponentName = SafeComponentName(topLevelComponent),
                            AttachmentEntity = edgeSelection.Entity,
                            AttachmentPoint = edgeSelection.MidPoint,
                            AttachmentEdgeLength = edgeSelection.Length
                        });

                    continue;
                }

                // Keep the longest visible edge found anywhere under the same
                // top-level BOM item. This gives a cleaner leader target while
                // still producing only one balloon for that top-level item.
                if (edgeSelection.Length > existingCandidate.AttachmentEdgeLength)
                {
                    existingCandidate.AttachmentEntity = edgeSelection.Entity;
                    existingCandidate.AttachmentPoint = edgeSelection.MidPoint;
                    existingCandidate.AttachmentEdgeLength = edgeSelection.Length;
                }
            }

            List<BalloonCandidate> result =
                candidatesByTopLevelBomItem.Values.ToList();

            report.VisibleComponents = visibleComponents.Length;
            report.UniqueCandidates = result.Count;

            if (result.Count == 0)
            {
                report.SkippedMessages.Add(
                    "No top-level BOM items with usable visible geometry were found in the selected view.");
            }

            return result;
        }

        private static EdgeSelection GetBestVisibleEdge(
            ISldWorks swApp,
            SwView view,
            SwComponent component)
        {
            object[] visibleEdges;

            try
            {
                visibleEdges = ToObjectArray(
                    view.GetVisibleEntities2(
                        component,
                        (int)swViewEntityType_e.swViewEntityType_Edge));
            }
            catch
            {
                visibleEdges = new object[0];
            }

            if (visibleEdges.Length == 0)
                return null;

            EdgeSelection best = null;

            foreach (object edgeObject in visibleEdges)
            {
                IEdge edge = edgeObject as IEdge;
                IEntity entity = edgeObject as IEntity;

                if (edge == null || entity == null)
                    continue;

                SheetPoint start;
                SheetPoint end;

                if (!TryGetEdgeSheetEndPoints(swApp, view, edge, out start, out end))
                    continue;

                double length = start.DistanceTo(end);

                if (length < MinimumVisibleEdgeLength)
                    continue;

                if (best == null || length > best.Length)
                {
                    best = new EdgeSelection
                    {
                        Entity = entity,
                        Length = length,
                        MidPoint = new SheetPoint(
                            (start.X + end.X) / 2.0,
                            (start.Y + end.Y) / 2.0)
                    };
                }
            }

            return best;
        }

        private static bool TryGetEdgeSheetEndPoints(
            ISldWorks swApp,
            SwView view,
            IEdge edge,
            out SheetPoint start,
            out SheetPoint end)
        {
            start = SheetPoint.Empty;
            end = SheetPoint.Empty;

            if (swApp == null || view == null || edge == null)
                return false;

            try
            {
                IVertex startVertex = edge.GetStartVertex() as IVertex;
                IVertex endVertex = edge.GetEndVertex() as IVertex;

                if (startVertex == null || endVertex == null)
                    return false;

                double[] startModel = ToDoubleArray(startVertex.GetPoint());
                double[] endModel = ToDoubleArray(endVertex.GetPoint());

                if (startModel.Length < 3 || endModel.Length < 3)
                    return false;

                start = ModelPointToSheetPoint(swApp, view, startModel);
                end = ModelPointToSheetPoint(swApp, view, endModel);

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static SheetPoint ModelPointToSheetPoint(
            ISldWorks swApp,
            SwView view,
            double[] modelPoint)
        {
            IMathUtility mathUtility = swApp.GetMathUtility() as IMathUtility;

            if (mathUtility == null)
                return SheetPoint.Empty;

            IMathPoint mathPoint = mathUtility.CreatePoint(modelPoint) as IMathPoint;

            if (mathPoint == null)
                return SheetPoint.Empty;

            IMathTransform transform = view.ModelToViewTransform as IMathTransform;

            if (transform == null)
                return SheetPoint.Empty;

            IMathPoint sheetPoint = mathPoint.MultiplyTransform(transform) as IMathPoint;

            if (sheetPoint == null)
                return SheetPoint.Empty;

            double[] data = ToDoubleArray(sheetPoint.ArrayData);

            if (data.Length < 2)
                return SheetPoint.Empty;

            return new SheetPoint(data[0], data[1]);
        }

        private static List<BalloonCandidate> CreateMissingBalloons(
            IModelDoc2 model,
            SwView view,
            List<BalloonCandidate> candidates,
            AutoBalloonReport report)
        {
            List<BalloonCandidate> created = new List<BalloonCandidate>();

            ISelectionMgr selectionManager = model.SelectionManager as ISelectionMgr;
            IModelDocExtension extension = model.Extension as IModelDocExtension;

            if (selectionManager == null || extension == null)
            {
                report.SkippedMessages.Add("Could not access SOLIDWORKS selection manager or model extension.");
                return created;
            }

            foreach (BalloonCandidate candidate in candidates)
            {
                if (candidate == null || candidate.AttachmentEntity == null)
                    continue;

                try
                {
                    model.ClearSelection2(true);

                    SwSelectData selectData = selectionManager.CreateSelectData() as SwSelectData;

                    if (selectData != null)
                        selectData.View = view;

                    bool selected = candidate.AttachmentEntity.Select4(false, selectData);

                    if (!selected)
                    {
                        report.SkippedMessages.Add(candidate.ComponentName + ": visible edge could not be selected.");
                        continue;
                    }

                    SwBalloonOptions balloonOptions = extension.CreateBalloonOptions() as SwBalloonOptions;

                    if (balloonOptions == null)
                    {
                        report.SkippedMessages.Add(candidate.ComponentName + ": SOLIDWORKS did not create balloon options.");
                        continue;
                    }

                    INote balloonNote = extension.InsertBOMBalloon2(balloonOptions) as INote;

                    if (balloonNote == null)
                    {
                        report.SkippedMessages.Add(candidate.ComponentName + ": SOLIDWORKS did not create the BOM balloon.");
                        continue;
                    }

                    IAnnotation annotation = balloonNote.GetAnnotation() as IAnnotation;

                    if (annotation == null)
                    {
                        report.SkippedMessages.Add(candidate.ComponentName + ": balloon annotation could not be read after creation.");
                        continue;
                    }

                    candidate.BalloonNote = balloonNote;
                    candidate.Annotation = annotation;
                    created.Add(candidate);
                    report.Created++;
                }
                catch (Exception ex)
                {
                    report.SkippedMessages.Add(candidate.ComponentName + ": " + ex.Message);
                }
                finally
                {
                    try { model.ClearSelection2(true); } catch { }
                }
            }

            return created;
        }

        private static void ArrangeCreatedBalloons(
            List<BalloonCandidate> balloons,
            ViewBox viewBox)
        {
            if (balloons == null || balloons.Count == 0)
                return;

            foreach (BalloonCandidate balloon in balloons)
            {
                balloon.Side = DetermineSide(balloon.AttachmentPoint, viewBox);
            }

            ArrangeSide(
                balloons.Where(item => item.Side == BalloonSide.Left).ToList(),
                viewBox,
                BalloonSide.Left);

            ArrangeSide(
                balloons.Where(item => item.Side == BalloonSide.Right).ToList(),
                viewBox,
                BalloonSide.Right);

            ArrangeSide(
                balloons.Where(item => item.Side == BalloonSide.Top).ToList(),
                viewBox,
                BalloonSide.Top);

            ArrangeSide(
                balloons.Where(item => item.Side == BalloonSide.Bottom).ToList(),
                viewBox,
                BalloonSide.Bottom);
        }

        private static BalloonSide DetermineSide(
            SheetPoint attachment,
            ViewBox viewBox)
        {
            double leftDistance = Math.Abs(attachment.X - viewBox.MinX);
            double rightDistance = Math.Abs(viewBox.MaxX - attachment.X);
            double topDistance = Math.Abs(viewBox.MaxY - attachment.Y);
            double bottomDistance = Math.Abs(attachment.Y - viewBox.MinY);

            // Prefer left/right for normal drawing views unless top/bottom is clearly closer.
            double horizontalBest = Math.Min(leftDistance, rightDistance);
            double verticalBest = Math.Min(topDistance, bottomDistance);

            if (verticalBest < horizontalBest * 0.55)
                return topDistance < bottomDistance ? BalloonSide.Top : BalloonSide.Bottom;

            return leftDistance < rightDistance ? BalloonSide.Left : BalloonSide.Right;
        }

        private static void ArrangeSide(
            List<BalloonCandidate> sideBalloons,
            ViewBox viewBox,
            BalloonSide side)
        {
            if (sideBalloons == null || sideBalloons.Count == 0)
                return;

            if (side == BalloonSide.Left || side == BalloonSide.Right)
            {
                sideBalloons.Sort(
                    (a, b) => b.AttachmentPoint.Y.CompareTo(a.AttachmentPoint.Y));

                List<double> yPositions = GetEvenVerticalPositions(
                    sideBalloons.Count,
                    viewBox.MinY,
                    viewBox.MaxY);

                double x = side == BalloonSide.Left
                    ? viewBox.MinX - DefaultViewOffset
                    : viewBox.MaxX + DefaultViewOffset;

                for (int index = 0; index < sideBalloons.Count; index++)
                {
                    ApplyBalloonPosition(
                        sideBalloons[index],
                        new SheetPoint(x, yPositions[index]));
                }
            }
            else
            {
                sideBalloons.Sort(
                    (a, b) => a.AttachmentPoint.X.CompareTo(b.AttachmentPoint.X));

                List<double> xPositions = GetEvenHorizontalPositions(
                    sideBalloons.Count,
                    viewBox.MinX,
                    viewBox.MaxX);

                double y = side == BalloonSide.Top
                    ? viewBox.MaxY + DefaultViewOffset
                    : viewBox.MinY - DefaultViewOffset;

                for (int index = 0; index < sideBalloons.Count; index++)
                {
                    ApplyBalloonPosition(
                        sideBalloons[index],
                        new SheetPoint(xPositions[index], y));
                }
            }
        }

        private static List<double> GetEvenVerticalPositions(
            int count,
            double minY,
            double maxY)
        {
            List<double> positions = new List<double>();

            if (count <= 0)
                return positions;

            double centre = (minY + maxY) / 2.0;
            double requiredSpan = Math.Max(maxY - minY, (count - 1) * DefaultBalloonSpacing);
            double start = centre + requiredSpan / 2.0;

            for (int index = 0; index < count; index++)
                positions.Add(start - index * DefaultBalloonSpacing);

            return positions;
        }

        private static List<double> GetEvenHorizontalPositions(
            int count,
            double minX,
            double maxX)
        {
            List<double> positions = new List<double>();

            if (count <= 0)
                return positions;

            double centre = (minX + maxX) / 2.0;
            double requiredSpan = Math.Max(maxX - minX, (count - 1) * DefaultBalloonSpacing);
            double start = centre - requiredSpan / 2.0;

            for (int index = 0; index < count; index++)
                positions.Add(start + index * DefaultBalloonSpacing);

            return positions;
        }

        private static void ApplyBalloonPosition(
            BalloonCandidate candidate,
            SheetPoint balloonPosition)
        {
            if (candidate == null || candidate.Annotation == null)
                return;

            try
            {
                candidate.Annotation.SetLeaderAttachmentPointAtIndex(
                    0,
                    candidate.AttachmentPoint.X,
                    candidate.AttachmentPoint.Y,
                    0.0);
            }
            catch
            {
                // Some balloon/leader combinations reject explicit attachment point changes.
            }

            try
            {
                candidate.Annotation.SetPosition2(
                    balloonPosition.X,
                    balloonPosition.Y,
                    0.0);
            }
            catch
            {
                // Keep processing other balloons.
            }
        }

        private static bool IsComponentSuppressed(SwComponent component)
        {
            if (component == null)
                return true;

            try
            {
                return component.GetSuppression() ==
                       (int)swComponentSuppressionState_e.swComponentSuppressed;
            }
            catch
            {
                return false;
            }
        }

        private static SwComponent GetTopLevelComponent(
            SwComponent component)
        {
            if (component == null)
                return null;

            SwComponent current = component;

            for (int guard = 0; guard < 64; guard++)
            {
                SwComponent parent = null;

                try
                {
                    parent = current.GetParent() as SwComponent;
                }
                catch
                {
                    parent = null;
                }

                if (parent == null)
                    return current;

                current = parent;
            }

            return current;
        }

        private static string GetTopLevelBomIdentityKey(
            SwComponent component)
        {
            if (component == null)
                return string.Empty;

            string path = string.Empty;
            string configuration = string.Empty;

            try { path = component.GetPathName() ?? string.Empty; } catch { path = string.Empty; }
            try { configuration = component.ReferencedConfiguration ?? string.Empty; } catch { configuration = string.Empty; }

            if (string.IsNullOrWhiteSpace(path))
                path = SafeComponentName(component);

            return path.Trim() + "|" + configuration.Trim();
        }

        private static string SafeComponentName(SwComponent component)
        {
            if (component == null)
                return string.Empty;

            try
            {
                return component.Name2 ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static object[] ToObjectArray(object value)
        {
            if (value == null)
                return new object[0];

            object[] objectArray = value as object[];

            if (objectArray != null)
                return objectArray;

            Array array = value as Array;

            if (array == null)
                return new object[0];

            object[] result = new object[array.Length];
            array.CopyTo(result, 0);
            return result;
        }

        private static double[] ToDoubleArray(object value)
        {
            if (value == null)
                return new double[0];

            double[] doubleArray = value as double[];

            if (doubleArray != null)
                return doubleArray;

            Array array = value as Array;

            if (array == null)
                return new double[0];

            double[] result = new double[array.Length];

            for (int index = 0; index < array.Length; index++)
            {
                object item = array.GetValue(index);
                result[index] = item == null ? 0.0 : Convert.ToDouble(item);
            }

            return result;
        }

        private static void ShowReport(
            string title,
            AutoBalloonReport report)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine(title);
            builder.AppendLine();
            builder.AppendLine("Visible components scanned: " + report.VisibleComponents);
            builder.AppendLine("Unique balloon candidates: " + report.UniqueCandidates);
            builder.AppendLine("Balloons created / arranged: " + report.Created);
            builder.AppendLine("Suppressed components skipped: " + report.SkippedSuppressed);

            if (report.SkippedMessages.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("Skipped / warnings:");

                foreach (string message in report.SkippedMessages.Take(20))
                    builder.AppendLine("- " + message);

                if (report.SkippedMessages.Count > 20)
                    builder.AppendLine("- ... " + (report.SkippedMessages.Count - 20) + " more item(s). ");
            }

            MessageBox.Show(
                builder.ToString(),
                "Cabin Tools - Auto Arrange Balloons",
                MessageBoxButtons.OK,
                report.SkippedMessages.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }

        private enum BalloonSide
        {
            Left,
            Right,
            Top,
            Bottom
        }

        private sealed class BalloonCandidate
        {
            public SwComponent Component;
            public string ComponentKey;
            public string ComponentName;
            public IEntity AttachmentEntity;
            public SheetPoint AttachmentPoint;
            public double AttachmentEdgeLength;
            public INote BalloonNote;
            public IAnnotation Annotation;
            public BalloonSide Side;
        }

        private sealed class EdgeSelection
        {
            public IEntity Entity;
            public double Length;
            public SheetPoint MidPoint;
        }

        private sealed class AutoBalloonReport
        {
            public int VisibleComponents;
            public int UniqueCandidates;
            public int Created;
            public int SkippedSuppressed;
            public readonly List<string> SkippedMessages = new List<string>();
        }

        private struct ViewBox
        {
            public readonly double MinX;
            public readonly double MinY;
            public readonly double MaxX;
            public readonly double MaxY;

            public ViewBox(
                double minX,
                double minY,
                double maxX,
                double maxY)
            {
                MinX = minX;
                MinY = minY;
                MaxX = maxX;
                MaxY = maxY;
            }
        }

        private struct SheetPoint
        {
            public static readonly SheetPoint Empty = new SheetPoint(0.0, 0.0);

            public readonly double X;
            public readonly double Y;

            public SheetPoint(
                double x,
                double y)
            {
                X = x;
                Y = y;
            }

            public double DistanceTo(SheetPoint other)
            {
                double dx = X - other.X;
                double dy = Y - other.Y;
                return Math.Sqrt(dx * dx + dy * dy);
            }
        }
    }
}
