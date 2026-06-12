# Code Review — 2026-06-12

Scope: Module project after the "compile-time source generation" refactor (commit 16f8fa8).
Build verified: `dotnet build XafReportParametersObjects.slnx` → 0 errors.
DevExpress claims verified against dxdocs (25.2).

> **Status (same day):** #1–#7, #9, #10 fixed and verified end-to-end on Blazor
> (see session_handoff.md). #8 resolved: the explicit export is redundant —
> `[DomainComponent]` module-assembly types are auto-collected (removal left as todo).
> #11 deliberately not fixed. Extra finding during testing: never reassign
> `ParametersObjectType` on predefined reports (PredefinedReportsUpdater recreates them);
> the Updater linking now skips them.

## Critical — the Generate workflow doesn't close the loop

### 1. Generated parameter objects are never linked to their report
The Generate action writes the `.cs` file, but after rebuild **nothing sets
`ReportDataV2.ParametersObjectType` to the generated type**. The only place it's
set is `Updater.cs:63`, hardcoded for `OrdersReport`/`OrdersReportParameters`.

Evidence: three generated classes exist (`Fresh`, `LastTry`, `Complicado`) and
none can ever be invoked — their reports still have no parameters object.
This is why "Test the Generate action end-to-end" is still open; as written, the
last step fails silently (report just runs without a parameter dialog).

**Fix:** in `Updater.UpdateDatabaseAfterUpdateSchema()`, loop over
`ReportParameterDefinition`s, resolve
`Type.GetType(ReportParameterSourceGenerator.GetFullTypeName(def.GeneratedClassName))`,
and set `def.Report.ParametersObjectType` when the type exists. ~10 lines, runs
on every startup, self-heals after each rebuild.

### 2. `Status` is never set to `Generated`
The Generate action updates `IsStale` and `ParameterSignatureHash` but not
`Status` (`GenerateParameterObjectController.cs:64-66`). The
`ReportParameterStatus` enum is currently dead weight. Set
`definition.Status = ReportParameterStatus.Generated` in the action — it also
gives the Updater fix above a cheap filter.

### 3. Generator ignores `IncludeInCriteria` and `CriteriaPropertyPath`
`ReportParameterFieldDefinition` exposes both fields to the user, but
`ReportParameterSourceGenerator.Generate(…, fieldDefinitions, …)` recomputes the
path by naming convention (`ResolveCriteriaPath`) and includes every field.
The controller also never populates `CriteriaPropertyPath`. The UI promises
control the generator doesn't honor. Either:
- honor them: `field.CriteriaPropertyPath ?? ResolveCriteriaPath(...)`, skip when `!IncludeInCriteria`, and write the resolved path back to the field so the user sees/edits what was inferred; or
- delete both columns (and `IsRequired`, also dead — never set, never emitted).

Honoring them is ~5 lines and makes the metadata grid actually useful.

## Should fix

### 4. `ReportParameterStaleDetectionController` commits the user's ObjectSpace on view open
`View.ObjectSpace.CommitChanges()` (line 42) inside `OnActivated` commits **any**
pending edits in the detail view as a side effect, every time the view activates.
Lazy fix: just set `IsStale` and show the message without committing — the flag
gets persisted with the user's next save, and the warning is the real payload.

### 5. Report is loaded twice in the Generate action
`GenerateAction_Execute` loads the report for inspection, then
`HideReportParameters` loads it again from storage and saves. Pass the
already-loaded `report` in and save once — removes a load/save round-trip and a
second deserialization.

### 6. Orphaned doc comment
`GenerateParameterObjectController.cs:160-163` — the "Walks up from the build
output directory…" `<summary>` sits on `HideReportParameters` instead of
`FindModuleProjectDirectory`. Move or delete.

## Minor / housekeeping

7. **Junk generated files**: `GeneratedParameters/Fresh.cs`, `LastTry.cs`,
   `Complicado.cs` are test leftovers (all `GetCriteria() => null`), untracked,
   but compiled into the module as `[DomainComponent]` types. Delete them;
   decide whether `GeneratedParameters/` should be committed or gitignored
   (recommend: committed — generated code is source in this design).
8. **Redundant export**: `Module.cs:22` adds `OrdersReportParameters` to
   `AdditionalExportedTypes`. Per dxdocs, `[DomainComponent]` types declared in
   a module assembly are collected automatically (`GetDeclaredExportedTypes`).
   Likely removable — and importantly, the generated classes do NOT need to be
   added there either. Confirm during the end-to-end test before deleting.
9. **Empty `Setup` override** in `Module.cs:36-39` — delete.
10. **Unused packages** in Module.csproj: `Microsoft.EntityFrameworkCore.InMemory`
    (only commented-out usage) and `System.IdentityModel.Tokens.Jwt` (no usage
    found). Template leftovers — remove.
11. **`ResolveClrType` fallback** (`ReportParameterSourceGenerator.cs:226`):
    unknown type names silently become `string`. Only affects exotic non-lookup
    parameter types, but a silently wrong criteria branch is worth at least a
    generated `// TODO` comment in the output. Low priority.

## What's solid

- The core architecture decision (generate source, rebuild, no runtime Roslyn) is sound and much simpler than what it replaced.
- `OrdersReportParameters` matches the documented `ReportParametersObjectBase` pattern exactly ([DomainComponent], IObjectSpaceCreator ctor, GetCriteria, Visible=false params).
- The field-clearing loop in the controller correctly avoids the `ObservableCollection.Clear()` issue fixed in 3cf61eb.
- Inspector/generator separation is clean; SanitizePropertyName handles edge cases properly.
- DbContext, entities, and seed updater follow XAF EF Core conventions (virtual props, ObservableCollection init, Aggregated).

## Suggested order of work
1. Fix #1 + #2 together (Updater linking, ~15 lines) — this unblocks the open "end-to-end test" todo.
2. Fix #3 (honor metadata) before testing, since the test exercises the grid.
3. Run the end-to-end test; use it to confirm #8.
4. Sweep #4–#11 afterward.
