# Phase 3B Validation – Instance GVL and Optional PRG

## Scope

Phase 3B adds two project-level generated artifact types:

- `GVL_<prefix>_Units.TcGVL` for nodes with `generate.instance = true`,
- optional `PRG_<prefix>_Generated.TcPOU` when project option `programCallStructure` is enabled.

The GVL uses `{attribute 'qualified_only'}`. Instance names are derived from stable IEC node names and emitted in deterministic node order. Each node may explicitly bind a project FB through `instanceType`; otherwise the generator selects its generated ApplicationUnit base FB or the corresponding ETAB library FB.

The PRG calls only instances explicitly selected with `callInProgram`. This avoids automatically invoking configuration scaffolds or FBs that require mandatory `VAR_IN_OUT` arguments. The generator does not add the PRG to a TwinCAT task.

## Validation and Safety Rules

- `instanceType` without `instance = true` is rejected.
- `callInProgram` without `instance = true` is rejected.
- Enabling `programCallStructure` without any selected callable instance is rejected.
- Project-level artifacts use UUID-v5 TwinCAT GUIDs derived from the project ID and artifact kind.
- Node input order does not affect GVL or PRG output.
- Every new artifact is valid XML, uses LF line endings, and carries its manifest content hash.
- Rename, update, deletion, conflict, staging, and rollback behavior is inherited from the existing generation transaction.

## Automated Coverage

The Phase 3B tests cover:

1. the complete 15-artifact BrushMachine reference set,
2. explicit project FB types in the generated GVL,
3. generated-base and ETAB-library type fallbacks,
4. project-wide PRG opt-in,
5. deterministic selected instance call order,
6. node-order independence for both project-level artifacts,
7. invalid instance-type and PRG-call combinations,
8. manifest round-trip and project integration for both new artifact kinds.

Current result: 52 core tests and 7 service tests passed. The complete Release build finished with 0 warnings and 0 errors, the TypeScript and format checks passed, and the embedded desktop-service smoke test completed successfully with the 15-artifact preview and lossless save/reopen flow. No Playwright test was run.

## Real Reference-Project Preview

On 2026-08-13, the CLI performed a read-only preview against the real `AutomationBase_Beispiel.plcproj` directory with `--integrate-project`.

The initial artifact/project-entry preview observed:

- 15 proposed artifacts,
- `Generated/GVLs/GVL_BM_Units.TcGVL` included,
- 15 proposed generated `Compile` entries,
- 7 proposed generated `Folder` entries,
- compatible existing ETAB library reference retained,
- no files written to the reference project.

After the Phase 4A compiled-object scan was added, the same read-only preview correctly blocks project integration with seven `PLC_OBJECT_NAME_CONFLICT` issues: three command enums, three request DUTs, and the existing machine-status DUT. This supersedes the earlier conflict-free file-path assessment; no reference-project files were written in either run.

The reference model keeps `programCallStructure = false`, so it does not introduce a second cyclic entry point beside the handwritten application. PRG generation is exercised through automated tests and can be enabled deliberately in the editor or model.

## Acceptance Boundary

This validates deterministic generation and file/project planning only. It does not prove TwinCAT XAE open, task assignment, a complete PLC compile, simulation, or machine behavior. The existing reference project also has handwritten object names that overlap other generated artifacts; Phase 4 golden-sample reconciliation remains necessary before complete-project compile acceptance.
