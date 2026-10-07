using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;

namespace SolidDNA
{
    public enum PropertyPaneDocumentKind
    {
        Part,
        Assembly,
        Drawing
    }

    public enum PropertyPaneScope
    {
        Document,
        ActiveConfiguration,
        SelectedConfigurations,
        AllConfigurations
    }

    public enum PropertyPaneControlType
    {
        Text,
        MultilineText,
        List,
        Date,
        Automatic,
        ReadOnlyPreview,
        // Kept for schema-v1 profile compatibility. New profiles use
        // ReadOnlyPreview for read-only values.
        ReadOnly
    }

    [Serializable]
    public sealed class PropertyPaneProfile
    {
        public int SchemaVersion { get; set; }
        public bool AllowDrawingNumberOverride { get; set; }
        public bool UseSharedWorkbook { get; set; }
        public string SharedWorkbookPath { get; set; }
        public List<PropertyPaneLayout> Layouts { get; set; }
        public List<PropertyPaneNamedList> Lists { get; set; }
        public List<DrawingNumberMap> DrawingNumberMappings { get; set; }
        public List<DimensionMap> Dimensions { get; set; }
        public List<string> PropertyCatalog { get; set; }

        public PropertyPaneProfile()
        {
            SchemaVersion = 1;
            SharedWorkbookPath = string.Empty;
            Layouts = new List<PropertyPaneLayout>();
            Lists = new List<PropertyPaneNamedList>();
            DrawingNumberMappings = new List<DrawingNumberMap>();
            Dimensions = new List<DimensionMap>();
            PropertyCatalog = new List<string>();
        }
    }

    [Serializable]
    public sealed class PropertyPaneLayout
    {
        [XmlAttribute]
        public PropertyPaneDocumentKind DocumentKind { get; set; }
        public List<PropertyPaneGroup> Groups { get; set; }

        public PropertyPaneLayout()
        {
            Groups = new List<PropertyPaneGroup>();
        }
    }

    [Serializable]
    public sealed class PropertyPaneGroup
    {
        [XmlAttribute]
        public string Id { get; set; }
        [XmlAttribute]
        public string Label { get; set; }
        [XmlAttribute]
        public int Order { get; set; }
        [XmlAttribute]
        public bool Collapsed { get; set; }
        public List<PropertyPaneField> Fields { get; set; }

        public PropertyPaneGroup()
        {
            Fields = new List<PropertyPaneField>();
        }
    }

    [Serializable]
    public sealed class PropertyPaneField
    {
        [XmlAttribute]
        public string Id { get; set; }
        [XmlAttribute]
        public string Label { get; set; }
        [XmlAttribute]
        public string PropertyName { get; set; }
        [XmlAttribute]
        public int Order { get; set; }
        [XmlAttribute]
        public PropertyPaneControlType ControlType { get; set; }
        [XmlAttribute]
        public PropertyPaneScope DefaultScope { get; set; }
        [XmlAttribute]
        public string ListName { get; set; }
        [XmlAttribute]
        public bool Required { get; set; }
        [XmlAttribute]
        public bool ReadOnly { get; set; }
        [XmlAttribute]
        public bool Visible { get; set; }
        [XmlAttribute]
        public string AutomaticValue { get; set; }
        [XmlAttribute]
        public string DefaultExpression { get; set; }
        [XmlAttribute]
        public string DefaultValue { get; set; }
        [XmlAttribute]
        public bool BlankMeansNoChange { get; set; }
        [XmlAttribute]
        public bool AllowExplicitClear { get; set; }
        [XmlAttribute]
        public int MaxLength { get; set; }
        [XmlAttribute]
        public string ValidationPattern { get; set; }
        [XmlAttribute]
        public string ValidationMessage { get; set; }
        [XmlAttribute]
        public string VisibleWhenFieldId { get; set; }
        [XmlAttribute]
        public string VisibleWhenValue { get; set; }
        [XmlAttribute]
        public string EnabledWhenFieldId { get; set; }
        [XmlAttribute]
        public string EnabledWhenValue { get; set; }
        [XmlAttribute]
        public string HelpText { get; set; }
        [XmlArrayItem("Scope")]
        public List<PropertyPaneScope> AllowedScopes { get; set; }

        public PropertyPaneField()
        {
            Visible = true;
            BlankMeansNoChange = true;
            AllowExplicitClear = true;
            AllowedScopes = new List<PropertyPaneScope>();
        }
    }

    [Serializable]
    public sealed class PropertyPaneNamedList
    {
        [XmlAttribute]
        public string Name { get; set; }
        [XmlElement("Value")]
        public List<string> Values { get; set; }

        public PropertyPaneNamedList()
        {
            Values = new List<string>();
        }
    }

    [Serializable]
    public sealed class DrawingNumberMap
    {
        [XmlAttribute]
        public string CabinDescription { get; set; }
        [XmlAttribute]
        public string CabinDefined { get; set; }
        [XmlAttribute]
        public string LayoutType { get; set; }
        [XmlAttribute]
        public string DrawingNumber { get; set; }
    }

    [Serializable]
    public sealed class DimensionMap
    {
        [XmlAttribute]
        public string Id { get; set; }
        [XmlAttribute]
        public string Label { get; set; }
        [XmlAttribute]
        public string FeatureName { get; set; }
        [XmlAttribute]
        public string FromPlane { get; set; }
        [XmlAttribute]
        public bool AllowNegative { get; set; }
        [XmlAttribute]
        public int Order { get; set; }
    }

    internal static class PropertyPaneDefaults
    {
        public const int CurrentSchemaVersion = 2;

        public static PropertyPaneProfile Create()
        {
            PropertyPaneProfile profile = new PropertyPaneProfile
            {
                SchemaVersion = CurrentSchemaVersion,
                AllowDrawingNumberOverride = false
            };

            profile.Lists.Add(List("Class", "Ceiling Material", "Fixed Furniture", "Wall Material"));
            profile.Lists.Add(List("Sub-Family", "Ceiling Panel", "Wall Panel", "Wall Profile"));
            profile.Lists.Add(List("Brand", "Anttiteollisuus", "Arteor", "Banco", "Dormakaba", "ENSTO", "Foliot", "Gigamedia", "Glamox", "IndelB", "Korppinen", "Legrand", "Meka", "PMB", "SBA", "Solar Solve", "TBD", "Zenitel"));
            profile.Lists.Add(List("Model", "20 x 20 x 1,5", "20 x 25 x 1,5", "40 x 20 x 2", "Custom Made", "D.520.0053.408.861", "D.520.4100.408.401", "D.520.4100.408.403", "Extension Screen B-15", "Halfround", "No Lid", "JMC 1 31 B-15", "JMC 1 31 B-15 EXT", "JMC 1 31 B-15 EXT RF", "Petra Q 8371", "PS1-165", "RTRt-E 52580", "StopLite", "TBD", "TDB 22", "TL43-W574 LED 1300HF 840 OP", "Tomeo", "TPL 27", "Type 121", "Type 304", "Type 305", "Type 321", "Type 421", "With Kick-Out Panel"));
            profile.Lists.Add(List("DesignBy", "pasi.turpeinen@almaco.cc", "sanju.shrestha@almaco.cc"));
            profile.Lists.Add(List("Status", "In work", "Ready"));

            AddPartLayout(profile);
            AddAssemblyLayout(profile);
            AddDrawingLayout(profile);
            AddDrawingMappings(profile);
            profile.Lists.Add(List("Cabin type description",
                profile.DrawingNumberMappings.Select(m => m.CabinDescription)
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray()));
            profile.Lists.Add(List("Cabin type defined",
                profile.DrawingNumberMappings.Select(m => m.CabinDefined)
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray()));
            profile.Lists.Add(List("Layout Type",
                profile.DrawingNumberMappings.Select(m => m.LayoutType)
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray()));

            profile.Dimensions.Add(new DimensionMap { Id = "bottom", Label = "Bottom elevation", FeatureName = "ADJUST Height - Bottom", FromPlane = "Steel deck", AllowNegative = true, Order = 0 });
            profile.Dimensions.Add(new DimensionMap { Id = "height", Label = "Height", FeatureName = "ADJUST Height - Top", FromPlane = "ADJUST Height - Bottom", AllowNegative = false, Order = 1 });
            profile.Dimensions.Add(new DimensionMap { Id = "width", Label = "Width", FeatureName = "ADJUST Width - Side", FromPlane = "Side", AllowNegative = false, Order = 2 });
            profile.Dimensions.Add(new DimensionMap { Id = "depth", Label = "Depth", FeatureName = "ADJUST Depth - Front", FromPlane = "Back", AllowNegative = false, Order = 3 });

            profile.PropertyCatalog.AddRange((
                "Description|Brand|Model|Revision|Cabin type description|Cabin type defined|Layout type|Material|DesignBy|DesignDate|CheckedBy|CheckedDate|SW-FileName|SW-PartNumber|Client|Project|Status|Description2|Description3|Specification2|Number|PartNo|Weight|Finish|StockSize|UnitOfMeasure|Cost - Total Cost|Cost - Material Cost|Cost - Manufacturing Cost|Cost - Material Name|Cost - Template Name|Cost - Stock Type|Cost - Stock Size|Cost - Cost Calculation Time|MakeOrBuy|LeadTime|DrawnBy|DrawnDate|EngineeringApproval|EngAppDate|ManufacturingApproval|MfgAppDate|QAApproval|QAAppDate|Vendor|VendorNo|DateCompleted|CompanyName|Department|Division|Group|Author|Owner|Source|IsFastener|RouteOnDrop|DoNotSpin|PunchID|Supplier").Split('|'));

            return profile;
        }

        private static void AddPartLayout(PropertyPaneProfile profile)
        {
            PropertyPaneGroup classification = Group("classification", "Class and sub-family", 0);
            classification.Fields.Add(Field("class", "Class", "Class", 0, PropertyPaneControlType.List, "Class"));
            classification.Fields.Add(Field("sub-family", "Sub-Family", "Sub-Family", 1, PropertyPaneControlType.List, "Sub-Family"));

            PropertyPaneGroup attributes = Group("attributes", "Other attributes", 1);
            attributes.Fields.Add(Field("brand", "Brand", "Brand", 0, PropertyPaneControlType.List, "Brand", defaultScope: PropertyPaneScope.ActiveConfiguration));
            attributes.Fields.Add(Field("model", "Model", "Model", 1, PropertyPaneControlType.List, "Model"));
            attributes.Fields.Add(Field("specification1", "Specification", "Specification1", 2, defaultScope: PropertyPaneScope.ActiveConfiguration));
            attributes.Fields.Add(Field("reference", "Reference", "Reference", 3, defaultScope: PropertyPaneScope.ActiveConfiguration));

            PropertyPaneGroup general = Group("general", "General", 2);
            general.Fields.Add(Field("material", "Material", "Material", 0, PropertyPaneControlType.Text, null, "$PRP:\"SW-Material\""));

            PropertyPaneGroup drawing = Group("drawing-properties", "Drawing properties", 3);
            drawing.Fields.Add(Field("project", "Project", "Project", 0));
            drawing.Fields.Add(Field("description", "Description", "Description", 1, PropertyPaneControlType.MultilineText));
            drawing.Fields.Add(Field("designby", "Designed By", "DesignBy", 2, PropertyPaneControlType.List, "DesignBy"));
            drawing.Fields.Add(Field("designdate", "Designed Date", "DesignDate", 3, PropertyPaneControlType.Date));

            profile.Layouts.Add(new PropertyPaneLayout
            {
                DocumentKind = PropertyPaneDocumentKind.Part,
                Groups = new List<PropertyPaneGroup> { classification, attributes, general, drawing }
            });
        }

        private static void AddAssemblyLayout(PropertyPaneProfile profile)
        {
            PropertyPaneGroup identity = Group("assembly-identity", "General", 0);
            identity.Fields.Add(Field("sw-filename", "File name", "SW-FileName", 0, PropertyPaneControlType.ReadOnlyPreview, null, "$PRP:\"SW-File Name\"", "FileName"));
            identity.Fields.Add(Field("description", "Description", "Description", 1, PropertyPaneControlType.MultilineText));

            PropertyPaneGroup cabin = Group("assembly-cabin", "Cabin", 1);
            cabin.Fields.Add(Field("cabin-description", "Cabin type", "Cabin type description", 0, PropertyPaneControlType.List, "Cabin type description"));
            cabin.Fields.Add(Field("cabin-defined", "Cabin defined", "Cabin type defined", 1, PropertyPaneControlType.List, "Cabin type defined"));
            cabin.Fields.Add(Field("layout-type", "Layout", "Layout type", 2, PropertyPaneControlType.List, "Layout Type"));
            cabin.Fields.Add(Field("brand", "Brand", "Brand", 3, PropertyPaneControlType.List, "Brand", defaultScope: PropertyPaneScope.ActiveConfiguration));
            cabin.Fields.Add(Field("model", "Model", "Model", 4, PropertyPaneControlType.List, "Model"));

            PropertyPaneGroup approval = Group("assembly-approval", "Design and status", 2);
            approval.Fields.Add(Field("designby", "Designed By", "DesignBy", 0, PropertyPaneControlType.List, "DesignBy"));
            approval.Fields.Add(Field("designdate", "Designed Date", "DesignDate", 1, PropertyPaneControlType.Date));
            approval.Fields.Add(Field("status", "Status", "Status", 2, PropertyPaneControlType.List, "Status"));
            approval.Fields.Add(Field("weight", "Weight", "Weight", 3, PropertyPaneControlType.ReadOnlyPreview, null, "[SW-Mass] kg", "Weight"));
            profile.Layouts.Add(new PropertyPaneLayout
            {
                DocumentKind = PropertyPaneDocumentKind.Assembly,
                Groups = new List<PropertyPaneGroup> { identity, cabin, approval }
            });
        }

        private static void AddDrawingLayout(PropertyPaneProfile profile)
        {
            PropertyPaneGroup titles = Group("drawing-titles", "Titles", 0);
            titles.Fields.Add(Field("title2", "Title 2", "Title2", 0, PropertyPaneControlType.Automatic, null, "$PRP:\"Cabin type description\"", "Title2"));
            titles.Fields.Add(Field("title3", "Title 3", "Title 3", 1, PropertyPaneControlType.Automatic, null, "$PRP:\"Cabin type defined\" - $PRP:\"Layout Type\"", "Title3"));

            PropertyPaneGroup drawing = Group("drawing-identification", "Drawing", 1);
            drawing.Fields.Add(Field("sw-filename", "File name", "SW-File Name", 0, PropertyPaneControlType.ReadOnlyPreview, null, null, "FileName"));
            drawing.Fields.Add(Field("cabin-description", "Cabin type", "Cabin type description", 1, PropertyPaneControlType.List, "Cabin type description"));
            drawing.Fields.Add(Field("cabin-defined", "Cabin defined", "Cabin type defined", 2, PropertyPaneControlType.List, "Cabin type defined"));
            drawing.Fields.Add(Field("layout-type", "Layout", "Layout Type", 3, PropertyPaneControlType.List, "Layout Type"));
            drawing.Fields.Add(Field("drwnumber", "Drawing Number", "DrwNumber", 4, PropertyPaneControlType.Automatic, null, null, "DrawingNumber"));
            drawing.Fields.Add(Field("revision", "Revision", "Revision", 5));

            PropertyPaneGroup approval = Group("drawing-approval", "Design and check", 2);
            approval.Fields.Add(Field("designby", "Designer", "DesignBy", 0, PropertyPaneControlType.List, "DesignBy"));
            approval.Fields.Add(Field("checkedby", "Checked By", "CheckedBy", 1));
            approval.Fields.Add(Field("checkeddate", "Checked Date", "CheckedDate", 2, PropertyPaneControlType.Date));

            PropertyPaneLayout layout = new PropertyPaneLayout
            {
                DocumentKind = PropertyPaneDocumentKind.Drawing,
                Groups = new List<PropertyPaneGroup> { titles, drawing, approval }
            };
            foreach (PropertyPaneField field in layout.Groups.SelectMany(g => g.Fields))
            {
                field.AllowedScopes = new List<PropertyPaneScope> { PropertyPaneScope.Document };
                field.DefaultScope = PropertyPaneScope.Document;
            }
            profile.Layouts.Add(layout);
        }

        private static void AddDrawingMappings(PropertyPaneProfile profile)
        {
            const string rows = @"Cabin Compact (1) A|M|Room Layout|D.520.0053.408.562
Cabin Compact (1) A|M|Ceiling Layout|D.520.0053.408.562
Cabin Compact (1) A|M|Wall Layout|D.520.0053.408.562
Cabin Compact (1) A|S|Room Layout|D.520.0053.408.562
Cabin Compact (1) A|S|Ceiling Layout|D.520.0053.408.562
Cabin Compact (1) A|S|Wall Layout|D.520.0053.408.562
Cabin Compact (1) A|PM|Room Layout|D.520.0053.408.562
Cabin Compact (1) A|PM|Ceiling Layout|D.520.0053.408.562
Cabin Compact (1) A|PM|Wall Layout|D.520.0053.408.562
Cabin Compact (1) A|PS|Room Layout|D.520.0053.408.562
Cabin Compact (1) A|PS|Ceiling Layout|D.520.0053.408.562
Cabin Compact (1) A|PS|Wall Layout|D.520.0053.408.562
Cabin Compact (1) B|M|Room Layout|D.520.0044.408.563
Cabin Compact (1) B|M|Ceiling Layout|D.520.0044.408.563
Cabin Compact (1) B|M|Wall Layout|D.520.0044.408.563
Cabin Compact (1) B|S|Room Layout|D.520.0044.408.563
Cabin Compact (1) B|S|Ceiling Layout|D.520.0044.408.563
Cabin Compact (1) B|S|Wall Layout|D.520.0044.408.563
Cabin Compact (2)|S|Room Layout|D.520.0053.408.561
Cabin Compact (2)|S|Ceiling Layout|D.520.0053.408.561
Cabin Compact (2)|S|Wall Layout|D.520.0053.408.561
Cabin Standard (1)|S|Room Layout|D.520.0044.408.562
Cabin Standard (1)|S|Ceiling Layout|D.520.0044.408.562
Cabin Standard (1)|S|Wall Layout|D.520.0044.408.562
Cabin Standard (1)|PS|Room Layout|D.520.0044.408.562
Cabin Standard (1)|PS|Ceiling Layout|D.520.0044.408.562
Cabin Standard (1)|PS|Wall Layout|D.520.0044.408.562
Cabin Compact+ (1) A|S|Room Layout|D.520.0053.408.563
Cabin Compact+ (1) A|S|Ceiling Layout|D.520.0053.408.563
Cabin Compact+ (1) A|S|Wall Layout|D.520.0053.408.563
Cabin Compact+ (1) A|M|Room Layout|D.520.0053.408.563
Cabin Compact+ (1) A|M|Ceiling Layout|D.520.0053.408.563
Cabin Compact+ (1) A|M|Wall Layout|D.520.0053.408.563
Cabin Compact+ (1) B|S|Room Layout|D.520.0044.408.564
Cabin Compact+ (1) B|S|Ceiling Layout|D.520.0044.408.564
Cabin Compact+ (1) B|S|Wall Layout|D.520.0044.408.564
Cabin Compact+ (1) B|M|Room Layout|D.520.0044.408.564
Cabin Compact+ (1) B|M|Ceiling Layout|D.520.0044.408.564
Cabin Compact+ (1) B|M|Wall Layout|D.520.0044.408.564
Cabin Compact+ (1) B|PS|Room Layout|D.520.0044.408.564
Cabin Compact+ (1) B|PS|Ceiling Layout|D.520.0044.408.564
Cabin Compact+ (1) B|PS|Wall Layout|D.520.0044.408.564
Cabin Crew Comfort (1)|S|Room Layout|D.520.0061.408.564
Cabin Crew Comfort (1)|S|Ceiling Layout|D.520.0061.408.564
Cabin Crew Comfort (1)|S|Wall Layout|D.520.0061.408.564
Cabin Crew Comfort (1)|PS|Room Layout|D.520.0061.408.564
Cabin Crew Comfort (1)|PS|Ceiling Layout|D.520.0061.408.564
Cabin Crew Comfort (1)|PS|Wall Layout|D.520.0061.408.564
Cabin Officer Standard (1)|S|Room Layout|D.520.0061.408.563
Cabin Officer Standard (1)|S|Ceiling Layout|D.520.0061.408.563
Cabin Officer Standard (1)|S|Wall Layout|D.520.0061.408.563
Cabin Officer Comfort (1)|S|Room Layout|D.520.0061.408.562
Cabin Officer Comfort (1)|S|Ceiling Layout|D.520.0061.408.562
Cabin Officer Comfort (1)|S|Wall Layout|D.520.0061.408.562
Cabin Senior Standard (1)|S|Room Layout|D.520.0075.408.563
Cabin Senior Standard (1)|S|Ceiling Layout|D.520.0075.408.563
Cabin Senior Standard (1)|S|Wall Layout|D.520.0075.408.563
Cabin Senior Comfort (1)|S|Room Layout|D.520.0075.408.562
Cabin Senior Comfort (1)|S|Ceiling Layout|D.520.0075.408.562
Cabin Senior Comfort (1)|S|Wall Layout|D.520.0075.408.562
Cabin Chief Officer & Engineer (1)|Dayroom|Room Layout|D.520.0084.408.567
Cabin Chief Officer & Engineer (1)|Dayroom|Ceiling Layout|D.520.0084.408.567
Cabin Chief Officer & Engineer (1)|Dayroom|Wall Layout|D.520.0084.408.567
Cabin Chief Officer & Engineer (1)|Sleeping Room|Room Layout|D.520.0084.408.562
Cabin Chief Officer & Engineer (1)|Sleeping Room|Ceiling Layout|D.520.0084.408.562
Cabin Chief Officer & Engineer (1)|Sleeping Room|Wall Layout|D.520.0084.408.562
Cabin Captain (1)|Dayroom|Room Layout|D.520.0084.408.564
Cabin Captain (1)|Dayroom|Ceiling Layout|D.520.0084.408.564
Cabin Captain (1)|Dayroom|Wall Layout|D.520.0084.408.564
Cabin Captain (1)|Sleeping Room|Room Layout|D.520.0084.408.563
Cabin Captain (1)|Sleeping Room|Ceiling Layout|D.520.0084.408.563
Cabin Captain (1)|Sleeping Room|Wall Layout|D.520.0084.408.563";

            foreach (string line in rows.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] values = line.Split('|');
                if (values.Length == 4)
                {
                    profile.DrawingNumberMappings.Add(new DrawingNumberMap
                    {
                        CabinDescription = values[0],
                        CabinDefined = values[1],
                        LayoutType = values[2],
                        DrawingNumber = values[3]
                    });
                }
            }
        }

        private static PropertyPaneNamedList List(string name, params string[] values)
        {
            return new PropertyPaneNamedList { Name = name, Values = values.ToList() };
        }

        private static PropertyPaneGroup Group(string id, string label, int order)
        {
            return new PropertyPaneGroup { Id = id, Label = label, Order = order, Collapsed = false };
        }

        private static PropertyPaneField Field(string id, string label, string propertyName, int order,
            PropertyPaneControlType controlType = PropertyPaneControlType.Text, string listName = null,
            string defaultExpression = null, string automaticValue = null,
            PropertyPaneScope defaultScope = PropertyPaneScope.Document)
        {
            bool readOnly = controlType == PropertyPaneControlType.ReadOnly ||
                            controlType == PropertyPaneControlType.ReadOnlyPreview ||
                            controlType == PropertyPaneControlType.Automatic;
            return new PropertyPaneField
            {
                Id = id,
                Label = label,
                PropertyName = propertyName,
                Order = order,
                ControlType = controlType,
                ListName = listName,
                ReadOnly = readOnly,
                AutomaticValue = automaticValue,
                DefaultExpression = defaultExpression,
                DefaultScope = defaultScope,
                BlankMeansNoChange = true,
                AllowExplicitClear = !readOnly,
                AllowedScopes = readOnly
                    ? new List<PropertyPaneScope> { PropertyPaneScope.Document }
                    : new List<PropertyPaneScope> { PropertyPaneScope.Document, PropertyPaneScope.ActiveConfiguration, PropertyPaneScope.SelectedConfigurations, PropertyPaneScope.AllConfigurations }
            };
        }
    }
}
