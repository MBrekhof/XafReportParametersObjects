using DevExpress.XtraReports.UI;
using System.Security.Cryptography;
using System.Text;

namespace XafReportParametersObjects.Module.Services;

public record ParameterInfo(
    string Name,
    string PropertyName,
    Type ClrType,
    string? ReferencedTypeName,
    bool IsLookup,
    object? DefaultValue);

public record InspectionResult(
    List<ParameterInfo> Parameters,
    string SignatureHash);

public static class ReportParameterInspector
{
    private static readonly HashSet<string> KnownBaseTypes = new()
    {
        "DevExpress.Persistent.BaseImpl.EF.BaseObject",
        "DevExpress.Persistent.BaseImpl.BaseObject"
    };

    public static InspectionResult Inspect(XtraReport report)
    {
        var parameters = new List<ParameterInfo>();
        var signatureParts = new List<string>();

        foreach (var param in report.Parameters.Cast<DevExpress.XtraReports.Parameters.Parameter>())
        {
            // Inspect all parameters, not just visible ones.
            // Parameters should be Visible=false to prevent the report viewer
            // from showing its own parameter panel (XAF handles the UI instead).

            var clrType = param.Type ?? typeof(string);
            var isLookup = IsBusinessObjectType(clrType);
            var referencedTypeName = isLookup ? clrType.FullName : null;
            var propertyName = SanitizePropertyName(param.Name);

            parameters.Add(new ParameterInfo(
                Name: param.Name,
                PropertyName: propertyName,
                ClrType: clrType,
                ReferencedTypeName: referencedTypeName,
                IsLookup: isLookup,
                DefaultValue: param.Value));

            signatureParts.Add($"{param.Name}:{clrType.FullName}");
        }

        signatureParts.Sort(StringComparer.Ordinal);
        var hash = ComputeHash(string.Join("|", signatureParts));

        return new InspectionResult(parameters, hash);
    }

    private static bool IsBusinessObjectType(Type type)
    {
        var current = type.BaseType;
        while (current is not null)
        {
            if (KnownBaseTypes.Contains(current.FullName ?? ""))
                return true;
            current = current.BaseType;
        }
        return false;
    }

    private static string SanitizePropertyName(string name)
    {
        var sb = new StringBuilder();
        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c))
                sb.Append(sb.Length == 0 ? char.ToUpperInvariant(c) : c);
        }
        return sb.Length > 0 ? sb.ToString() : "Parameter";
    }

    private static string ComputeHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes)[..16];
    }
}
