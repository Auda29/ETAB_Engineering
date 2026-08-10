# Phase 1C Validation Record

> This document records the historical read-only 1C interim state. The now-completed writing Phase 1, including base FBs, `check`, `generate`, and rollback, is documented in the [Phase 1 completion validation](Phase1_Validation.md).

## Status

- Phase: 1C – Manifest and Filesystem Planning
- Result: completed
- Validation date: 2026-08-10
- Execution mode: read-only outside temporary automated tests

Phase 1C extends the artifact preview with a safe comparison against an existing generated state. The core creates a deterministic manifest in memory, reads an existing manifest if present, and checks exactly the files managed by it. Project or generator outputs are still not written, renamed, or deleted.

## Manifest v0.1

The proposed content of `Generated/etab-generation-manifest.json` contains:

- `manifestVersion`,
- `generatorVersion`,
- `schemaVersion`,
- `projectId`,
- `semanticModelHash`,
- for each artifact: `sourceModelId`, `kind`, `name`, `twinCatGuid`, `relativePath`, and `contentHash`.

The manifest is sorted deterministically, uses two-space indentation and LF line endings exclusively, and contains no timestamps, user names, machine names, or absolute paths.

## Semantic Model Hash

The model hash is SHA-256 over a canonical JSON representation of the typed model.

Excluded:

- the complete `layout` block.

Canonically sorted:

- nodes by `name` and `id`,
- commands by `enumValue`, `name`, and `id`,
- relationships by `kind`, `sourceNodeId`, `targetNodeId`, and `id`,
- MTP procedures by `procedureId`, `name`, and `id`,
- JSON objects by property name.

Payload fields retain their model order because the model specification defines that order as PLC-semantic.

## Target Root and Path Safety

Without an additional option, the CLI uses the directory containing the `.etab.json` project file as the project root. For tests against a different target state, it can be set explicitly:

```powershell
etab preview BrushMachine.etab.json --root C:\TwinCAT\BrushMachine
```

Safety rules:

- the project root must exist as a directory,
- `project.generation.generatedRoot` must be relative,
- the resolved generator root must be a true subdirectory of the project root,
- `.` and `..` segments are not permitted,
- every current and previous manifest path is resolved to an absolute path and revalidated against the generator root,
- no recursive filesystem scans or globs are used.

## Change Kinds

| Status | Condition |
|---|---|
| `create` | no previous manifest entry and target path is free |
| `unchanged` | managed file matches both the previous and new content hashes |
| `update` | managed file matches the previous hash and the new content differs |
| `rename` | same model ID and artifact kind, stable GUID, unchanged old path, free new path |
| `delete` | previous manifest entry has been removed and the specific old file is unchanged |
| `conflict` | a safe automatic next step is not possible |

At least the following conditions are treated as conflicts:

- manifested file is missing,
- manifested file was modified outside ETAB Engineering,
- an unmanifested file occupies a target path,
- rename target is occupied,
- manifest is syntactically or semantically invalid,
- manifest belongs to a different project or schema ID,
- previous or new paths leave the generator-owned area,
- stored TwinCAT GUID does not match the deterministic UUID v5.

When an artifact conflict exists, the manifest status is also reported as `conflict`. A future `generate` command must not write in that case.

## CLI

Read-only comparison against the default root beside the project file:

```powershell
dotnet run --project .\src\ETAB.Engineering.Cli\ETAB.Engineering.Cli.csproj -- preview .\examples\BrushMachine.reference.etab.json
```

Comparison against an explicit project root with all planned content displayed:

```powershell
dotnet run --project .\src\ETAB.Engineering.Cli\ETAB.Engineering.Cli.csproj -- preview .\examples\BrushMachine.reference.etab.json --root . --content
```

`--content` additionally displays all planned `.TcDUT` content and the complete proposed manifest content. It does not write this content.

Exit codes:

- 0: preview successful and conflict-free,
- 1: project validation failed or preview contains conflicts,
- 2: invalid CLI arguments,
- 3: unexpected execution error.

## Automated Evidence

```text
dotnet test ETAB.Engineering.sln --no-restore
Passed: 27, Failed: 0, Skipped: 0
```

The ten new Phase 1C tests verify:

- an empty root produces ten `create` artifacts and a `create` manifest,
- a fully materialized, unchanged state produces only `unchanged`,
- a domain payload change updates only the affected DUT,
- a `symbolStem` change produces three `rename` operations with stable GUIDs,
- a disabled artifact produces a safe `delete`,
- a manually modified managed file produces `conflict`,
- an occupied unmanifested target path produces `conflict`,
- an invalid manifest blocks comparison,
- a generator root that escapes the project root is rejected before comparison,
- the model hash remains identical across layout and non-semantic input-order changes.

The tests materialize comparison states only in UUID-based subdirectories of `%TEMP%\etab-engineering-tests`. Before recursive removal, the resolved path is revalidated against this exact test root.

## BrushMachine Reference Run

Without an existing generated state:

```text
Project: BrushMachine
Artifacts: 10
Manifest: [create] Generated/etab-generation-manifest.json
10 x [create]
PREVIEW_EXIT_CODE=0
```

Before and after the CLI preview, both the default root and explicit root were checked for a newly created `Generated/` directory. The preview does not create such a directory.

## Not Yet Demonstrated

- no production writing of the manifest or DUT files,
- no production rename or delete operations,
- no transactional write flow or rollback,
- no CLI `generate` or CLI `check`,
- no base-FB, GVL, PRG, or `.plcproj` generation,
- no TwinCAT compile of the preview artifacts,
- no simulation, online test, or machine validation.

The next safe slice is a `check` command that evaluates the same plan as a CI check. Only after that should a writing, conflict-protected `generate` command be implemented.
