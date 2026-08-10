# Phase 1B Validation Record

> This document records the historical read-only 1B interim state. The current, fully completed Phase 1 state, including base FBs and writing generation, is documented in the [Phase 1 completion validation](Phase1_Validation.md).

## Status

- Phase: 1B – Deterministic DUT Preview
- Result: completed
- Validation date: 2026-08-10
- Execution mode: entirely in memory, without file writes

Phase 1B extends the validated project core with the first actual generation stage. It renders the planned command enums, request DUTs, and status DUTs as complete TwinCAT XML content, but deliberately does not yet write them to `Generated/` or modify a `.plcproj` file.

## Implemented Artifacts

Depending on `node.generate`, the following are produced:

| Artifact Kind | Example Name | Target Path for Future Generation |
|---|---|---|
| `command-enum` | `E_BM_ProcessCommand` | `Generated/DUTs/Commands/*.TcDUT` |
| `request-dut` | `ST_BM_ProcessRequest` | `Generated/DUTs/Requests/*.TcDUT` |
| `status-dut` | `ST_BM_ProcessStatus` | `Generated/DUTs/Status/*.TcDUT` |

The BrushMachine preview contains ten artifacts:

- three command enums,
- three request DUTs,
- four status DUTs.

RecipeManager, MachineLink, and ProcessCycle do not yet produce DUTs in the reference model because their respective generation flags are disabled.

## Deterministic Rules

- Nodes: `name`, then stable `id`.
- Commands: `enumValue`, then `name`, then stable `id`.
- Payload fields: unchanged model order.
- Layout: completely excluded from artifacts, GUIDs, and hashes.
- Line endings: always LF, regardless of operating system.
- Content hash: SHA-256 over UTF-8 content without a BOM.
- TwinCAT GUID: UUID v5 using the defined generator namespace.

The UUID v5 name follows the Phase 0 contract:

```text
<project-id>/<model-id>/<artifact-kind>
```

This keeps the TwinCAT GUID stable when a node is renamed. A change to `symbolStem` may change the file name and content, but not the object GUID derived from the node ID.

## Generated Contracts

### Command Enum

- attributes `qualified_only`, `strict`, and `to_string`,
- fixed numeric `enumValue` values,
- deterministic numeric ordering,
- auto-generated marker containing the node ID and artifact kind.

### Request DUT

Fixed header:

```iecst
bExecute   : BOOL;
eCommand   : <generated command enum>;
nCommandID : UDINT;
```

Payload fields follow in model order. Array dimensions are rendered, for example, as `ARRAY[1..3] OF LREAL`.

### Status DUT

The fixed header follows the node kind:

| Node Kind | Embedded Status |
|---|---|
| `applicationUnit` | `stUnit : ETAB.ST_ETAB_ApplicationUnitStatus` |
| Application Unit with Typed Request | additionally `stOperation : ETAB.ST_ETAB_CommandStatus` |
| `commandUnit` | `stCommand : ETAB.ST_ETAB_CommandStatus` |
| `recipeManager` | `stRecipe : ETAB.ST_ETAB_RecipeStatus` |
| `machineLink` | `stLink : ETAB.ST_ETAB_MachineLinkStatus` |

The library DUTs are referenced only. `ET_AutomationBase` was not modified.

## CLI

Compact preview:

```powershell
dotnet run --project .\src\ETAB.Engineering.Cli\ETAB.Engineering.Cli.csproj -- preview .\examples\BrushMachine.reference.etab.json
```

Preview including complete XML content:

```powershell
dotnet run --project .\src\ETAB.Engineering.Cli\ETAB.Engineering.Cli.csproj -- preview .\examples\BrushMachine.reference.etab.json --content
```

For each artifact, the compact output includes:

- artifact kind,
- relative target path,
- deterministic TwinCAT GUID,
- SHA-256 content hash.

`preview` validates the project first. An invalid model continues to use exit code 1. As with `validate`, `--schema <file>` can select an explicit schema.

## Evidence Collected

### Automated Tests

```text
dotnet test ETAB.Engineering.sln --no-restore
Passed: 17, Failed: 0, Skipped: 0
```

In addition to the seven Phase 1A tests, the new tests verify:

- exact artifact list for BrushMachine,
- golden snapshots for the Process command, request, and status,
- well-formed XML for every artifact,
- SHA-256 against the actual UTF-8 content,
- LF line endings exclusively,
- no output change caused by layout changes,
- no output change caused by a different node or command input order,
- stable TwinCAT GUIDs when a node is renamed,
- correct embedded library-status fields for all four node kinds,
- UUID v5 implementation against a known RFC test vector.

### Positive CLI Case

```text
PREVIEW ...\examples\BrushMachine.reference.etab.json
Project: BrushMachine
Artifacts: 10
PREVIEW_EXIT_CODE=0
```

### CLI Error Path

```text
INVALID ...\README.md
[JSON_PARSE] line 1, byte 1: '#' is an invalid start of a value.
INVALID_PREVIEW_EXIT_CODE=1
```

### Write Protection

The expected output directory was checked before and after `preview --content`:

```text
GENERATED_EXISTS_BEFORE=False
GENERATED_EXISTS_AFTER=False
```

This demonstrates that Phase 1B did not create a `Generated/` directory during the reference run.

## Not Yet Demonstrated

- no writing of rendered `.TcDUT` files,
- no manifest and no comparison with existing files,
- no classification as `create`, `update`, `rename`, `delete`, `unchanged`, or `conflict`,
- no base-FB, GVL, or PRG generation,
- no `.plcproj` integration,
- no TwinCAT compile of the preview artifacts,
- no simulation, online test, or machine validation.

These boundaries are deliberate: the next phase first adds the manifest and safe filesystem comparison. A writing `generate` command follows only after that.
