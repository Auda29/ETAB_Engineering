# Phase 1 Completion Validation

## Status

- Phase: 1 – Headless Generator Core
- Result: completed
- Validation date: 2026-08-10
- Reference model: `examples/BrushMachine.reference.etab.json`

Phase 1 provides a deterministic, file-based generator core. It validates the project model, plans all changes read-only, and writes only after the explicit CLI command `generate`. Visual modeling begins in Phase 2; integration into a `.plcproj` file and compilation of the actually generated project artifacts follow in Phase 3.

## Implemented Scope

The BrushMachine reference run produces 14 PLC artifacts:

- three command enums,
- three request DUTs,
- four project-specific status DUTs,
- four ApplicationUnit base FBs.

It also produces `Generated/etab-generation-manifest.json`. The manifest contains the project and schema versions, semantic model hash, and for each artifact its model ID, kind, name, stable TwinCAT GUID, relative path, and SHA-256 content hash.

The base FBs extend `ETAB.FB_ETAB_ApplicationUnit`, call `SUPER^()`, and then call the protected `OnExecuteOperation()` hook. The generated hook deliberately remains empty. Safety, motion, and process logic is not generated.

## CLI

The following commands are available:

```text
etab validate <project-file> [--schema <schema-file>]
etab preview  <project-file> [--schema <schema-file>] [--root <directory>] [--content]
etab check    <project-file> [--schema <schema-file>] [--root <directory>]
etab generate <project-file> [--schema <schema-file>] [--root <directory>]
```

`preview` and `check` do not write. `check` returns exit code 0 only for a fully synchronized state, and exit code 1 for either a conflict-free but outdated state or a conflict. `generate` executes only a conflict-free plan.

## Write and Conflict Safety

Before the first write, the target root, relative paths, reparse points, target occupancy, and hashes expected since planning are revalidated. The write process:

1. prepares new content in a UUID-based staging directory under `Generated/`,
2. moves modified or to-be-deleted managed files to a separate backup directory,
3. applies `create`, `update`, `rename`, and `delete`,
4. writes the new manifest last,
5. removes transaction directories only after another path and reparse-point validation.

On failure, already written targets are removed and backups are restored individually. If restoration fails, the affected backup remains available for manual recovery and the run is reported as failed. Conflicts block the entire write process before the transaction starts.

## Automated Evidence

```text
dotnet test ETAB.Engineering.sln --configuration Release --no-restore
Passed: 35, Failed: 0, Skipped: 0
```

Coverage includes:

- schema and semantic validation, including duplicate stable command IDs and `enumValue` values,
- snapshot and determinism validation for every artifact kind,
- stable UUID v5 TwinCAT GUIDs,
- manifest and semantic model hash,
- `create`, `update`, `rename`, `delete`, `unchanged`, and `conflict`,
- detection of files modified manually or occupied after preview,
- complete no-op for unchanged regeneration,
- byte-identical output for identical input,
- transaction rollback after an injected failure midway through an update/rename sequence,
- unchanged user file outside `Generated/`,
- UTF-8 without BOM, LF line endings, and matching manifest content hashes,
- structural parsing of every actually written `.TcDUT` and `.TcPOU` file,
- globally unique TwinCAT XML `Id` attributes in the reference run.

## Isolated CLI End-to-End Run

The Release build was run against a freshly created UUID subdirectory of `%TEMP%\etab-engineering-cli-verification`. Before cleanup, the directory was checked for path boundaries and reparse points.

```text
CLI_VERIFY check-before=1 generate-first=0 check-after=0 generate-second=0 files=15 byte-identical=true outside-generated-files=0
```

This demonstrates the expected out-of-date status before generation, the first write, the subsequent synchronized state, no-op regeneration, and the write boundary.

## Phase 1 Acceptance Criteria

- [x] Identical input produces byte-identical output.
- [x] Duplicate stable command IDs and duplicate `enumValue` values within a node are rejected.
- [x] No file outside the generator-owned area is modified.
- [x] Written TwinCAT XML artifacts can be parsed structurally.

## Deliberate Boundary

Phase 1 modifies neither `ET_AutomationBase` nor a `.plcproj` file. The Phase 0 spike already compiled the inheritance and hook pattern used here successfully with TwinCAT XAE. The 14 files produced by this generator are structurally validated, but have not yet been included in a project and therefore have not themselves been compiled by XAE. This stronger evidence, together with `<Compile Include="…">` entries, GVL/PRG structures, and the project copy, explicitly belongs to Phase 3.

Simulation, online testing, and machine behavior have not been demonstrated either.
