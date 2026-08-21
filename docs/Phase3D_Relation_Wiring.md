# Phase 3D Validation – PLC Relation Wiring

## Scope

Phase 3D turns model relations into an optional, generated PLC adapter without deriving machine behavior. Enabling **Generate relation wiring** creates:

- `Generated/POUs/FB_<prefix>_Relations.TcPOU`,
- `GVL_<prefix>_Units.fbEtabRelationWiring : FB_<prefix>_Relations`.

Existing models remain backward compatible: absent or disabled `project.generation.relationWiring` keeps relations logical-only.

## Runtime Contract

| Relation | Generated behavior |
|---|---|
| `contains` | Assigns `rUnit.ipMasterUnit` for ApplicationUnit children. A CommandUnit child stays structural. |
| `commands` | Exposes an explicit method forwarding the standard ETAB command envelope to the target's `StartCommand`. |
| `observes` | Exposes an explicit typed read of the target ApplicationUnit or CommandUnit status. |
| `usesRecipe` | Exposes an explicit typed read of `ST_ETAB_RecipeStatus`. |
| `usesLink` | Exposes an explicit typed read of `ST_ETAB_MachineLinkStatus`. |

The generator does not choose commands, map project-specific payloads, call recipe operations, drive machine-link inputs, derive safety/interlocks, or create process, motion, recovery, and I/O logic. Those remain explicit project code.

## Validation and Determinism

- Every related source and target must generate a PLC instance while wiring is enabled.
- The reserved GVL instance name `fbEtabRelationWiring` is collision checked.
- Custom RecipeManager or MachineLink wrappers may declare a validated `relationStatusMember`; the standard default is `stStatus`.
- Relation methods are ordered by kind, source ID, target ID, and relation ID.
- Reordering nodes, relations, or canvas layout does not alter generated content.
- Member names longer than 80 characters receive a deterministic relation-ID suffix.
- The artifact uses a project-derived UUID-v5 TwinCAT GUID and participates in manifests, hashes, conflict checks, project integration, and transactional rollback.
- If the optional generated PRG is enabled, relation wiring is called before selected node instances. With the later `runtimeExecution` option, Phase 3E assigns this PRG once to the detected TwinCAT task; the legacy PRG-only option remains available without task modification.

## Automated Evidence

The test suite covers all five relation types, logical-only backward compatibility, missing instance validation, relation-order independence, generated GVL and PRG integration, manifest round-trip, update/rename behavior, XML parsing, hashes, and TwinCAT project compile entries.

The current stable-release validation on 2026-08-21 passes all 91 Core tests and all 10 editor-service tests. Manual TwinCAT evidence additionally covers a successful rebuild of the task-bound `TwinCAT Project5` model and a successful `Rebuild All` of the 36-node, 62-relation OmniGrind model with 168 managed artifacts.

## Acceptance Boundary

The automated checks prove deterministic generated XML and project-file integration. The two successful TwinCAT rebuilds add compile evidence for the generated adapters and their project integration. They do not prove that adapter methods execute with the intended online values. The remaining live step is to exercise each relevant relation path online, verify command handoff and observed status values, and then perform simulation, safety, and machine acceptance separately.
