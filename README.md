# XafReportParametersObjects

Generate XAF `ReportParametersObjectBase` classes **from report metadata at the push of a button** — no runtime Roslyn compilation, no hand-writing boilerplate parameter classes.

A DevExpress **XAF** (eXpressApp Framework) solution exploring [`ReportParametersObjectBase`](https://docs.devexpress.com/eXpressAppFramework/DevExpress.ExpressApp.ReportsV2.ReportParametersObjectBase) for **ReportsV2**: the user fills in an XAF detail view before a report runs, and the report data is filtered by a `CriteriaOperator` built from that input.

| | |
|---|---|
| Framework | .NET 8, DevExpress XAF **25.2.3** |
| ORM | EF Core 8 (SQL Server / LocalDB) |
| Platforms | Blazor Server + WinForms |
| Reporting | `DevExpress.ExpressApp.ReportsV2` |

## Why / Where Would You Use This?

**Use it when reports outlive your sprint planning.** In a typical XAF line-of-business app, reports accumulate: finance wants an orders-per-customer report this month, logistics wants shipments-by-region next month. Each one needs a parameters dialog (date range, customer, minimum amount, …), and in stock XAF that means a developer hand-writes a `ReportParametersObjectBase` class per report. This project turns that into: an admin clicks **Generate** on the report, a developer reviews one generated file in the next release branch, done. The criteria logic is plain compiled C# — debuggable, diffable in code review, no reflection or runtime magic.

Concrete scenarios where this pattern pays off:

- **Power users design reports at runtime** (XAF's end-user report designer) and each report needs a clean parameters dialog instead of the report viewer's raw parameter panel — with criteria actually applied at the data-source level (SQL), not after loading everything.
- **Many similar filtered reports** over the same domain — the convention-based criteria mapping (`CustomerName` → `Customer.Name = ?`, `MinAmount` → `Amount >= ?`) eliminates copy-paste classes.
- **Scheduled/background report execution** (e.g. Hangfire) where parameter values must be captured as a typed object and replayed later — a compiled parameters class serializes cleanly; dialog input boxes don't.
- **You inherited reports with `?paramName` FilterStrings that mysteriously don't filter** — that substitution silently doesn't work with `ReportParametersObjectBase` (see [Gotchas](#key-xaf-findings-the-hard-won-stuff)); this repo shows the working `GetCriteria()` route end-to-end.

Even if you never adopt the generator, the repo is useful as a **reference implementation**: a verified, minimal example of `ReportParametersObjectBase` on Blazor + WinForms, plus the four gotchas that cost real debugging hours, plus a Playwright E2E harness for XAF Blazor apps.

**When *not* to use it:** a handful of static reports whose parameters never change — just hand-write the parameter classes (see `Reports/OrdersReportParameters.cs` for the template) and skip the metadata machinery. And it's a spike, not a product: no security trimming, no multi-tenancy, rough edges documented below.

## The Problem

XAF ReportsV2 lets you show a parameters dialog before a report executes, but you must hand-write a `ReportParametersObjectBase` subclass per report: properties, `GetCriteria()`, `CreateObjectSpace()`, and wire it to the report. For apps where power users create reports at runtime, that's a developer round-trip for every report.

## The Approach

This solution generates those classes from metadata:

```
┌─────────────┐   Generate    ┌──────────────┐   rebuild   ┌──────────────────┐
│ XtraReport  │ ───────────►  │ .cs source   │ ──────────► │ compiled class    │
│ parameters  │  (inspector + │ on disk      │  (dotnet    │ auto-linked to    │
│ (metadata)  │   generator)  │              │   build)    │ report at startup │
└─────────────┘               └──────────────┘             └──────────────────┘
```

1. **Inspect** — `ReportParameterInspector` reads the XtraReport's parameters (name, CLR type, default, lookup detection) and computes a SHA-256 signature hash for stale detection.
2. **Generate** — `ReportParameterSourceGenerator` emits a complete `ReportParametersObjectBase` subclass (`[DomainComponent]`, `IObjectSpaceCreator` ctor, properties, `GetCriteria()`, `GetSorting()`) into `Module/GeneratedParameters/`.
3. **Rebuild** — the generated file compiles into the module like any other source file. No runtime compilation, no dynamic assemblies. (An earlier iteration compiled with Roslyn at runtime; it was deleted — compile-time generation is dramatically simpler and debuggable.)
4. **Link** — on the next startup, `Updater.LinkGeneratedParameterObjects()` resolves the generated type by name and sets `ReportDataV2.ParametersObjectType`. The next time the report runs, XAF shows the generated detail view and applies the criteria.

### Criteria path resolution

The generator maps parameter names to criteria properties by convention, all overridable per field in the UI:

| Parameter name | Resolved criteria | Rule |
|---|---|---|
| `CustomerName` (string) | `Customer.Name = ?` | `XxxName` → navigation `Xxx` + its `Name` |
| `MinAmount` (numeric) | `Amount >= ?` | `Min`/`Max` prefix → range on the tail property |
| `MaxAmount` (numeric) | `Amount <= ?` | |
| `OrderDate` (exact match) | `OrderDate >= ?` (DateTime) / `= ?` | case-insensitive exact property match |
| `StartDate` | *(unresolved)* | no Start/End convention (yet) — set `Criteria Property Path` to `OrderDate` in the grid |
| Customer (lookup) | `Customer.ID = ?` | business-object typed parameters |

Each `ReportParameterFieldDefinition` row exposes **Include In Criteria** (exclude a field) and **Criteria Property Path** (override the convention). User edits **survive regeneration** — they are merged back by parameter name when you re-run Generate.

## Solution Structure

```
XafReportParametersObjects/
├── XafReportParametersObjects.Module/          # shared XAF module
│   ├── BusinessObjects/
│   │   ├── Customer.cs / Order.cs              # sample domain (seeded)
│   │   ├── ReportParameterDefinition.cs        # one per report: class name, status, hash
│   │   └── ReportParameterFieldDefinition.cs   # one per parameter: type, criteria path
│   ├── Controllers/
│   │   ├── GenerateParameterObjectController.cs  # the "Generate Parameter Object" action
│   │   └── ReportParameterStaleDetectionController.cs  # warns when report params changed
│   ├── Services/
│   │   ├── ReportParameterInspector.cs         # XtraReport → ParameterInfo list + hash
│   │   └── ReportParameterSourceGenerator.cs   # metadata → C# source
│   ├── GeneratedParameters/                    # generated .cs output lands here
│   ├── Reports/
│   │   ├── OrdersReport.cs                     # predefined sample report (code-built)
│   │   └── OrdersReportParameters.cs           # hand-coded reference implementation
│   └── DatabaseUpdate/Updater.cs               # seed data + startup linking
├── XafReportParametersObjects.Blazor.Server/   # Blazor Server app
└── XafReportParametersObjects.Win/             # WinForms app
```

## Getting Started

Prerequisites: .NET 8 SDK, SQL Server LocalDB (`(localdb)\mssqllocaldb`), a DevExpress license with the 25.2 NuGet feed configured.

```powershell
dotnet build XafReportParametersObjects.slnx
dotnet run --project XafReportParametersObjects\XafReportParametersObjects.Blazor.Server --urls http://localhost:5000
```

The database is created and seeded on first run (2 customers, 3 orders, the predefined Orders Report). No login — security is not configured in this spike.

> DEBUG builds auto-update the database schema on model changes even without an attached debugger (the stock XAF template only updates under the VS debugger — that's been changed in `BlazorApplication.cs` / `WinApplication.cs` so `dotnet run` workflows survive model changes).

## How-To: Generate a Parameter Object for a Report

1. **Get a user-created report.** In *Reports*, select a report. If it's a **predefined** report (registered in code), use **Copy Predefined Report** first and work with the copy — see [Gotchas](#gotcha-2-never-reassign-a-predefined-reports-parametersobjecttype).
2. **Hide the report's own parameters** — handled automatically by Generate, but be aware: any `Visible = true` XtraReport parameter makes the viewer show its *own* parameter panel and bypass XAF's dialog entirely.
3. **Create a `ReportParameterDefinition`** (*ReportParameters → Report Parameter Definition → New*): link the report, choose a `GeneratedClassName` (a valid C# identifier, e.g. `SalesByRegionParameters`), save.
4. **Click "Generate Parameter Object".** The Fields grid fills with one row per report parameter, including the inferred criteria path. The `.cs` file is written to `Module/GeneratedParameters/` (override the location with `ReportParameters:OutputDirectory` in `appsettings.json`).
5. **Review the field grid.** Fix unresolved or wrong criteria paths (e.g. `StartDate` → `OrderDate`), untick *Include In Criteria* for display-only parameters, then Generate again — your edits are preserved.
6. **Rebuild and restart.** On startup the module updater links the compiled class to the report (`Status` must be `Generated`; predefined reports are skipped).
7. **Run the report.** The XAF detail view appears, the user fills it in, `GetCriteria()` filters the `CollectionDataSource`.

If you later edit the report's parameters, the stale-detection controller flags the definition (`IsStale`) when you open it and prompts you to regenerate (signature-hash comparison).

### Implementing this in your own XAF app

Copy these into your module — they have no dependencies on the sample domain:

- `Services/ReportParameterInspector.cs`
- `Services/ReportParameterSourceGenerator.cs` (adjust `GeneratedNamespace`)
- `Controllers/GenerateParameterObjectController.cs` and `Controllers/ReportParameterStaleDetectionController.cs`
- `BusinessObjects/ReportParameterDefinition.cs`, `ReportParameterFieldDefinition.cs`, `ReportParameterStatus.cs` (+ add the two `DbSet`s to your DbContext)
- The `LinkGeneratedParameterObjects()` block from `DatabaseUpdate/Updater.cs`

Then make sure your reports module is registered with `options.ReportDataType = typeof(ReportDataV2)` and the generated output folder is inside your module project so it compiles.

## Key XAF Findings (the hard-won stuff)

These were discovered by reading the ReportsV2 source and verified against a running app — details in `session_handoff.md` and `code-review-2026-06-12.md`.

### Gotcha 1: `Visible = true` parameters bypass XAF's dialog
The report viewer shows its own parameter panel using the XtraReport defaults and ignores `ReportParametersObjectBase` input. **All** XtraReport parameters must be `Visible = false`. The Generate action enforces this.

### Gotcha 2: never reassign a predefined report's `ParametersObjectType`
`PredefinedReportsUpdater` reconciles its registrations on every database update. A predefined report row whose parameters type no longer matches the `AddPredefinedReport<T>()` declaration is treated as orphaned — it disappears from the Reports list and a duplicate canonical row is created. Generated parameter objects are therefore **only linked to user-created reports** (`PredefinedReportTypeName == null`).

### Gotcha 3: `?paramName` FilterString substitution does not work with `ReportParametersObjectBase`
The framework passes the whole object as **one** hidden parameter (`XafReportParametersObject`); individual properties are never copied to individual report parameters. Filter via `GetCriteria()` returning a `CriteriaOperator` — it flows into `CollectionDataSource.Criteria` on both Blazor and WinForms (`ReportServiceController.OnHandleAccepted()` is platform-agnostic).

### Gotcha 4: `[DomainComponent]` module-assembly types need no registration
Non-persistent `[DomainComponent]` classes declared inside an XAF module assembly are auto-collected into the Application Model — generated classes work without touching `AdditionalExportedTypes`.

## Testing

The repo ships a committed end-to-end test: **`XafReportParametersObjects.E2ETests`**, a C# console runner using **Playwright for .NET** (`Microsoft.Playwright`, headless Chromium). One-time setup, then one command:

```powershell
dotnet build XafReportParametersObjects\XafReportParametersObjects.E2ETests
# one-time: install the Playwright browser
powershell -ExecutionPolicy Bypass -File XafReportParametersObjects\XafReportParametersObjects.E2ETests\bin\Debug\net8.0\playwright.ps1 install chromium

dotnet run --project XafReportParametersObjects\XafReportParametersObjects.E2ETests
```

It exercises the **entire** workflow — including the rebuild in the middle, which is why it's a console runner that owns the app process rather than an xUnit suite against a fixed server:

1. Pre-clean previous test artifacts (DB rows + generated file), build, start the Blazor app on port 5100
2. Copy the predefined report (UI), create a `ReportParameterDefinition`, click Generate, assert the emitted `.cs`
3. Edit `Criteria Property Path` → `OrderDate` in the grid, regenerate, assert the user edit was honored **and preserved**
4. Stop the app, `dotnet build` (the generated class compiles in), restart
5. Assert via SQL that the startup updater linked `ParametersObjectType` to the generated class
6. Run the report through the XAF parameter dialog (customer + minimum amount), **export the preview to CSV** and assert it contains exactly the matching seed row (`ORD-001`) and not the excluded ones

Exit code 0 = pass, 1 = fail (CI-friendly). It needs the LocalDB instance and mutates the dev database (test rows are cleaned up at the start of each run, so failures leave evidence).

Two Playwright lessons baked into the script: Blazor's data binding ignores `FillAsync` (no input events), so text entry uses `PressSequentiallyAsync`; and the XAF Blazor report viewer renders pages as **bitmap `<img>` elements** — there is no report text in the DOM, so the filtering assertion exports the document to CSV and checks the file instead.

This catches what unit tests can't: the interplay of the XAF application model, the Blazor report viewer, the parameter dialog, and the actual SQL filtering. If you change the generator or the linking logic, re-run it — *build passing is not the same as the dialog appearing*.

## Known Limitations / Roadmap

- WinForms is verified manually (generated parameter dialog + filtering work); there is no scripted WinForms E2E — the DevExpress WinForms grid exposes nothing via UI Automation by default, so the committed E2E runner covers Blazor only
- Lookup (business-object) parameter generation is implemented but untested end-to-end
- `DefaultValue` metadata is captured but not emitted as property initializers
- No `StartXxx`/`EndXxx` date convention in the path resolver (workaround: edit the path in the grid)
- No security/multi-tenancy considerations — this is a spike

## License / Status

Exploration project. Use the patterns freely; expect rough edges.
