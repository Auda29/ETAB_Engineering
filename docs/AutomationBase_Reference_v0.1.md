# AutomationBase Reference Classification v0.1

## 1. Purpose and Boundary

The `AutomationBase Beispiel` project serves as the golden sample for model v0.1. This classification describes structure, responsibilities, and public contracts. It does not incorporate process implementations into the generator.

Validation basis: static source review in the workspace. No TwinCAT compile, simulation, or machine validation was performed for this document.

## 2. Logical Structure

```text
FB_BM_Application                         Composition Root
├─ FB_BM_Machine                         Master Application Unit
│  ├─ FB_BM_MotionUnit                   Subunit / Axis Commands
│  ├─ FB_BM_WorkpieceUnit                Subunit / Workpiece Handling
│  ├─ FB_BM_ProcessUnit                  Subunit / Brushes and Extraction
│  └─ FB_BM_ProcessCycle                 Sequence Coordinator
├─ FB_BM_RecipeService                   Recipe Adapter
└─ FB_BM_CellInterface                   Machine-Link Adapter
```

In the source code, MotionUnit, WorkpieceUnit, and ProcessUnit are bound to `FB_BM_Machine` through `rUnit.ipMasterUnit`. `FB_BM_ProcessCycle` creates typed requests for the three functional units.

## 3. Node Classification

### `FB_BM_Application`

- Classification: composition root.
- ETAB kind: none.
- Responsibility: call order, provider selection, request arbitration, status aggregation, and output transfer.
- Generator boundary: no automatically generated sequence body.
- Model treatment: project root and source of the overall visual layout.

### `FB_BM_Machine`

- Classification: master application unit.
- ETAB kind: `applicationUnit`.
- Role: `machine`.
- Base: `EXTENDS ETAB.FB_ETAB_ApplicationUnit`.
- Contains the three functional subunits and the sequence coordinator.
- Project-specific status: ready for cycle, recovery request, last mode, and last state.

### `FB_BM_MotionUnit`

- Classification: typed application unit.
- ETAB kind: `applicationUnit`.
- Role: `motion`.
- Exclusive responsibility: six axis commands.
- Internally uses `ETAB.FB_ETAB_CommandUnit`.
- All functional commands except `NoAction` are mapped to `E_ETAB_UnitCommand.User`.

Request payload:

- `stJobData : ST_BM_JobData`

Status payload:

- `bAllHomed : BOOL`
- `bAllSafe : BOOL`
- `bAllStandstill : BOOL`
- `bSynchronized : BOOL`
- `fMeasuredLength : LREAL`

### `FB_BM_WorkpieceUnit`

- Classification: typed application unit.
- ETAB kind: `applicationUnit`.
- Role: `workpiece`.
- Exclusive responsibility: door, vacuum, blow-off, and laser.
- Internally uses `ETAB.FB_ETAB_CommandUnit`.
- All functional commands except `NoAction` are mapped to `E_ETAB_UnitCommand.User`.

Request payload:

- `aVacuumZone : ARRAY[1..13] OF BOOL`
- `fVacuumMinimum : LREAL`
- `tVacuumTimeout : TIME`
- `tBlowTime : TIME`

Status payload:

- `bDoorSafe : BOOL`
- `bWorkpieceClamped : BOOL`
- `bVacuumReleased : BOOL`
- `bBlowComplete : BOOL`
- `fMeasuredLength : LREAL`

### `FB_BM_ProcessUnit`

- Classification: typed application unit.
- ETAB kind: `applicationUnit`.
- Role: `process`.
- Exclusive responsibility: brushes and extraction.
- Internally uses `ETAB.FB_ETAB_CommandUnit`.
- All functional commands except `NoAction` are mapped to `E_ETAB_UnitCommand.User`.

Request payload:

- `aBrushSpeed : ARRAY[1..3] OF LREAL`
- `tBrushTimeout : TIME`
- `tExhaustTimeout : TIME`
- `tExhaustRunOn : TIME`

Status payload:

- `bExhaustAvailable : BOOL`
- `bSeamBrushesAtSpeed : BOOL`
- `bEndBrushAtSpeed : BOOL`
- `bAllBrushesStopped : BOOL`

### `FB_BM_ProcessCycle`

- Classification: sequence coordinator.
- ETAB kind: `commandUnit`.
- Role: `orchestrator`.
- Uses `ETAB.FB_ETAB_CommandUnit`.
- Creates Motion, Workpiece, and Process requests.
- Observes the command status of the three units.
- Uses recipe, job, and interlock data.
- Has controlled stop, abort, and recovery paths.
- The sequence steps remain entirely handwritten.

### `FB_BM_CommandBroker`

- Classification: project pattern for operator-command arbitration.
- Prioritizes Abort, Stop, Reset, Recover, Start, Home, and Clear.
- Is not modeled as a generic ETAB node in v0.1.
- May later be evaluated as a reusable editor/generator pattern.

### Recipe and Machine Link

- `FB_BM_RecipeService` is treated as a project-specific adapter around a `recipeManager`.
- `FB_BM_CellInterface` is treated as a project-specific adapter around a `machineLink`.
- Project data types, hardware coupling, and domain validation remain handwritten.

## 4. Relationships in the Reference Model

| Source | Relationship | Target |
|---|---|---|
| Machine | `contains` | MotionUnit |
| Machine | `contains` | WorkpieceUnit |
| Machine | `contains` | ProcessUnit |
| Machine | `contains` | ProcessCycle |
| ProcessCycle | `commands` | MotionUnit |
| ProcessCycle | `commands` | WorkpieceUnit |
| ProcessCycle | `commands` | ProcessUnit |
| ProcessCycle | `observes` | MotionUnit |
| ProcessCycle | `observes` | WorkpieceUnit |
| ProcessCycle | `observes` | ProcessUnit |
| ProcessCycle | `usesRecipe` | RecipeManager |
| Machine | `usesLink` | CellLink |

## 5. Generatable Portion

- command enums for the functional units,
- request DUTs including the implicit ETAB request header,
- project-specific status DUTs,
- base scaffolds for ApplicationUnit and CommandUnit,
- instance and relationship scaffolds,
- future status aggregation.

## 6. Handwritten Portion

- call and arbitration logic in `FB_BM_Application`,
- complete sequence logic of the units,
- the `FB_BM_ProcessCycle` sequence,
- safety and interlock evaluation,
- output arbitration,
- I/O and hardware providers,
- simulation,
- recovery and safe-stop details,
- domain-specific recipe and job validation.

## 7. Source Paths

- `../../AutomationBase Beispiel/AutomationBase_Beispiel/AutomationBase_Beispiel/AutomationBase_Beispiel/POUs/Application/FB_BM_Application.TcPOU`
- `../../AutomationBase Beispiel/AutomationBase_Beispiel/AutomationBase_Beispiel/AutomationBase_Beispiel/POUs/Units/FB_BM_Machine.TcPOU`
- `../../AutomationBase Beispiel/AutomationBase_Beispiel/AutomationBase_Beispiel/AutomationBase_Beispiel/POUs/Units/FB_BM_MotionUnit.TcPOU`
- `../../AutomationBase Beispiel/AutomationBase_Beispiel/AutomationBase_Beispiel/AutomationBase_Beispiel/POUs/Units/FB_BM_WorkpieceUnit.TcPOU`
- `../../AutomationBase Beispiel/AutomationBase_Beispiel/AutomationBase_Beispiel/AutomationBase_Beispiel/POUs/Units/FB_BM_ProcessUnit.TcPOU`
- `../../AutomationBase Beispiel/AutomationBase_Beispiel/AutomationBase_Beispiel/AutomationBase_Beispiel/POUs/Commands/FB_BM_ProcessCycle.TcPOU`
- `../../AutomationBase Beispiel/AutomationBase_Beispiel/AutomationBase_Beispiel/AutomationBase_Beispiel/POUs/Commands/FB_BM_CommandBroker.TcPOU`
