using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AutomacoesCivil3D
{
    public sealed class LoinConfiguration
    {
        [JsonPropertyName("schema")]
        public string Schema { get; set; } = "AutomacoesCivil3D.LOIN/v1";

        [JsonPropertyName("generatedAt")]
        public string GeneratedAt { get; set; } = string.Empty;

        [JsonPropertyName("sourceFile")]
        public string SourceFile { get; set; } = string.Empty;

        [JsonPropertyName("includedSheets")]
        public List<string> IncludedSheets { get; set; } = new();

        [JsonPropertyName("ignoredSheets")]
        public List<string> IgnoredSheets { get; set; } = new();

        [JsonPropertyName("propertySetDefinitions")]
        public List<LoinPsetDefinition> PropertySetDefinitions { get; set; } = new();

        [JsonPropertyName("layers")]
        public List<LoinLayerDefinition> Layers { get; set; } = new();

        [JsonPropertyName("elements")]
        public List<LoinElementDefinition> Elements { get; set; } = new();

        [JsonPropertyName("Mappings")]
        public List<IfcMappingRule> Mappings { get; set; } = new();

        [JsonPropertyName("diagnostics")]
        public List<LoinDiagnostic> Diagnostics { get; set; } = new();
    }

    public sealed class LoinPsetDefinition
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("group")]
        public string Group { get; set; } = string.Empty;

        [JsonPropertyName("properties")]
        public List<LoinPsetProperty> Properties { get; set; } = new();
    }

    public sealed class LoinPsetProperty
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("dataType")]
        public string DataType { get; set; } = "Text";

        [JsonPropertyName("sourceColumn")]
        public string SourceColumn { get; set; } = string.Empty;
    }

    public sealed class LoinLayerDefinition
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("color")]
        public LoinColorDefinition Color { get; set; } = new();

        [JsonPropertyName("disciplines")]
        public List<string> Disciplines { get; set; } = new();

        [JsonPropertyName("elements")]
        public List<string> Elements { get; set; } = new();
    }

    public sealed class LoinElementDefinition
    {
        [JsonPropertyName("discipline")]
        public string Discipline { get; set; } = string.Empty;

        [JsonPropertyName("sourceSheet")]
        public string SourceSheet { get; set; } = string.Empty;

        [JsonPropertyName("sourceRow")]
        public int SourceRow { get; set; }

        [JsonPropertyName("element")]
        public string Element { get; set; } = string.Empty;

        [JsonPropertyName("ifcClass")]
        public string IfcClass { get; set; } = string.Empty;

        [JsonPropertyName("predefinedType")]
        public string PredefinedType { get; set; } = string.Empty;

        [JsonPropertyName("classificationCode")]
        public string ClassificationCode { get; set; } = string.Empty;

        [JsonPropertyName("layer")]
        public string Layer { get; set; } = string.Empty;

        [JsonPropertyName("color")]
        public LoinColorDefinition Color { get; set; } = new();

        [JsonPropertyName("requiredElementProperties")]
        public List<string> RequiredElementProperties { get; set; } = new();

        [JsonPropertyName("requiredPhysicalProperties")]
        public List<string> RequiredPhysicalProperties { get; set; } = new();

        [JsonPropertyName("notApplicableProperties")]
        public List<string> NotApplicableProperties { get; set; } = new();

        [JsonPropertyName("rowValues")]
        public Dictionary<string, string> RowValues { get; set; } = new();
    }

    public sealed class LoinColorDefinition
    {
        [JsonPropertyName("raw")]
        public string Raw { get; set; } = string.Empty;

        [JsonPropertyName("red")]
        public int? Red { get; set; }

        [JsonPropertyName("green")]
        public int? Green { get; set; }

        [JsonPropertyName("blue")]
        public int? Blue { get; set; }

        [JsonPropertyName("fallbackAci")]
        public short? FallbackAci { get; set; }

        [JsonPropertyName("note")]
        public string Note { get; set; } = string.Empty;

        [JsonIgnore]
        public bool HasRgb => Red.HasValue && Green.HasValue && Blue.HasValue;
    }

    public sealed class LoinDiagnostic
    {
        [JsonPropertyName("severity")]
        public string Severity { get; set; } = "info";

        [JsonPropertyName("sheet")]
        public string Sheet { get; set; } = string.Empty;

        [JsonPropertyName("row")]
        public int Row { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;
    }
}
