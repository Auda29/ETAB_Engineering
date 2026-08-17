# Generation Contract v0.1

## 1. Purpose

This contract defines the write and ownership boundaries of the generator. It has been binding since Phase 0 so that the editor and generator do not develop conflicting assumptions.

## 2. Ownership Areas

### Generator-Owned Area

Default CLI path:

```text
Generated/
```

The TwinCAT-first desktop workflow uses the selected PLC project directory directly so generated objects land in its existing `DUTs`, `POUs`, and `GVLs` hierarchy. This mode is represented by `project.generation.generatedRoot = "."`.

In either layout, only files assigned to the current project and a model ID in the last valid manifest are considered generator-managed. Selecting the PLC root does not transfer ownership of handwritten or otherwise unmanaged files in that directory to ETAB Engineering.

### User-Owned Area

Default path:

```text
Application/
```

The generator does not modify or delete any existing file there. In direct TwinCAT output mode (`generatedRoot = "."`) user stubs are created below this application root. In isolated CLI output mode they remain inside the transaction boundary as `Generated/Application/...`.

An initial user scaffold may optionally be generated only when:

- the user explicitly requests it,
- the specific target file does not yet exist,
- no naming collision exists.

Generation of initial user scaffolds is disabled by default.

User scaffolds carry `preserveUserEdits = true` in the generation manifest. Once the file exists, its actual hash is used only for preflight race detection; content differences never schedule an update. Removing the node or disabling scaffolding removes ETAB's compile ownership but leaves the user file on disk. A path-changing symbol rename is blocked until the user-owned file is renamed explicitly.

## 3. Manifest

`etab-generation-manifest.json` contains at least:

- manifest version,
- generator version,
- schema version,
- project ID,
- semantic model hash excluding layout,
- model ID, artifact kind, TwinCAT GUID, relative path, and content hash for each artifact.

The manifest is written only after all intended output files have been generated successfully.

## 4. Generation Process

1. Validate the project file structurally.
2. Validate semantic rules.
3. Resolve target paths to absolute paths and verify them against the permitted roots.
4. Read the existing manifest.
5. Verify current generator-managed files against their previous hashes.
6. Generate all new content in memory.
7. Build a plan containing `create`, `update`, `rename`, `delete`, `unchanged`, and `conflict` operations.
8. Output the change preview.
9. Abort without writing if conflicts exist.
10. Write only after an explicit generate command.
11. Immediately before writing, revalidate target paths, reparse points, target occupancy, and expected previous hashes.
12. Prepare new content in a UUID-based staging directory within the configured generator root.
13. Back up managed files to be modified or deleted in a separate transaction directory.
14. Execute only the specifically planned `create`, `update`, `rename`, and `delete` operations.
15. Write the new manifest last.
16. Remove transaction directories only after another path and reparse-point validation.
17. If a write fails, remove targets already written and restore backed-up files individually.

## 5. Conflict Rules

A conflict exists at least when:

- a generated file has been modified manually since the last manifest,
- a target path is occupied by a file not recorded in the manifest,
- two model objects produce the same target path,
- a user file would have to be renamed, overwritten, or deleted,
- the project or output root cannot be resolved unambiguously,
- a target path traverses a symbolic link, junction, or other reparse point,
- a generated IEC object name is already compiled from another `.TcDUT`, `.TcPOU`, or `.TcGVL` path in the selected project,
- a managed file changed after planning or a previously free target is now occupied.

Conflicts are never overwritten, renamed, or deleted automatically.

## 6. Renaming

- The model ID is retained.
- The TwinCAT GUID is retained.
- The new target name is calculated from the current naming rules.
- The old path is removed only if it is present in the manifest, unchanged, and within the configured generator root.
- The preview reports the operation as `rename`.

## 7. Deletion

- Only specific, manifested files are deleted.
- The generator does not use recursive globs to determine targets.
- The resolved path must be within the configured generator root.
- Modified files are retained and reported as conflicts.
- User files are never deleted automatically.

## 8. Transaction Safety

Staging and backup data reside in uniquely named UUID subdirectories of the configured generator root. The generator resolves every source, target, and transaction path to an absolute path and revalidates it against this root. Recursive cleanup is aborted as soon as a reparse point is detected.

If a file operation fails, the previous state is restored. If an individual backup cannot be restored to its original path, that backup is not removed. The run reports the incomplete restoration and remaining recovery path as an error.

## 9. TwinCAT Project File

Modification of a `.plcproj` file begins only in Phase 3.

The following additional rules then apply:

- existing unmanaged compile entries remain unchanged,
- the generator manages only entries recorded in its manifest,
- library references are not globally reordered or replaced,
- an XML structural validation is performed before writing,
- a real XAE compile is a separate acceptance step after writing.

Phase 3A implements this boundary as an explicit CLI opt-in using `--integrate-project` together with `--root`. The project file and generated artifacts participate in one preflight and rollback transaction. ETAB Engineering records only the `Compile`, `Folder`, `PlaceholderReference`, and `PlaceholderResolution` elements that it adds in the project-integration manifest below the configured generator root. Project integration adds both the configured ETAB reference and a direct `ET` reference to `EngineeringToolbox`; the latter is required because generated runtime settings use the library's qualified-only `ET.eMODE` values.

Compatible entries that already exist are retained but not claimed as managed state. Missing, duplicated, incompatible, or externally changed managed entries block the whole write. Project XML is parsed before planning and again after applying the targeted textual changes; unrelated project-file lines and their existing line endings are preserved.

Before adding compile entries, project integration resolves and parses existing `.TcDUT`, `.TcPOU`, and `.TcGVL` compile items inside the selected root. IEC object names are compared case-insensitively with the proposed generated artifacts. A same-name object at another path is a hard `PLC_OBJECT_NAME_CONFLICT`; unsafe, missing, reparse-point, or unreadable compiled-object paths also block integration because a collision-free project cannot be proven.

Phase 3B adds the project-level artifact kinds `instance-gvl` and `program-call-structure`. They follow the same deterministic GUID, manifest, hash, conflict, staging, and rollback rules as node-level artifacts. The PRG remains optional. The legacy `programCallStructure` switch only emits it; `runtimeExecution` additionally integrates one call into a detected TwinCAT task.

Runtime task integration is part of the same preflight, staging, write, and rollback transaction as generated artifacts and the `.plcproj`. The complete proposed `.TcTTO` content and its hash are included in preview confirmation. A changed task invalidates the preview. Existing calls remain unmanaged, multiple or modified managed calls are conflicts, and the project-integration manifest records only the task path and generated program name owned by ETAB.

Phase 3D adds the opt-in project-level artifact kind `relation-wiring`. `FB_<prefix>_Relations` is emitted only when `relationWiring = true` and the model contains relations. Its methods are deterministic in `kind`, `sourceNodeId`, `targetNodeId`, and relation-ID order. Generated names are bounded to 80 IEC characters with a stable relation-ID suffix when necessary. The adapter instance is added to the qualified unit GVL. When the optional PRG exists, that instance is called before selected node instances. Relation wiring participates in the same manifest, compiled-object collision scan, confirmation token, project integration, staging, rollback, rename, and deletion rules as every other managed artifact.

With generated runtime execution, `contains` creates parent-before-child ordering, while `usesRecipe` and `usesLink` include and schedule the dependency before its consumer. Configured `commandRoutes` execute inside relation wiring and copy only `bExecute`, the explicitly selected target enum value, and `nCommandID`; custom request payload is never guessed or overwritten. Missing routes remain passive typed adapters. More than one automatic routing relation for the same target is a validation conflict.

Phase 3C exposes this same transaction through the editor. The editor first requests a target-aware preview and receives a confirmation token derived from the resolved root, integration option, complete plan, expected and proposed hashes, artifacts, and manifests. Generate is permitted only for a saved model whose current document still matches the file on disk, a conflict-free plan, the exact token, and an explicit confirmation. Any changed model, target, integration option, or filesystem state invalidates the plan or is rejected by the executor preflight.

The later TwinCAT-first desktop workflow removes manual target selection from the editor. A native `.plcproj` selection creates or reopens the companion ETAB model in the same PLC directory, sets direct-root output, and enables project integration as one binding configuration. CLI callers may continue to use a separate generated subdirectory and explicit integration option.

## 10. Output Attributes

Generated PLC objects contain a clear notice in the ST source area, for example:

```iecst
(* <auto-generated by ETAB Engineering; source-id: ...> *)
```

Where TwinCAT attributes are useful, `{attribute 'TcGenerated'}` may also be used. An attribute does not replace manifest and hash validation.

## 11. Determinism

The following must not affect PLC output:

- timestamps,
- user name,
- absolute workspace path,
- canvas positions,
- current machine or XAE session.

Line endings and indentation are defined globally for the generator. The same model version must produce identical content and TwinCAT GUIDs on different machines.

## 12. Validation Evidence

Evidence is reported separately:

1. JSON schema validation successful.
2. Semantic model validation successful.
3. TwinCAT XML structurally valid.
4. TwinCAT project opens successfully.
5. TwinCAT compile successful.
6. Optional simulation successful.
7. Optional machine validation successful.

No earlier level of evidence substitutes for a later one.
