# Phase 3E – Generated Runtime Execution

## Scope

Phase 3E closes the cyclic-execution gap without taking ownership of handwritten application logic. The project option `runtimeExecution` performs three related actions:

- generates `PRG_<prefix>_Generated`,
- calls `fbEtabRelationWiring()` first and then the selected ApplicationUnit and CommandUnit instances,
- assigns that PRG once to a detected TwinCAT task through a manifest-managed `PouCall`.

RecipeManager and MachineLink instances remain available in `GVL_<prefix>_Units`, but the editor does not select them for automatic cyclic calls because project-specific wrappers may require explicit inputs.

## Editor Workflow

In the project properties, **Enable generated runtime execution** enables the runtime program and initially selects every ApplicationUnit and CommandUnit instance. Each callable node then exposes **Run cyclically in generated runtime** for deliberate inclusion or exclusion.

The generation preview lists the TwinCAT task as a separate integration change. Selecting it displays the complete proposed `.TcTTO` XML before generation. The final confirmation counts artifact and project-integration changes separately.

## Task Selection and Ownership

Task selection is deterministic:

1. With one compiled `.TcTTO`, ETAB selects it.
2. With multiple compiled task objects, ETAB selects the unique task that already calls `MAIN`.
3. If no unique task can be proven, generation stops with `PLC_TASK_AMBIGUOUS`.

Existing task calls and task metadata remain byte-preserved. ETAB records only `taskFile` and `programName` under `managedTaskPouCall` in `etab-project-integration-manifest.json`. Disabling runtime execution removes only that managed call. A missing, duplicated, non-standard, externally changed, or no-longer-compiled managed call blocks the complete generation transaction.

The legacy `programCallStructure` option remains backward compatible: it generates the PRG but does not edit a task unless `runtimeExecution` is also enabled.

## Transaction Safety

The task file participates in the same conflict-protected transaction as generated artifacts, the generation manifest, the `.plcproj`, and the project-integration manifest:

- preview records the original task hash and complete proposed content,
- a changed task rejects execution before any write,
- the proposed task is staged and hash-verified,
- the original task is backed up before replacement,
- any later failure restores the task, project file, and generated files together.

Reparse points, rooted paths, traversal segments, missing task files, invalid task XML, and task paths outside the selected PLC root are rejected.

## Automated and Project-Copy Evidence

Local validation on 2026-08-17 passes 73 Core tests and 10 editor-service tests. Runtime-specific coverage verifies:

- PRG generation and relation-wiring-first call order,
- single-task and unique-`MAIN` task detection,
- preservation of handwritten `PouCall` entries,
- repeated synchronized no-op planning,
- removal of only the manifest-owned runtime call,
- rejection when the task changes after preview,
- rollback after an injected failure immediately following the task update.

The new CLI was also run against an isolated copy of the user-built `TwinCAT Project5` PLC directory. Preview proposed exactly one new artifact, one `.plcproj` compile entry, and one `PlcTask.TcTTO` call. Generation retained `MAIN`, inserted `PRG_PLC_Generated` before the existing task metadata, and produced a PRG that calls relation wiring before the four selected callable instances. A repeated integrated CLI check reported `CHECK SYNCHRONIZED`. The live TwinCAT project was not modified.

## Acceptance Boundary

These checks prove deterministic generation, task-file structure, ownership, synchronization, and rollback. The user's preceding project version built successfully in TwinCAT XAE, but the new task-integrated project copy has not yet been compiled or executed in TwinCAT. TwinCAT compile, online runtime behavior, simulation, safety validation, and machine acceptance remain separate user validation steps.
