# Phase 3A Validation – TwinCAT Project Integration

## Scope

Phase 3A adds safe file-based TwinCAT project integration to the CLI. It is deliberately opt-in through `--integrate-project` and requires an explicitly selected `--root`.

The integration manages:

- `Compile` entries for generated PLC artifacts,
- `Folder` entries required by the generated hierarchy,
- the configured ETAB `PlaceholderReference` and `PlaceholderResolution`, plus the direct EngineeringToolbox (`ET`) `PlaceholderReference` required by generated typed mode assignments,
- ownership metadata in `Generated/etab-project-integration-manifest.json`.

GVL instance generation, the optional PRG call structure, an editor-side confirmed write action, and TwinCAT XAE compile validation remain outside this slice.

## Ownership and Safety Boundary

- Only project elements newly added by ETAB Engineering are recorded as managed state.
- Compatible pre-existing project and library entries remain unmanaged and untouched.
- A missing, duplicated, incompatible, or externally modified managed element is a conflict and blocks every write.
- The configured `.plcproj` must currently be a direct child of the selected root.
- Project roots and project files reached through symbolic links, junctions, or other reparse points are rejected.
- Expected paths and hashes are checked again immediately before writing.
- Generated artifacts, the artifact manifest, the project integration manifest, and the `.plcproj` update are staged, backed up, applied, and rolled back as one transaction.
- Project XML is parsed both before planning and after the targeted textual changes. Unrelated project-file lines and their existing line endings are preserved.

## Automated Coverage

The project integration test suite covers:

1. initial integration and an unchanged repeated plan,
2. preservation of a compatible pre-existing library reference,
3. preservation of an unmanaged pre-existing `Compile` entry,
4. managed artifact rename and corresponding project update,
5. conflict detection after manual modification of a managed `Compile` entry,
6. preflight rejection when the project changes after preview,
7. rollback of project and artifact changes after an injected write failure,
8. conflict detection for an incompatible pre-existing ETAB library reference.

The complete automated validation commands are:

```powershell
dotnet build .\ETAB.Engineering.sln --configuration Release --no-restore
dotnet test .\ETAB.Engineering.sln --configuration Release --no-build --no-restore
npm.cmd --prefix .\src\ETAB.Engineering.Editor run check
```

Current result: 43 core tests and 4 service tests passed, the Release build completed with 0 warnings and 0 errors, and the TypeScript check passed.

## Real Project-Copy Smoke Test

On 2026-08-13, the CLI was exercised against a temporary copy of the real reference project:

```text
AutomationBase Beispiel/AutomationBase_Beispiel/AutomationBase_Beispiel/
AutomationBase_Beispiel/AutomationBase_Beispiel.plcproj
```

The original reference project remained unchanged. The temporary copy was generated and then checked with the same model, root, and `--integrate-project` option.

Observed result:

- 14 generated `Compile` entries,
- 6 generated `Folder` entries,
- 16 files below `Generated/` including both manifests,
- project diff of exactly 48 added and 0 removed lines,
- repeated `check` result: `SYNCHRONIZED`,
- unchanged project file and both unchanged manifests on the repeated check.

The exact project diff confirms that the integration added only the expected elements and did not normalize or rewrite unrelated project-file lines.

## Acceptance Boundary

This evidence proves structural validity, ownership behavior, transaction safety, and idempotence at the file level. It is not evidence that the complete project opens or compiles in TwinCAT XAE, runs in simulation, or behaves correctly on a machine.

The reference project currently also contains handwritten types whose names overlap generated output. Those collisions must be reconciled as part of the Phase 4 golden-sample work before a meaningful complete-project compile can be accepted.
