# XafReportParametersObjects

## Project Overview

XAF application exploring `ReportParametersObjectBase` functionality for DevExpress XAF Reports (ReportsV2). The goal is to build custom report parameter objects that allow users to configure report parameters through XAF's UI before report generation.

## Tech Stack

- .NET 8, C#, DevExpress XAF 25.2.3
- EF Core (SQL Server)
- ReportsV2 module (`DevExpress.ExpressApp.ReportsV2`)
- Platforms: Blazor Server + WinForms

## Solution Structure

```
XafReportParametersObjects/
  XafReportParametersObjects.Module/       # Shared module (business objects, controllers)
  XafReportParametersObjects.Blazor.Server/ # Blazor Server app
  XafReportParametersObjects.Win/          # WinForms app
```

## Key References

- `ReportParametersObjectBase`: [DevExpress Docs](https://docs.devexpress.com/eXpressAppFramework/DevExpress.ExpressApp.ReportsV2.ReportParametersObjectBase)
- Base class for report parameter objects shown to users before report execution
- Parameters appear as a Detail View that users fill in before the report runs

## Workflow Rules

### Always Plan First

Before implementing any feature or change:
1. Read `session_handoff.md` and `todo.md` to understand current state
2. Create or update a plan in `todo.md` before writing code
3. Break work into small, verifiable steps

### Session Management

**At session start:**
- Read `session_handoff.md` for context from previous sessions
- Read `todo.md` for current task status

**At session end:**
- Update `todo.md` with completed/remaining items
- Update `session_handoff.md` with:
  - What was accomplished
  - Current state of the code
  - What to do next
  - Any blockers or open questions

### Noteworthy Findings

When discovering XAF patterns, gotchas, or useful techniques during this project, add them to the global XAF skill so they benefit all projects. Look for the `devexpress-xaf` skill or create entries in memory.

## DevExpress Docs

Use the `mcp__dxdocs` tools to search and read DevExpress documentation when needed. Always verify API usage against docs rather than guessing.
