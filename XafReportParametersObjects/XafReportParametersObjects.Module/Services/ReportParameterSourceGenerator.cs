using System.Text;

namespace XafReportParametersObjects.Module.Services;

public static class ReportParameterSourceGenerator
{
    private const string Namespace = "XafReportParametersObjects.Module.GeneratedParameters";

    public static string Generate(string className, List<ParameterInfo> parameters, Type reportDataSourceType)
    {
        var sb = new StringBuilder();

        sb.AppendLine("using DevExpress.ExpressApp;");
        sb.AppendLine("using DevExpress.ExpressApp.DC;");
        sb.AppendLine("using DevExpress.ExpressApp.ReportsV2;");
        sb.AppendLine("using DevExpress.Data.Filtering;");
        sb.AppendLine("using DevExpress.Xpo.DB;");
        sb.AppendLine("using System.ComponentModel;");
        sb.AppendLine();
        sb.AppendLine($"namespace {Namespace};");
        sb.AppendLine();
        sb.AppendLine("[DomainComponent]");
        sb.AppendLine($"public class {className} : ReportParametersObjectBase");
        sb.AppendLine("{");

        // Constructor
        sb.AppendLine($"    public {className}(IObjectSpaceCreator provider) : base(provider)");
        sb.AppendLine("    {");
        sb.AppendLine("    }");
        sb.AppendLine();

        // Properties
        foreach (var param in parameters)
        {
            var typeName = param.IsLookup
                ? param.ClrType.FullName!
                : GetSimpleTypeName(param.ClrType);

            if (param.IsLookup)
            {
                sb.AppendLine($"    public {typeName}? {param.PropertyName} {{ get; set; }}");
            }
            else
            {
                var defaultSuffix = GetDefaultValueSuffix(param.ClrType);
                sb.AppendLine($"    public {typeName} {param.PropertyName} {{ get; set; }}{defaultSuffix}");
            }
        }

        sb.AppendLine();

        // CreateObjectSpace
        sb.AppendLine("    protected override IObjectSpace CreateObjectSpace()");
        sb.AppendLine("    {");
        sb.AppendLine($"        return objectSpaceCreator.CreateObjectSpace(typeof({reportDataSourceType.FullName}));");
        sb.AppendLine("    }");
        sb.AppendLine();

        // GetCriteria
        GenerateGetCriteria(sb, parameters);

        // GetSorting
        sb.AppendLine("    public override SortProperty[] GetSorting()");
        sb.AppendLine("    {");
        sb.AppendLine("        return Array.Empty<SortProperty>();");
        sb.AppendLine("    }");

        sb.AppendLine("}");

        return sb.ToString();
    }

    private static void GenerateGetCriteria(StringBuilder sb, List<ParameterInfo> parameters)
    {
        sb.AppendLine("    public override CriteriaOperator GetCriteria()");
        sb.AppendLine("    {");
        sb.AppendLine("        var criteria = new List<CriteriaOperator>();");
        sb.AppendLine();

        foreach (var param in parameters)
        {
            if (param.IsLookup)
            {
                sb.AppendLine($"        if ({param.PropertyName} is not null)");
                sb.AppendLine($"            criteria.Add(CriteriaOperator.Parse(\"{param.PropertyName}.ID = ?\", {param.PropertyName}.ID));");
            }
            else if (param.ClrType == typeof(string))
            {
                sb.AppendLine($"        if (!string.IsNullOrEmpty({param.PropertyName}))");
                sb.AppendLine($"            criteria.Add(CriteriaOperator.Parse(\"{param.PropertyName} = ?\", {param.PropertyName}));");
            }
            else if (param.ClrType == typeof(DateTime))
            {
                sb.AppendLine($"        if ({param.PropertyName} != default)");
                sb.AppendLine($"            criteria.Add(CriteriaOperator.Parse(\"{param.PropertyName} >= ?\", {param.PropertyName}));");
            }
            else
            {
                sb.AppendLine($"        criteria.Add(CriteriaOperator.Parse(\"{param.PropertyName} = ?\", {param.PropertyName}));");
            }
        }

        sb.AppendLine();
        sb.AppendLine("        return CriteriaOperator.And(criteria);");
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    private static string GetSimpleTypeName(Type type)
    {
        if (type == typeof(string)) return "string";
        if (type == typeof(int)) return "int";
        if (type == typeof(long)) return "long";
        if (type == typeof(decimal)) return "decimal";
        if (type == typeof(double)) return "double";
        if (type == typeof(float)) return "float";
        if (type == typeof(bool)) return "bool";
        if (type == typeof(DateTime)) return "DateTime";
        if (type == typeof(Guid)) return "Guid";
        return type.FullName ?? type.Name;
    }

    private static string GetDefaultValueSuffix(Type type)
    {
        if (type == typeof(string)) return " = string.Empty;";
        return "";
    }

    public static string GetFullTypeName(string className) => $"{Namespace}.{className}";
}
