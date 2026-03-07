# Dynamic ReportParametersObject Generation - Design

## Problem

XAF reports with runtime parameters don't integrate well with background scheduling (Hangfire). The current approach in wlncentral stores parameters as flat string key-value pairs, bypassing XAF's `ReportParametersObjectBase` infrastructure entirely. This means no native Detail View for parameter editing, no type-safe lookups, and fragile string-based type conversion.

## Solution

Dynamically generate `ReportParametersObjectBase` subclasses from XtraReport parameter definitions using Roslyn compilation (same pattern as XafDynamicAssemblies). Support both runtime-only types and graduation to compiled source files.

## Architecture

### Data Model

```
ReportParameterDefinition (1:1 with ReportDataV2)
├── Id: Guid
├── Report: ReportDataV2 (FK)
├── GeneratedClassName: string
├── GeneratedSource: string (null while runtime-only)
├── Status: enum (Runtime | Graduating | Compiled)
├── ParameterSignatureHash: string (detects report definition drift)
├── IsStale: bool (set when report params change)
└── Fields: IList<ReportParameterFieldDefinition>

ReportParameterFieldDefinition
├── Id: Guid
├── ParameterName: string (XtraReport parameter name)
├── PropertyName: string (generated C# property name)
├── ClrTypeName: string ("System.DateTime", etc.)
├── ReferencedTypeName: string (for lookups: "MyApp.Customer")
├── IsRequired: bool
├── DefaultValue: string (serialized)
├── IncludeInCriteria: bool
└── CriteriaPropertyPath: string (e.g. "Customer.Id")
```

### Code Generation Pipeline

Triggered by explicit "Generate Parameter Object" action on a report:

1. **Inspect** - Load XtraReport, iterate visible `Parameters`, determine types (scalar vs lookup)
2. **Store metadata** - Create/update `ReportParameterDefinition` + field rows, compute signature hash
3. **Generate source** - Emit `[DomainComponent]` class inheriting `ReportParametersObjectBase`:
   - Constructor takes `IObjectSpaceCreator`
   - Typed properties for each parameter
   - Lookup properties load via `ObjectSpace`
   - `CreateObjectSpace()` override
   - `GetCriteria()` builds `CriteriaOperator` from properties marked `IncludeInCriteria`
   - `GetSorting()` returns empty array
4. **Compile** - Roslyn `CSharpCompilation.Create()`, load into `AssemblyLoadContext`, register with `XafTypesInfo`
5. **Associate** - Set `IReportDataV2.ParametersObjectType` to the generated type

Process restart required after generation (same as XafDynamicAssemblies).

### Stale Detection

- Store hash of report parameter signature (names + types) on `ReportParameterDefinition`
- On report save or on "Generate" action, compare current hash with stored hash
- If different, set `IsStale = true` and show warning
- Generated `GetCriteria()` is defensive: missing parameters are ignored, extra parameters use defaults
- User explicitly regenerates when ready

### Hangfire Integration (for wlncentral port)

```csharp
public record GenerateReportCommand(
    string ReportKey,
    string FileType = "Pdf",
    string? ParametersObjectTypeName = null,
    string? SerializedParameterValues = null,  // JSON
    string? EmailRecipients = null,
    Guid? ScheduledReportId = null);
```

Handler flow:
1. Resolve type from `ParametersObjectTypeName` (loaded via EarlyBootstrap)
2. `Activator.CreateInstance(type, objectSpaceCreator)`
3. Deserialize JSON, set properties (scalars directly, lookups by Guid via ObjectSpace)
4. `IReportDataSourceHelper.SetXafReportParametersObject(report, parametersObject)`
5. Report engine calls `GetCriteria()`/`GetSorting()` natively

### Graduation Path

Same as XafDynamicAssemblies:
1. "Graduate" action generates clean C# source, stores in `GeneratedSource`
2. Status -> Graduating
3. Developer copies file to Module project
4. After build, Status -> Compiled
5. Compiled types excluded from Roslyn generation

## Prototype Scope

Build in XafReportParametersObjects project:

1. Sample business objects (Customer, Order) for report data and lookup testing
2. Sample XtraReport with parameters (string, DateTime, Customer lookup)
3. Core services:
   - `ReportParameterInspector` - extract parameter metadata from XtraReport
   - `ReportParameterSourceGenerator` - generate C# class source
   - `ReportParameterCompiler` - Roslyn compilation + ALC loading
   - `ReportParameterGraduationService` - generate graduation source
4. Metadata entities (`ReportParameterDefinition`, `ReportParameterFieldDefinition`)
5. Controller with "Generate Parameter Object" action
6. Stale detection (signature hash + IsStale flag)

Out of scope: Hangfire integration, process restart orchestration, EarlyBootstrap (wlncentral concerns).
