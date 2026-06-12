# Todo

## Current State

The Generate workflow is **verified end-to-end on Blazor** (2026-06-12):
1. Create a `ReportParameterDefinition`, link a **user-created** report, click Generate
2. `.cs` file written to `GeneratedParameters/`, Status → Generated, criteria paths inferred and editable per field
3. Rebuild → on next startup the Updater links `Report.ParametersObjectType` to the generated class automatically
4. Running the report shows the XAF parameter dialog; `GetCriteria()` filters correctly

### Architecture
- **Generate action** writes a `.cs` source file to disk (no runtime Roslyn compilation)
- Developer rebuilds the app to activate the generated parameter object
- **Updater.LinkGeneratedParameterObjects()** closes the loop on startup (skips predefined reports)
- Field metadata is honored: `IncludeInCriteria` excludes a field from criteria, `CriteriaPropertyPath` overrides the convention-based path; user edits survive regeneration (merged by parameter name)
- XtraReport parameters must be `Visible = false` (Generate action does this automatically)
- `?paramName` substitution does NOT work with `ReportParametersObjectBase` — use `GetCriteria()`
- **Generated parameter objects are for user-created reports only.** Predefined reports declare their parameters type in code; modifying their `ParametersObjectType` makes `PredefinedReportsUpdater` treat the row as orphaned and recreate it (duplicate rows)

## Remaining Tasks

- [ ] Test with WinForms platform (compiles, not yet run)
- [ ] Add a Customer lookup parameter to test lookup generation
- [ ] Generator: emit property defaults from `DefaultValue` metadata (currently ignored; generated DateTime properties start empty)
- [ ] Generator: consider Start/End date convention (`StartDate` → nearest date property) — today the user fixes it via `CriteriaPropertyPath`, which works
- [ ] Consider removing redundant `AdditionalExportedTypes.Add(typeof(OrdersReportParameters))` (confirmed: `[DomainComponent]` types in the module assembly are auto-collected — generated classes work without it)
- [ ] Consider Hangfire integration (serialize parameter values, apply at scheduled execution time)
- [ ] Push to GitHub

## Completed

- [x] Research `ReportParametersObjectBase` API and patterns
- [x] Create sample business objects (Customer, Order) with seed data
- [x] Create metadata entities (ReportParameterDefinition, ReportParameterFieldDefinition)
- [x] Create ReportParameterInspector service
- [x] Create ReportParameterSourceGenerator service
- [x] Create GenerateParameterObjectController with Generate action
- [x] Create sample XtraReport (OrdersReport) with parameters
- [x] Create hand-coded OrdersReportParameters with GetCriteria()
- [x] Simplify from runtime Roslyn compilation to compile-time source generation
- [x] Fix report filtering (root cause: Visible=true on XtraReport parameters)
- [x] DevExpress ReportsV2 source code analysis — documented findings in memory
- [x] **Code review (2026-06-12)** — see `code-review-2026-06-12.md`
- [x] Close the loop: Updater links generated types to reports on startup (skips predefined reports)
- [x] Set Status=Generated in the Generate action
- [x] Honor IncludeInCriteria + CriteriaPropertyPath in generator; preserve user edits across regeneration
- [x] Stop committing user ObjectSpace in stale-detection controller
- [x] Single report load in Generate action; misc cleanup (dead IsRequired, empty Setup, unused packages, junk generated files)
- [x] DEBUG builds auto-update DB schema without attached debugger (Blazor + Win)
- [x] **End-to-end test on Blazor — PASSED** (generate → rebuild → auto-link → dialog → filtered preview)
