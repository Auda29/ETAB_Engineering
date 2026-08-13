# Phase 4A – Golden-Sample Reconciliation

## Scope

Phase 4A compares the 15-artifact BrushMachine reference output with the objects already compiled by `AutomationBase_Beispiel.plcproj`. The live workspace was inspected and previewed read-only. No generated file, manifest, project file, task object, PLC source, or running editor instance was changed.

## Compiled IEC-Name Conflicts

The current reference model proposes seven IEC names that the golden sample already compiles from user-owned paths:

| IEC object | Existing path | Generated path |
|---|---|---|
| `E_BM_MotionCommand` | `DUTs/Commands/E_BM_MotionCommand.TcDUT` | `Generated/DUTs/Commands/E_BM_MotionCommand.TcDUT` |
| `E_BM_WorkpieceCommand` | `DUTs/Commands/E_BM_WorkpieceCommand.TcDUT` | `Generated/DUTs/Commands/E_BM_WorkpieceCommand.TcDUT` |
| `E_BM_ProcessCommand` | `DUTs/Commands/E_BM_ProcessCommand.TcDUT` | `Generated/DUTs/Commands/E_BM_ProcessCommand.TcDUT` |
| `ST_BM_MotionRequest` | `DUTs/Commands/ST_BM_MotionRequest.TcDUT` | `Generated/DUTs/Requests/ST_BM_MotionRequest.TcDUT` |
| `ST_BM_WorkpieceRequest` | `DUTs/Commands/ST_BM_WorkpieceRequest.TcDUT` | `Generated/DUTs/Requests/ST_BM_WorkpieceRequest.TcDUT` |
| `ST_BM_ProcessRequest` | `DUTs/Commands/ST_BM_ProcessRequest.TcDUT` | `Generated/DUTs/Requests/ST_BM_ProcessRequest.TcDUT` |
| `ST_BM_MachineStatus` | `DUTs/Status/ST_BM_MachineStatus.TcDUT` | `Generated/DUTs/Status/ST_BM_MachineStatus.TcDUT` |

The integration planner now reads the named root object from existing compiled `.TcDUT`, `.TcPOU`, and `.TcGVL` files and compares IEC names case-insensitively. All seven conflicts appear in both CLI and editor previews and disable generation before any write.

## Semantic Comparison

The three existing command enums match the model exactly in literal names and numeric values:

- Motion: 17 of 17 entries, no difference,
- Workpiece: 9 of 9 entries, no difference,
- Process: 8 of 8 entries, no difference.

The three existing request DUTs also match the generated contract semantically. Field names, types, array bounds, and order agree, including `ARRAY[1..13] OF BOOL` for `aVacuumZone` and `ARRAY[1..3] OF LREAL` for `aBrushSpeed`. Only comments, formatting, GUID ownership, and file location differ.

The existing `ST_BM_MachineStatus` does not match the lean generated contract. The existing object is a 42-field project aggregate for application, units, commands, recipes, plant state, HMI phases, and FAT results. The proposed generated object has six fields: `stUnit`, `stOperation`, `bReadyForCycle`, `bRecoveryRequested`, `eLastMode`, and `eLastState`. Replacing the existing status would break its current consumers and is not safe.

## Task Assignment Decision

The existing `PlcTask.TcTTO` calls only `MAIN`, and `MAIN` calls the handwritten `FB_BM_Application` instance. The reference model therefore keeps `programCallStructure = false`.

ETAB Engineering v0.1 does not edit `.TcTTO` objects. If a generated PRG is enabled for another project, the PLC engineer must select exactly one cyclic entry path: call it once from an existing program or assign it manually to one task. Assigning and calling it simultaneously would execute the generated instances twice.

## Required Ownership Decision

The recommended next step is to keep all seven existing DUTs externally owned in a dedicated golden-sample integration model by disabling their corresponding generation flags. This preserves the existing GUIDs, paths, consumers, and aggregate machine-status contract. The alternative is to migrate the six semantically matching command/request types into `Generated/`, which changes file and GUID ownership and requires an explicit PLC migration. The existing machine-status DUT must remain external or the generated status must receive a distinct name.

No ownership switch has been applied yet because these alternatives materially change the golden sample. After that decision, the next validation sequence is: read-only preview, generation into a copy, repeated no-op generation, XAE open, full PLC compile, and the existing simulation/FAT regression.

## Automated Evidence

The integration test suite includes a case-insensitive duplicate IEC-name collision and verifies that the complete transaction is rejected without writes. Current automated result: 52 core tests and 7 service tests passed. Structural XML and automated file tests are not TwinCAT compile, simulation, or machine evidence.
