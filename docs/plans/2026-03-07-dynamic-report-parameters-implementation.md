# Dynamic ReportParametersObject Generation - Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Dynamically generate `ReportParametersObjectBase` subclasses from XtraReport parameter definitions using Roslyn, with graduation to compiled source.

**Architecture:** Inspect XtraReport parameters -> store metadata -> generate C# source -> compile via Roslyn -> register with XAF TypesInfo -> associate with report. Same pattern as XafDynamicAssemblies but scoped to report parameter objects.

**Tech Stack:** .NET 8, DevExpress XAF 25.2.3, EF Core (SQL Server via localdb), Roslyn (Microsoft.CodeAnalysis 4.10.0), Blazor Server

---

### Task 1: Sample Business Objects (Customer, Order)

Create domain objects so we have something to report on and to test lookup parameters.

**Files:**
- Create: `XafReportParametersObjects/XafReportParametersObjects.Module/BusinessObjects/Customer.cs`
- Create: `XafReportParametersObjects/XafReportParametersObjects.Module/BusinessObjects/Order.cs`
- Modify: `XafReportParametersObjects/XafReportParametersObjects.Module/BusinessObjects/XafReportParametersObjectsDbContext.cs`

**Step 1: Create Customer entity**

```csharp
// Customer.cs
using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl.EF;
using System.ComponentModel;

namespace XafReportParametersObjects.Module.BusinessObjects;

[DefaultClassOptions]
[NavigationItem("SampleData")]
public class Customer : BaseObject
{
    public virtual string Name { get; set; } = string.Empty;
    public virtual string Email { get; set; } = string.Empty;
    public virtual string City { get; set; } = string.Empty;

    [DefaultValue(true)]
    public virtual bool IsActive { get; set; } = true;
}
```

**Step 2: Create Order entity**

```csharp
// Order.cs
using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl.EF;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace XafReportParametersObjects.Module.BusinessObjects;

[DefaultClassOptions]
[NavigationItem("SampleData")]
public class Order : BaseObject
{
    [Required]
    public virtual string OrderNumber { get; set; } = string.Empty;

    public virtual DateTime OrderDate { get; set; } = DateTime.Today;

    public virtual decimal Amount { get; set; }

    public virtual Customer? Customer { get; set; }

    public virtual string Description { get; set; } = string.Empty;
}
```

**Step 3: Register in DbContext**

Add to `XafReportParametersObjectsEFCoreDbContext`:

```csharp
public DbSet<Customer> Customers { get; set; }
public DbSet<Order> Orders { get; set; }
```

**Step 4: Add seed data in Updater**

Modify `DatabaseUpdate/Updater.cs` to seed a few customers and orders for testing:

```csharp
public override void UpdateDatabaseAfterUpdateSchema()
{
    base.UpdateDatabaseAfterUpdateSchema();

    if (ObjectSpace.FirstOrDefault<Customer>(c => c.Name == "Acme Corp") is null)
    {
        var acme = ObjectSpace.CreateObject<Customer>();
        acme.Name = "Acme Corp";
        acme.Email = "info@acme.com";
        acme.City = "Amsterdam";

        var globex = ObjectSpace.CreateObject<Customer>();
        globex.Name = "Globex";
        globex.Email = "info@globex.com";
        globex.City = "Rotterdam";

        var order1 = ObjectSpace.CreateObject<Order>();
        order1.OrderNumber = "ORD-001";
        order1.OrderDate = new DateTime(2026, 1, 15);
        order1.Amount = 1500m;
        order1.Customer = acme;
        order1.Description = "Widget batch";

        var order2 = ObjectSpace.CreateObject<Order>();
        order2.OrderNumber = "ORD-002";
        order2.OrderDate = new DateTime(2026, 2, 20);
        order2.Amount = 3200m;
        order2.Customer = globex;
        order2.Description = "Gadget shipment";

        var order3 = ObjectSpace.CreateObject<Order>();
        order3.OrderNumber = "ORD-003";
        order3.OrderDate = new DateTime(2026, 3, 1);
        order3.Amount = 750m;
        order3.Customer = acme;
        order3.Description = "Spare parts";

        ObjectSpace.CommitChanges();
    }
}
```

**Step 5: Build and verify**

Run: `dotnet build XafReportParametersObjects/XafReportParametersObjects.Blazor.Server/XafReportParametersObjects.Blazor.Server.csproj`
Expected: BUILD SUCCEEDED

**Step 6: Commit**

```bash
git add -A && git commit -m "feat: add Customer and Order sample business objects with seed data"
```

---

### Task 2: Metadata Entities (ReportParameterDefinition, ReportParameterFieldDefinition)

**Files:**
- Create: `XafReportParametersObjects/XafReportParametersObjects.Module/BusinessObjects/ReportParameterDefinition.cs`
- Create: `XafReportParametersObjects/XafReportParametersObjects.Module/BusinessObjects/ReportParameterFieldDefinition.cs`
- Create: `XafReportParametersObjects/XafReportParametersObjects.Module/BusinessObjects/ReportParameterStatus.cs`
- Modify: `XafReportParametersObjects/XafReportParametersObjects.Module/BusinessObjects/XafReportParametersObjectsDbContext.cs`

**Step 1: Create status enum**

```csharp
// ReportParameterStatus.cs
namespace XafReportParametersObjects.Module.BusinessObjects;

public enum ReportParameterStatus
{
    Runtime = 0,
    Graduating = 1,
    Compiled = 2
}
```

**Step 2: Create ReportParameterDefinition**

```csharp
// ReportParameterDefinition.cs
using DevExpress.ExpressApp.DC;
using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl.EF;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace XafReportParametersObjects.Module.BusinessObjects;

[DefaultClassOptions]
[NavigationItem("ReportParameters")]
public class ReportParameterDefinition : BaseObject
{
    public virtual ReportDataV2? Report { get; set; }

    [Required]
    [FieldSize(200)]
    public virtual string GeneratedClassName { get; set; } = string.Empty;

    [FieldSize(FieldSizeAttribute.Unlimited)]
    [Browsable(false)]
    public virtual string? GeneratedSource { get; set; }

    public virtual ReportParameterStatus Status { get; set; } = ReportParameterStatus.Runtime;

    [FieldSize(100)]
    [Browsable(false)]
    public virtual string? ParameterSignatureHash { get; set; }

    [DefaultValue(false)]
    public virtual bool IsStale { get; set; }

    [DevExpress.ExpressApp.DC.Aggregated]
    public virtual ObservableCollection<ReportParameterFieldDefinition> Fields { get; set; } = new();
}
```

**Step 3: Create ReportParameterFieldDefinition**

```csharp
// ReportParameterFieldDefinition.cs
using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl.EF;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace XafReportParametersObjects.Module.BusinessObjects;

public class ReportParameterFieldDefinition : BaseObject
{
    [Required]
    [FieldSize(100)]
    public virtual string ParameterName { get; set; } = string.Empty;

    [Required]
    [FieldSize(100)]
    public virtual string PropertyName { get; set; } = string.Empty;

    [Required]
    [FieldSize(200)]
    public virtual string ClrTypeName { get; set; } = "System.String";

    [FieldSize(200)]
    public virtual string? ReferencedTypeName { get; set; }

    [DefaultValue(false)]
    public virtual bool IsRequired { get; set; }

    [FieldSize(500)]
    public virtual string? DefaultValue { get; set; }

    [DefaultValue(true)]
    public virtual bool IncludeInCriteria { get; set; } = true;

    [FieldSize(200)]
    public virtual string? CriteriaPropertyPath { get; set; }

    public virtual ReportParameterDefinition? ReportParameterDefinition { get; set; }
}
```

**Step 4: Register in DbContext**

Add to `XafReportParametersObjectsEFCoreDbContext`:

```csharp
public DbSet<ReportParameterDefinition> ReportParameterDefinitions { get; set; }
public DbSet<ReportParameterFieldDefinition> ReportParameterFieldDefinitions { get; set; }
```

**Step 5: Build and verify**

Run: `dotnet build XafReportParametersObjects/XafReportParametersObjects.Blazor.Server/XafReportParametersObjects.Blazor.Server.csproj`
Expected: BUILD SUCCEEDED

**Step 6: Commit**

```bash
git add -A && git commit -m "feat: add ReportParameterDefinition and ReportParameterFieldDefinition metadata entities"
```

---

### Task 3: ReportParameterInspector Service

Extracts parameter metadata from an XtraReport loaded from ReportDataV2.

**Files:**
- Create: `XafReportParametersObjects/XafReportParametersObjects.Module/Services/ReportParameterInspector.cs`

**Step 1: Create the inspector**

```csharp
// ReportParameterInspector.cs
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
    /// <summary>
    /// Known XAF/EF business object base types. If a parameter's Type inherits from one of these,
    /// it's treated as a lookup rather than a scalar.
    /// </summary>
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
            if (!param.Visible) continue;

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
        // Remove non-alphanumeric characters, ensure starts with uppercase
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
        return Convert.ToHexString(bytes)[..16]; // Short hash is sufficient
    }
}
```

**Step 2: Build and verify**

Run: `dotnet build XafReportParametersObjects/XafReportParametersObjects.Module/XafReportParametersObjects.Module.csproj`
Expected: BUILD SUCCEEDED

**Step 3: Commit**

```bash
git add -A && git commit -m "feat: add ReportParameterInspector service for extracting parameter metadata"
```

---

### Task 4: ReportParameterSourceGenerator Service

Generates C# source for a `ReportParametersObjectBase` subclass from inspection results.

**Files:**
- Create: `XafReportParametersObjects/XafReportParametersObjects.Module/Services/ReportParameterSourceGenerator.cs`

**Step 1: Create the source generator**

```csharp
// ReportParameterSourceGenerator.cs
using System.Text;

namespace XafReportParametersObjects.Module.Services;

public static class ReportParameterSourceGenerator
{
    private const string Namespace = "XafReportParametersObjects.Module.GeneratedParameters";

    public static string Generate(string className, List<ParameterInfo> parameters, Type reportDataSourceType)
    {
        var sb = new StringBuilder();

        // Usings
        sb.AppendLine("using DevExpress.ExpressApp;");
        sb.AppendLine("using DevExpress.ExpressApp.DC;");
        sb.AppendLine("using DevExpress.ExpressApp.ReportsV2;");
        sb.AppendLine("using DevExpress.Data.Filtering;");
        sb.AppendLine("using DevExpress.Xpo.DB;");
        sb.AppendLine("using System.ComponentModel;");
        sb.AppendLine();

        // Namespace
        sb.AppendLine($"namespace {Namespace};");
        sb.AppendLine();

        // Class
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

        var criteriaParams = parameters.Where(p => p.CriteriaPropertyPath is not null || p.IsLookup).ToList();
        // For the generated version, we build criteria for all parameters
        // Scalars: "[PropertyPath] = ?" with the property value
        // Lookups: "[PropertyPath] = ?" with the object's key

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

    /// <summary>
    /// Generates the full namespace-qualified type name for a generated class.
    /// </summary>
    public static string GetFullTypeName(string className) => $"{Namespace}.{className}";
}
```

**Step 2: Build and verify**

Run: `dotnet build XafReportParametersObjects/XafReportParametersObjects.Module/XafReportParametersObjects.Module.csproj`
Expected: BUILD SUCCEEDED

**Step 3: Commit**

```bash
git add -A && git commit -m "feat: add ReportParameterSourceGenerator for C# class generation"
```

---

### Task 5: ReportParameterCompiler Service

Compiles generated C# source via Roslyn and loads into an AssemblyLoadContext.

**Files:**
- Create: `XafReportParametersObjects/XafReportParametersObjects.Module/Services/ReportParameterCompiler.cs`

**Step 1: Create the compiler**

```csharp
// ReportParameterCompiler.cs
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Reflection;
using System.Runtime.Loader;

namespace XafReportParametersObjects.Module.Services;

public sealed class ReportParameterCompiler
{
    private AssemblyLoadContext? _loadContext;
    private Assembly? _currentAssembly;

    public Assembly? CurrentAssembly => _currentAssembly;

    public CompilationResult Compile(string source, string assemblyName = "DynamicReportParameters")
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        // Collect references from all currently loaded assemblies
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToList();

        var compilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees: new[] { syntaxTree },
            references: references,
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release));

        using var ms = new MemoryStream();
        var result = compilation.Emit(ms);

        if (!result.Success)
        {
            var errors = result.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString())
                .ToList();

            return CompilationResult.Failed(errors);
        }

        ms.Seek(0, SeekOrigin.Begin);

        // Unload previous context if exists
        _loadContext = new AssemblyLoadContext(assemblyName, isCollectible: false);
        _currentAssembly = _loadContext.LoadFromStream(ms);

        return CompilationResult.Succeeded(_currentAssembly);
    }

    public Type? GetGeneratedType(string fullTypeName)
    {
        return _currentAssembly?.GetType(fullTypeName);
    }
}

public record CompilationResult
{
    public bool Success { get; init; }
    public Assembly? Assembly { get; init; }
    public List<string> Errors { get; init; } = new();

    public static CompilationResult Succeeded(Assembly assembly) =>
        new() { Success = true, Assembly = assembly };

    public static CompilationResult Failed(List<string> errors) =>
        new() { Success = false, Errors = errors };
}
```

**Step 2: Build and verify**

Run: `dotnet build XafReportParametersObjects/XafReportParametersObjects.Module/XafReportParametersObjects.Module.csproj`
Expected: BUILD SUCCEEDED

**Step 3: Commit**

```bash
git add -A && git commit -m "feat: add ReportParameterCompiler for Roslyn compilation and ALC loading"
```

---

### Task 6: ReportParameterGraduationService

Generates clean, hand-formatted C# source for graduation (saving to disk).

**Files:**
- Create: `XafReportParametersObjects/XafReportParametersObjects.Module/Services/ReportParameterGraduationService.cs`

**Step 1: Create the graduation service**

```csharp
// ReportParameterGraduationService.cs
using System.Text;
using XafReportParametersObjects.Module.BusinessObjects;

namespace XafReportParametersObjects.Module.Services;

public static class ReportParameterGraduationService
{
    public static string GenerateGraduationSource(ReportParameterDefinition definition)
    {
        var sb = new StringBuilder();

        sb.AppendLine("// ==========================================================================");
        sb.AppendLine($"// Graduated from dynamic ReportParameterDefinition: {definition.GeneratedClassName}");
        sb.AppendLine($"// Report: {definition.Report?.DisplayName ?? "(unknown)"}");
        sb.AppendLine($"// Generated: {DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine("//");
        sb.AppendLine("// This file was generated by the ReportParameterObject generator.");
        sb.AppendLine("// You can safely modify GetCriteria(), GetSorting(), and add validation.");
        sb.AppendLine("// ==========================================================================");
        sb.AppendLine();
        sb.AppendLine("using DevExpress.ExpressApp;");
        sb.AppendLine("using DevExpress.ExpressApp.DC;");
        sb.AppendLine("using DevExpress.ExpressApp.ReportsV2;");
        sb.AppendLine("using DevExpress.Data.Filtering;");
        sb.AppendLine("using DevExpress.Xpo.DB;");
        sb.AppendLine("using System.ComponentModel;");
        sb.AppendLine();
        sb.AppendLine("namespace XafReportParametersObjects.Module.ReportParameters;");
        sb.AppendLine();
        sb.AppendLine("[DomainComponent]");
        sb.AppendLine($"public class {definition.GeneratedClassName} : ReportParametersObjectBase");
        sb.AppendLine("{");

        // Constructor
        sb.AppendLine($"    public {definition.GeneratedClassName}(IObjectSpaceCreator provider) : base(provider)");
        sb.AppendLine("    {");
        sb.AppendLine("    }");
        sb.AppendLine();

        // Properties
        foreach (var field in definition.Fields)
        {
            var isLookup = !string.IsNullOrEmpty(field.ReferencedTypeName);
            var typeName = isLookup ? field.ReferencedTypeName! : GetSimpleTypeName(field.ClrTypeName);

            if (isLookup)
            {
                sb.AppendLine($"    public {typeName}? {field.PropertyName} {{ get; set; }}");
            }
            else
            {
                var defaultSuffix = field.ClrTypeName == "System.String" ? " = string.Empty;" : "";
                sb.AppendLine($"    public {typeName} {field.PropertyName} {{ get; set; }}{defaultSuffix}");
            }
        }

        sb.AppendLine();

        // CreateObjectSpace - TODO: customize the type for your report's data source
        sb.AppendLine("    protected override IObjectSpace CreateObjectSpace()");
        sb.AppendLine("    {");
        sb.AppendLine("        // TODO: Change the type argument to match your report's data source type");
        sb.AppendLine("        return objectSpaceCreator.CreateObjectSpace(typeof(object));");
        sb.AppendLine("    }");
        sb.AppendLine();

        // GetCriteria
        sb.AppendLine("    public override CriteriaOperator GetCriteria()");
        sb.AppendLine("    {");
        sb.AppendLine("        var criteria = new List<CriteriaOperator>();");
        sb.AppendLine();

        foreach (var field in definition.Fields.Where(f => f.IncludeInCriteria))
        {
            var isLookup = !string.IsNullOrEmpty(field.ReferencedTypeName);
            var path = field.CriteriaPropertyPath ?? field.PropertyName;

            if (isLookup)
            {
                sb.AppendLine($"        if ({field.PropertyName} is not null)");
                sb.AppendLine($"            criteria.Add(CriteriaOperator.Parse(\"{path} = ?\", {field.PropertyName}.ID));");
            }
            else if (field.ClrTypeName == "System.String")
            {
                sb.AppendLine($"        if (!string.IsNullOrEmpty({field.PropertyName}))");
                sb.AppendLine($"            criteria.Add(CriteriaOperator.Parse(\"{path} = ?\", {field.PropertyName}));");
            }
            else if (field.ClrTypeName == "System.DateTime")
            {
                sb.AppendLine($"        if ({field.PropertyName} != default)");
                sb.AppendLine($"            criteria.Add(CriteriaOperator.Parse(\"{path} >= ?\", {field.PropertyName}));");
            }
            else
            {
                sb.AppendLine($"        criteria.Add(CriteriaOperator.Parse(\"{path} = ?\", {field.PropertyName}));");
            }
        }

        sb.AppendLine();
        sb.AppendLine("        return CriteriaOperator.And(criteria);");
        sb.AppendLine("    }");
        sb.AppendLine();

        // GetSorting
        sb.AppendLine("    public override SortProperty[] GetSorting()");
        sb.AppendLine("    {");
        sb.AppendLine("        // TODO: Add sorting properties as needed");
        sb.AppendLine("        return Array.Empty<SortProperty>();");
        sb.AppendLine("    }");

        sb.AppendLine("}");

        return sb.ToString();
    }

    private static string GetSimpleTypeName(string clrTypeName) => clrTypeName switch
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
```

**Step 2: Build and verify**

Run: `dotnet build XafReportParametersObjects/XafReportParametersObjects.Module/XafReportParametersObjects.Module.csproj`
Expected: BUILD SUCCEEDED

**Step 3: Commit**

```bash
git add -A && git commit -m "feat: add ReportParameterGraduationService for generating compilable source"
```

---

### Task 7: GenerateParameterObjectController

XAF controller with "Generate Parameter Object" and "Graduate" actions on the ReportDataV2 detail view.

**Files:**
- Create: `XafReportParametersObjects/XafReportParametersObjects.Module/Controllers/GenerateParameterObjectController.cs`

**Step 1: Create the controller**

```csharp
// GenerateParameterObjectController.cs
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Actions;
using DevExpress.ExpressApp.ReportsV2;
using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl.EF;
using DevExpress.XtraReports.UI;
using XafReportParametersObjects.Module.BusinessObjects;
using XafReportParametersObjects.Module.Services;

namespace XafReportParametersObjects.Module.Controllers;

public class GenerateParameterObjectController : ViewController<DetailView>
{
    private readonly SimpleAction _generateAction;
    private readonly SimpleAction _graduateAction;

    public GenerateParameterObjectController()
    {
        TargetObjectType = typeof(ReportParameterDefinition);

        _generateAction = new SimpleAction(this, "GenerateParameterObject", PredefinedCategory.Edit)
        {
            Caption = "Generate Parameter Object",
            ToolTip = "Inspect the linked report and generate a ReportParametersObjectBase subclass",
            ImageName = "Action_Reload"
        };
        _generateAction.Execute += GenerateAction_Execute;

        _graduateAction = new SimpleAction(this, "GraduateParameterObject", PredefinedCategory.Edit)
        {
            Caption = "Graduate",
            ToolTip = "Generate compilable C# source for this parameter object",
            ImageName = "Action_Export"
        };
        _graduateAction.Execute += GraduateAction_Execute;
    }

    private void GenerateAction_Execute(object sender, SimpleActionExecuteEventArgs e)
    {
        var definition = (ReportParameterDefinition)View.CurrentObject;

        if (definition.Report is null)
        {
            Application.ShowViewStrategy.ShowMessage("Please link a Report before generating.");
            return;
        }

        // Load the XtraReport from ReportDataV2
        var reportDataSourceHelper = Application.ServiceProvider.GetService(
            typeof(IReportDataSourceHelper)) as IReportDataSourceHelper;

        XtraReport? report = null;
        try
        {
            var reportStorage = Application.ServiceProvider.GetService(typeof(IReportStorage)) as IReportStorage;
            if (reportStorage is not null)
            {
                report = reportStorage.LoadReport(definition.Report);
            }
        }
        catch (Exception ex)
        {
            Application.ShowViewStrategy.ShowMessage($"Failed to load report: {ex.Message}");
            return;
        }

        if (report is null)
        {
            Application.ShowViewStrategy.ShowMessage("Could not load the report definition.");
            return;
        }

        try
        {
            // Inspect
            var result = ReportParameterInspector.Inspect(report);

            if (result.Parameters.Count == 0)
            {
                Application.ShowViewStrategy.ShowMessage("The report has no visible parameters.");
                return;
            }

            // Check for stale
            if (definition.ParameterSignatureHash is not null &&
                definition.ParameterSignatureHash != result.SignatureHash)
            {
                definition.IsStale = false; // We're regenerating, so clear stale flag
            }

            // Update metadata
            definition.ParameterSignatureHash = result.SignatureHash;
            definition.Fields.Clear();

            foreach (var param in result.Parameters)
            {
                var field = View.ObjectSpace.CreateObject<ReportParameterFieldDefinition>();
                field.ParameterName = param.Name;
                field.PropertyName = param.PropertyName;
                field.ClrTypeName = param.ClrType.FullName ?? "System.String";
                field.ReferencedTypeName = param.ReferencedTypeName;
                field.IsRequired = false;
                field.DefaultValue = param.DefaultValue?.ToString();
                field.IncludeInCriteria = true;
                field.CriteriaPropertyPath = param.IsLookup ? $"{param.PropertyName}.ID" : param.PropertyName;
                definition.Fields.Add(field);
            }

            // Generate source
            var dataSourceType = report.DataSource?.GetType() ?? typeof(object);
            // Try to find the actual business object type from the report's data source
            var boType = typeof(object);
            if (report.DataSource is DevExpress.Persistent.Base.ReportsV2.CollectionDataSource cds)
            {
                boType = cds.ObjectType ?? typeof(object);
            }

            var source = ReportParameterSourceGenerator.Generate(
                definition.GeneratedClassName,
                result.Parameters,
                boType);

            // Compile
            var compiler = new ReportParameterCompiler();
            var compilationResult = compiler.Compile(source, $"ReportParams_{definition.GeneratedClassName}");

            if (!compilationResult.Success)
            {
                var errors = string.Join("\n", compilationResult.Errors);
                Application.ShowViewStrategy.ShowMessage($"Compilation failed:\n{errors}");
                return;
            }

            // Get the generated type
            var fullTypeName = ReportParameterSourceGenerator.GetFullTypeName(definition.GeneratedClassName);
            var generatedType = compiler.GetGeneratedType(fullTypeName);

            if (generatedType is null)
            {
                Application.ShowViewStrategy.ShowMessage($"Type '{fullTypeName}' not found in compiled assembly.");
                return;
            }

            // Register with XAF TypesInfo
            var typesInfo = XafTypesInfo.Instance;
            typesInfo.RegisterEntity(generatedType);

            // Associate with report
            definition.Report.ParametersObjectType = generatedType;

            definition.Status = ReportParameterStatus.Runtime;
            definition.IsStale = false;

            View.ObjectSpace.CommitChanges();

            Application.ShowViewStrategy.ShowMessage(
                $"Generated '{definition.GeneratedClassName}' with {result.Parameters.Count} parameters. " +
                "Restart the application to fully activate the parameter object.");
        }
        finally
        {
            report.Dispose();
        }
    }

    private void GraduateAction_Execute(object sender, SimpleActionExecuteEventArgs e)
    {
        var definition = (ReportParameterDefinition)View.CurrentObject;

        if (definition.Fields.Count == 0)
        {
            Application.ShowViewStrategy.ShowMessage("No fields defined. Generate the parameter object first.");
            return;
        }

        definition.GeneratedSource = ReportParameterGraduationService.GenerateGraduationSource(definition);
        definition.Status = ReportParameterStatus.Graduating;
        View.ObjectSpace.CommitChanges();

        Application.ShowViewStrategy.ShowMessage(
            $"Graduation source generated. Copy the source from 'GeneratedSource' field " +
            "into your Module project and set Status to 'Compiled' after building.");
    }
}
```

**Step 2: Build and verify**

Run: `dotnet build XafReportParametersObjects/XafReportParametersObjects.Module/XafReportParametersObjects.Module.csproj`
Expected: BUILD SUCCEEDED

**Step 3: Commit**

```bash
git add -A && git commit -m "feat: add GenerateParameterObjectController with Generate and Graduate actions"
```

---

### Task 8: Stale Detection Controller

Monitors report changes and flags parameter definitions as stale when the report's parameter signature changes.

**Files:**
- Create: `XafReportParametersObjects/XafReportParametersObjects.Module/Controllers/ReportParameterStaleDetectionController.cs`

**Step 1: Create the stale detection controller**

```csharp
// ReportParameterStaleDetectionController.cs
using DevExpress.ExpressApp;
using DevExpress.Persistent.BaseImpl.EF;
using DevExpress.XtraReports.UI;
using XafReportParametersObjects.Module.BusinessObjects;
using XafReportParametersObjects.Module.Services;

namespace XafReportParametersObjects.Module.Controllers;

public class ReportParameterStaleDetectionController : ViewController<DetailView>
{
    public ReportParameterStaleDetectionController()
    {
        TargetObjectType = typeof(ReportParameterDefinition);
    }

    protected override void OnActivated()
    {
        base.OnActivated();
        View.ObjectSpace.ObjectChanged += ObjectSpace_ObjectChanged;
    }

    protected override void OnDeactivated()
    {
        View.ObjectSpace.ObjectChanged -= ObjectSpace_ObjectChanged;
        base.OnDeactivated();
    }

    private void ObjectSpace_ObjectChanged(object? sender, ObjectChangedEventArgs e)
    {
        if (e.Object is not ReportParameterDefinition definition) return;
        if (e.PropertyName != nameof(ReportParameterDefinition.Report)) return;
        if (definition.Report is null || string.IsNullOrEmpty(definition.ParameterSignatureHash)) return;

        // Check if the report's parameters have changed
        try
        {
            var reportStorage = Application.ServiceProvider.GetService(typeof(IReportStorage)) as IReportStorage;
            if (reportStorage is null) return;

            using var report = reportStorage.LoadReport(definition.Report);
            if (report is null) return;

            var result = ReportParameterInspector.Inspect(report);
            definition.IsStale = result.SignatureHash != definition.ParameterSignatureHash;
        }
        catch
        {
            // Non-critical: stale detection is advisory
        }
    }
}
```

**Note:** `IReportStorage` may need to be resolved differently depending on XAF version. If it doesn't compile, the controller should use `ReportDataProvider.ReportsStorage` or the report loading pattern from the Blazor module instead. Adjust during implementation.

**Step 2: Build and verify**

Run: `dotnet build XafReportParametersObjects/XafReportParametersObjects.Module/XafReportParametersObjects.Module.csproj`
Expected: BUILD SUCCEEDED

**Step 3: Commit**

```bash
git add -A && git commit -m "feat: add stale detection controller for report parameter definitions"
```

---

### Task 9: Integration Test - Build, Run, Verify

End-to-end verification that the app starts, shows the UI, and the generation flow compiles.

**Step 1: Build the entire solution**

Run: `dotnet build XafReportParametersObjects.slnx`
Expected: BUILD SUCCEEDED with no errors

**Step 2: Fix any compilation errors**

Address missing usings, type resolution issues, or API differences.

**Step 3: Commit final state**

```bash
git add -A && git commit -m "fix: resolve compilation issues from integration"
```

**Step 4: Push to remote**

```bash
git push origin master
```

---

### Task Summary

| Task | Component | Key Files |
|------|-----------|-----------|
| 1 | Sample Business Objects | `Customer.cs`, `Order.cs`, `Updater.cs` |
| 2 | Metadata Entities | `ReportParameterDefinition.cs`, `ReportParameterFieldDefinition.cs` |
| 3 | Inspector Service | `ReportParameterInspector.cs` |
| 4 | Source Generator | `ReportParameterSourceGenerator.cs` |
| 5 | Roslyn Compiler | `ReportParameterCompiler.cs` |
| 6 | Graduation Service | `ReportParameterGraduationService.cs` |
| 7 | Generate Controller | `GenerateParameterObjectController.cs` |
| 8 | Stale Detection | `ReportParameterStaleDetectionController.cs` |
| 9 | Integration Verify | Full solution build + push |

### Dependencies

```
Task 1 (Business Objects) ──┐
Task 2 (Metadata)          ──┼── Task 7 (Controller) ── Task 9 (Integration)
Task 3 (Inspector)         ──┤
Task 4 (Source Generator)  ──┤
Task 5 (Compiler)          ──┤
Task 6 (Graduation)        ──┘── Task 8 (Stale Detection)
```

Tasks 1-6 are independent and can be implemented in parallel. Tasks 7-8 depend on all prior tasks. Task 9 is the final integration step.
