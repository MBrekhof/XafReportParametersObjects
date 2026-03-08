using System.Text;
using XafReportParametersObjects.Module.BusinessObjects;

namespace XafReportParametersObjects.Module.Services;

public static class ReportParameterSourceGenerator
{
    public const string GeneratedNamespace = "XafReportParametersObjects.Module.GeneratedParameters";

    private sealed record GeneratedField(
        string PropertyName,
        string TypeName,
        bool IsLookup,
        Type ClrType,
        string? CriteriaPath);

    public static string Generate(string className, List<ParameterInfo> parameters, Type reportDataSourceType)
    {
        var fields = parameters.Select(param =>
        {
            var propertyName = SanitizePropertyName(param.PropertyName, param.Name);
            return new GeneratedField(
                PropertyName: propertyName,
                TypeName: param.IsLookup ? param.ClrType.FullName! : GetSimpleTypeName(param.ClrType),
                IsLookup: param.IsLookup,
                ClrType: param.ClrType,
                CriteriaPath: ResolveCriteriaPath(propertyName, reportDataSourceType));
        }).ToList();

        return GenerateCore(className, fields, reportDataSourceType);
    }

    public static string Generate(string className, IEnumerable<ReportParameterFieldDefinition> fieldDefinitions, Type reportDataSourceType)
    {
        var fields = fieldDefinitions.Select(field =>
        {
            var isLookup = !string.IsNullOrWhiteSpace(field.ReferencedTypeName);
            var clrType = ResolveClrType(field.ClrTypeName);
            var typeName = isLookup
                ? field.ReferencedTypeName!
                : GetSimpleTypeName(field.ClrTypeName);
            var propertyName = SanitizePropertyName(field.PropertyName, field.ParameterName);

            return new GeneratedField(
                PropertyName: propertyName,
                TypeName: typeName,
                IsLookup: isLookup,
                ClrType: clrType,
                CriteriaPath: ResolveCriteriaPath(propertyName, reportDataSourceType));
        }).ToList();

        return GenerateCore(className, fields, reportDataSourceType);
    }

    private static string GenerateCore(string className, List<GeneratedField> fields, Type reportDataSourceType)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using DevExpress.Data.Filtering;");
        sb.AppendLine("using DevExpress.ExpressApp;");
        sb.AppendLine("using DevExpress.ExpressApp.DC;");
        sb.AppendLine("using DevExpress.ExpressApp.ReportsV2;");
        sb.AppendLine("using DevExpress.Xpo;");
        sb.AppendLine("using System.ComponentModel;");
        sb.AppendLine();
        sb.AppendLine($"namespace {GeneratedNamespace};");
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
        foreach (var field in fields)
        {
            if (field.IsLookup)
                sb.AppendLine($"    public {field.TypeName}? {field.PropertyName} {{ get; set; }}");
            else
            {
                var defaultSuffix = GetDefaultValueSuffix(field.ClrType);
                sb.AppendLine($"    public {field.TypeName} {field.PropertyName} {{ get; set; }}{defaultSuffix}");
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
        GenerateGetCriteria(sb, fields);

        // GetSorting
        sb.AppendLine("    public override SortProperty[] GetSorting() => null;");

        sb.AppendLine("}");

        return sb.ToString();
    }

    private static void GenerateGetCriteria(StringBuilder sb, List<GeneratedField> fields)
    {
        var fieldsWithPath = fields.Where(f => !string.IsNullOrWhiteSpace(f.CriteriaPath)).ToList();

        if (fieldsWithPath.Count == 0)
        {
            sb.AppendLine("    public override CriteriaOperator GetCriteria() => null;");
            sb.AppendLine();
            return;
        }

        sb.AppendLine("    public override CriteriaOperator GetCriteria()");
        sb.AppendLine("    {");
        sb.AppendLine("        var criteria = new List<CriteriaOperator>();");
        sb.AppendLine();

        foreach (var field in fieldsWithPath)
        {
            var path = field.CriteriaPath!.Replace("\\", "\\\\").Replace("\"", "\\\"");

            if (field.IsLookup)
            {
                sb.AppendLine($"        if ({field.PropertyName} is not null)");
                sb.AppendLine($"            criteria.Add(CriteriaOperator.Parse(\"{path}.ID = ?\", {field.PropertyName}.ID));");
            }
            else if (field.ClrType == typeof(string))
            {
                sb.AppendLine($"        if (!string.IsNullOrEmpty({field.PropertyName}))");
                sb.AppendLine($"            criteria.Add(CriteriaOperator.Parse(\"{path} = ?\", {field.PropertyName}));");
            }
            else if (field.ClrType == typeof(DateTime))
            {
                sb.AppendLine($"        if ({field.PropertyName} != default)");
                sb.AppendLine($"            criteria.Add(CriteriaOperator.Parse(\"{path} >= ?\", {field.PropertyName}));");
            }
            else if (IsNumericType(field.ClrType) && field.PropertyName.StartsWith("Min", StringComparison.Ordinal))
            {
                sb.AppendLine($"        if ({field.PropertyName} != default)");
                sb.AppendLine($"            criteria.Add(CriteriaOperator.Parse(\"{path} >= ?\", {field.PropertyName}));");
            }
            else if (IsNumericType(field.ClrType) && field.PropertyName.StartsWith("Max", StringComparison.Ordinal))
            {
                sb.AppendLine($"        if ({field.PropertyName} != default)");
                sb.AppendLine($"            criteria.Add(CriteriaOperator.Parse(\"{path} <= ?\", {field.PropertyName}));");
            }
            else
            {
                sb.AppendLine($"        if ({field.PropertyName} != default)");
                sb.AppendLine($"            criteria.Add(CriteriaOperator.Parse(\"{path} = ?\", {field.PropertyName}));");
            }
        }

        sb.AppendLine();
        sb.AppendLine("        return CriteriaOperator.And(criteria);");
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    private static string? ResolveCriteriaPath(string propertyName, Type reportDataSourceType)
    {
        if (reportDataSourceType == typeof(object)) return null;

        // Exact match
        var exact = FindProperty(reportDataSourceType, propertyName);
        if (exact is not null) return exact.Name;

        // Range conventions: "MinAmount"/"MaxAmount" -> "Amount"
        if (propertyName.Length > 3 &&
            (propertyName.StartsWith("Min", StringComparison.Ordinal) ||
             propertyName.StartsWith("Max", StringComparison.Ordinal)))
        {
            var tail = propertyName[3..];
            var rangeProp = FindProperty(reportDataSourceType, tail);
            if (rangeProp is not null) return rangeProp.Name;
        }

        // Name convention: "CustomerName" -> "Customer.Name"
        if (propertyName.EndsWith("Name", StringComparison.Ordinal) && propertyName.Length > 4)
        {
            var navName = propertyName[..^4];
            var navProp = FindProperty(reportDataSourceType, navName);
            if (navProp is not null && FindProperty(navProp.PropertyType, "Name") is not null)
                return $"{navProp.Name}.Name";
        }

        return null;
    }

    private static System.Reflection.PropertyInfo? FindProperty(Type type, string name)
    {
        return type.GetProperties()
            .FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsNumericType(Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        return t == typeof(byte) || t == typeof(short) || t == typeof(int) ||
               t == typeof(long) || t == typeof(float) || t == typeof(double) ||
               t == typeof(decimal);
    }

    private static Type ResolveClrType(string clrTypeName)
    {
        return clrTypeName switch
        {
            "System.String" => typeof(string),
            "System.Int32" => typeof(int),
            "System.Int64" => typeof(long),
            "System.Decimal" => typeof(decimal),
            "System.Double" => typeof(double),
            "System.Single" => typeof(float),
            "System.Boolean" => typeof(bool),
            "System.DateTime" => typeof(DateTime),
            "System.Guid" => typeof(Guid),
            _ => Type.GetType(clrTypeName) ?? typeof(string)
        };
    }

    private static string SanitizePropertyName(string input, string fallback)
    {
        var source = string.IsNullOrWhiteSpace(input) ? fallback : input;
        var sb = new StringBuilder();
        var upperNext = false;

        foreach (var ch in source)
        {
            if (char.IsLetterOrDigit(ch) || ch == '_')
            {
                if (sb.Length == 0)
                {
                    if (!char.IsLetter(ch) && ch != '_')
                        sb.Append('P');
                    sb.Append(char.ToUpperInvariant(ch));
                }
                else
                {
                    sb.Append(upperNext ? char.ToUpperInvariant(ch) : ch);
                }
                upperNext = false;
            }
            else
            {
                upperNext = true;
            }
        }

        return sb.Length > 0 ? sb.ToString() : "Parameter";
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

    private static string GetSimpleTypeName(string clrTypeName)
    {
        return clrTypeName switch
        {
            "System.String" => "string",
            "System.Int32" => "int",
            "System.Int64" => "long",
            "System.Decimal" => "decimal",
            "System.Double" => "double",
            "System.Single" => "float",
            "System.Boolean" => "bool",
            "System.DateTime" => "DateTime",
            "System.Guid" => "Guid",
            _ => clrTypeName
        };
    }

    private static string GetDefaultValueSuffix(Type type)
    {
        if (type == typeof(string)) return " = string.Empty;";
        return "";
    }

    public static string GetFullTypeName(string className) => $"{GeneratedNamespace}.{className}";
}
