# Todo

## Current Sprint

- [x] Create a sample XtraReport with parameters (string, DateTime, decimal) registered via PredefinedReportsUpdater
- [ ] Run the app and test the full Generate -> Associate -> Restart -> Use flow
- [ ] Test graduation flow (Generate -> Graduate -> copy source -> compile)
- [ ] Test stale detection (modify report params, verify IsStale flag)

## Completed

- [x] Research `ReportParametersObjectBase` API and patterns
- [x] Create sample business objects (Customer, Order) with seed data
- [x] Create metadata entities (ReportParameterDefinition, ReportParameterFieldDefinition)
- [x] Create ReportParameterInspector service
- [x] Create ReportParameterSourceGenerator service
- [x] Create ReportParameterCompiler (Roslyn) service
- [x] Create ReportParameterGraduationService
- [x] Create GenerateParameterObjectController with Generate and Graduate actions
- [x] Create ReportParameterStaleDetectionController

## Notes

- `FieldSize` attribute is XPO-only; use `StringLength` for EF Core
- `CollectionDataSource.ObjectTypeName` is a string, not a Type - use `Type.GetType()` to resolve
- `IReportStorage` is the DI service for loading reports: `serviceProvider.GetRequiredService<IReportStorage>()`
- Process restart required after generating parameter objects (XAF TypesInfo is process-static)
