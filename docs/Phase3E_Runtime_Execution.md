# Phase 3E – Generated Runtime Execution

## Scope

Phase 3E closes the cyclic-execution gap without taking ownership of handwritten application logic. The project option `runtimeExecution` performs three related actions:

- generates `PRG_<prefix>_Generated`,
- calls `fbEtabRelationWiring()` first and then all selected nodes plus required Recipe Manager and Machine Link dependencies,
- assigns that PRG once to a detected TwinCAT task through a manifest-managed `PouCall`.

For ApplicationUnit and CommandUnit nodes the PRG maps the generated command enum to `ETAB.E_ETAB_UnitCommand`, applies the modeled options, invokes the FB, and copies its library status into the generated node status. Recipe Manager and Machine Link requests have kind-specific contracts and their model settings are applied directly. Recipe data/default instances and the Machine Link Tx data are exposed in `GVL_<prefix>_Units`.

An explicit `instanceType` used with runtime execution must remain call-compatible with the corresponding ETAB FB inputs and outputs. ETAB can validate the configured IEC name but cannot prove a handwritten FB signature without a TwinCAT compiler. Leaving `instanceType` empty uses the known ETAB/generated contract.

The call order is derived deterministically. `contains` parents run before children; `usesRecipe` and `usesLink` targets run before their consumers. A selected consumer automatically includes these dependencies. Cycles outside the validated `contains` hierarchy fall back to stable name/ID order rather than creating nondeterministic output.

`commands` relations remain explicit. They always expose a typed request adapter. When `commandRoutes` are configured, the editor enables runtime execution, relation wiring maps the selected source request command to the configured target request command before either node is called, and both endpoints are included in the runtime. ETAB does not infer a route from a visual line. One target may have only one automatic routing relation.

## Editor Workflow

In the project properties, **Enable generated runtime execution** enables the runtime program and initially selects every ApplicationUnit and CommandUnit instance. Each callable node then exposes **Run cyclically in generated runtime** for deliberate inclusion or exclusion. The Relations tab of a `commands` relation edits optional source-to-target command mappings. **Create user stubs** seeds editable ApplicationUnit FBs once; later generations preserve their contents.

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

Local validation on 2026-08-17 passes 79 Core tests and 10 editor-service tests. Runtime-specific coverage verifies:

- PRG generation and relation-wiring-first call order,
- project-command to ETAB-command mapping and node option/status bindings,
- dependency and ApplicationUnit hierarchy ordering,
- explicit automatic command-route generation and validation,
- one-time user-stub creation with preservation after manual edits,
- single-task and unique-`MAIN` task detection,
- preservation of handwritten `PouCall` entries,
- repeated synchronized no-op planning,
- removal of only the manifest-owned runtime call,
- rejection when the task changes after preview,
- rollback after an injected failure immediately following the task update.

The rebuilt CLI was also run read-only against the user's existing `TwinCAT Project5` model. The preview produced typed ApplicationUnit/CommandUnit calls, safe inactive Recipe Manager inputs for its legacy model without a request DUT, an active Machine Link cycle, generated status publication, and dependency-before-consumer ordering. The live TwinCAT project was not modified.

## Acceptance Boundary

These checks prove deterministic generation, task-file structure, ownership, synchronization, and rollback. The user's preceding generated project version built successfully in TwinCAT XAE, but the new request/status bindings and command routing have not yet been compiled or executed in TwinCAT. TwinCAT compile, online runtime behavior, simulation, safety validation, and machine acceptance remain separate user validation steps.
