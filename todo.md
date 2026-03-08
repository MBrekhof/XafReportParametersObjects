# Todo

## Current State

Solution builds cleanly. Orders Report with `OrdersReportParameters` works on Blazor:
- XAF detail view shows before report preview
- User fills in parameters (CustomerName, StartDate, MinAmount)
- `GetCriteria()` builds `CriteriaOperator` from user input
- Report data is filtered correctly

### Architecture
- **Generate action** writes `.cs` source file to disk (no runtime Roslyn compilation)
- Developer rebuilds the app to activate the generated parameter object
- `ReportParametersObjectBase.GetCriteria()` handles filtering (works on both Blazor and WinForms)
- XtraReport parameters must be `Visible = false` (Generate action does this automatically)
- `?paramName` substitution does NOT work with `ReportParametersObjectBase` — use `GetCriteria()` instead

## Remaining Tasks

- [ ] Test the "Generate" action end-to-end (create a new report with parameters, generate, rebuild, verify)
- [ ] Test with WinForms platform
- [ ] Add a Customer lookup parameter to test lookup generation
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
- [x] Delete runtime compilation infrastructure (5 files, 4 NuGet packages)
- [x] Clean up Module.cs and Startup.cs (Blazor + Win)
- [x] Fix report filtering (root cause: Visible=true on XtraReport parameters)
- [x] Add HideReportParameters() to Generate action
- [x] Add smart output directory detection + appsettings.json override
- [x] DevExpress ReportsV2 source code analysis — documented findings in memory
