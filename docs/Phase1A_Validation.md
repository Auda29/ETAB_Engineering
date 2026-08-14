# Phase 1A Validation Record

> This document records the historical 1A interim state. The current, fully completed Phase 1 state is documented in the [Phase 1 completion validation](Phase1_Validation.md).

## Status

- Phase: 1A – Model and Validation Core
- Result: completed
- Validation date: 2026-08-10
- Target runtime: .NET 10
- Pinned SDK: 10.0.302 through `global.json`

Phase 1A is a read-only development slice. It does not yet produce TwinCAT objects, write a manifest, or modify either a TwinCAT project or `ET_AutomationBase`.

## Implemented Structure

```text
ETAB.Engineering.sln
├─ src/ETAB.Engineering.Core
│  ├─ Model
│  └─ Validation
├─ src/ETAB.Engineering.Cli
└─ tests/ETAB.Engineering.Core.Tests
```

- `ETAB.Engineering.Core`: typed project model and schema/semantic validation
- `ETAB.Engineering.Cli`: headless entry point with the `validate` command
- `ETAB.Engineering.Core.Tests`: positive and negative tests against the BrushMachine reference model
- `JsonSchema.Net` 9.4.0: evaluates the existing schema as JSON Schema Draft 2020-12

## Validation Chain

A project passes through four stages:

1. parse JSON syntax
2. validate the document against `schemas/etab-project.schema.json`
3. deserialize into the typed C# project model
4. validate cross-project semantic rules

Semantic validation covers:

- global uniqueness of all stable `id` values,
- node, command, and payload names without IEC case-insensitive duplicates,
- unique `enumValue` values within each node,
- exactly one `NoAction` with `enumValue = 0` for generated command enums,
- valid array bounds,
- coupling between request DUT and command enum,
- protection of implicit request fields and embedded library-status fields,
- MTP procedure IDs and local command references,
- existing relationship endpoints, no self-relations or duplicates, and appropriate source/target kinds,
- at most one `contains` parent and no hierarchy cycles,
- valid and unique layout references,
- collision-free generated TwinCAT artifact names.

## CLI

Run from this directory:

```powershell
dotnet run --project .\src\ETAB.Engineering.Cli\ETAB.Engineering.Cli.csproj -- validate .\examples\BrushMachine.reference.etab.json
```

Alternatively, `validate` accepts an explicit schema path through `--schema <file>`. Without this option, the CLI uses the copied schema.

Exit codes:

| Code | Meaning |
|---:|---|
| 0 | Project valid |
| 1 | Validation failed |
| 2 | Invalid CLI arguments |
| 3 | Unexpected execution error |

Each validation error includes a stable error code, a JSON path, and a description, for example:

```text
[JSON_PARSE] line 1, byte 1: '#' is an invalid start of a value.
```

## Evidence Collected

### Restore and Build

```text
dotnet restore ETAB.Engineering.sln
Restore succeeded for 3 projects.

dotnet build ETAB.Engineering.sln --no-restore
0 warnings, 0 errors
```

### Automated Tests

```text
dotnet test ETAB.Engineering.sln --no-build --no-restore
Passed: 7, Failed: 0, Skipped: 0
```

Covered cases:

- valid BrushMachine reference model,
- obsolete command field `value` instead of `enumValue`,
- duplicate stable ID,
- duplicate `enumValue`,
- collision with a reserved library-status field,
- unknown relationship endpoint,
- inverted array bound.

### Positive CLI Case

```text
VALID ...\examples\BrushMachine.reference.etab.json
Project: BrushMachine
Nodes: 7
Relations: 12
```

### CLI Error Path

A non-JSON file was deliberately supplied as the project:

```text
INVALID ...\README.md
[JSON_PARSE] line 1, byte 1: '#' is an invalid start of a value.
CLI_EXIT_CODE=1
```

## Not Yet Demonstrated

- no generation of `.TcDUT`, `.TcPOU`, `.TcGVL`, or `.plcproj` entries,
- no GUID or manifest management,
- no `preview`, `generate`, or `check` commands,
- no snapshot or determinism validation of generated files,
- no new TwinCAT compile in Phase 1A,
- no simulation, online test, or machine validation.

These items belong to the subsequent Phase 1 slices or to Phase 3.
