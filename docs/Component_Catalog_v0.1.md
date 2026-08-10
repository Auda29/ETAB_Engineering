# ETAB Component Catalog v0.1

## Purpose

This catalog defines which components of the current `ET_AutomationBase` library appear as independent nodes in the visual ETAB model v0.1. It is based on the statically reviewed `ET_AutomationBase_v0.1.0.3` state.

This classification is not evidence of a TwinCAT compile or runtime validation.

## Status Classes

- **MVP node:** can be modeled directly in project model v0.1.
- **Infrastructure:** treated as an option or generated detail of an MVP node.
- **Deferred:** current library component, but not part of the first visual model.
- **Project pattern:** example code, not a generic ETAB library component.

## MVP Nodes

### `applicationUnit`

Library foundation:

- `ETAB.FB_ETAB_ApplicationUnit`
- `ETAB.I_ETAB_ApplicationUnit`
- `ETAB.ST_ETAB_ApplicationUnitOptions`
- `ETAB.ST_ETAB_ApplicationUnitStatus`

Characteristics:

- extends `ET.Statemodel_Unit`,
- internally contains an `FB_ETAB_CommandUnit`,
- processes the basic ETAB commands,
- provides unit mode, unit state, and command status,
- can be used as a master unit or subunit.

Basic ETAB commands:

| Command | Value |
|---|---:|
| `NoAction` | 0 |
| `Reset` | 10 |
| `Start` | 20 |
| `Homing` | 25 |
| `Stop` | 30 |
| `Abort` | 40 |
| `Clear` | 50 |
| `User` | 100 |

Modeling in v0.1:

- Machine, Motion, Workpiece, and Process units are roles of an `applicationUnit`, not separate ETAB classes.
- Project-specific commands are mapped to `E_ETAB_UnitCommand.User` by default.
- Request payload and status payload are project-specific.
- `bExecute`, `eCommand`, and `nCommandID` belong to the implicit request contract and are not maintained as normal payload fields.

Generator target:

- command enum,
- request and status DUT,
- generated base function block,
- optional instance and status aggregation.

### `commandUnit`

Library foundation:

- `ETAB.FB_ETAB_CommandUnit`
- `ETAB.I_ETAB_CommandUnit`
- `ETAB.ST_ETAB_CommandOptions`
- `ETAB.ST_ETAB_CommandStatus`

Characteristics:

- generic command executor without its own ET state model,
- uses `ET.SEQUENCE_HDL`,
- supports start, finish, abort, and reset,
- provides sequence state, history, and errors.

Modeling in v0.1:

- suitable for sequence coordinators and project-specific function blocks,
- example: `FB_BM_ProcessCycle`,
- the actual CASE/sequence logic remains handwritten.

Generator target:

- optional command enum,
- request and status contract,
- command router or base scaffold,
- no automatic process sequence.

### `recipeManager`

Library foundation:

- `ETAB.FB_ETAB_RecipeManager`
- `ETAB.E_ETAB_RecipeCommand`
- `ETAB.ST_ETAB_RecipeOptions`
- `ETAB.ST_ETAB_RecipeStatus`

Supported library commands:

| Command | Value |
|---|---:|
| `NoAction` | 0 |
| `Read` | 10 |
| `Write` | 20 |
| `SaveAs` | 30 |
| `LoadDefault` | 40 |
| `Delete` | 50 |
| `Validate` | 60 |
| `Reset` | 70 |

Modeling in v0.1:

- references a project-specific recipe data type,
- describes file name, path, XPath, and options,
- does not generate a domain-specific recipe structure or validation logic,
- pointers, size, and external validation are bound later in the project adapter.

Generator target:

- instance and configuration scaffold,
- public status contract,
- no automatic serialization or domain-specific recipe logic.

### `machineLink`

Library foundation:

- `ETAB.FB_ETAB_MachineLink`
- `ETAB.ST_ETAB_MachineLinkData`
- `ETAB.ST_ETAB_MachineLinkOptions`
- `ETAB.ST_ETAB_MachineLinkStatus`

Bridge types:

| Bridge Type | Value |
|---|---:|
| `GenericBridge` | 0 |
| `EL6695` | 10 |
| `EL6692` | 20 |
| `ExternalBridge` | 30 |

Modeling in v0.1:

- Primary/Secondary role,
- bridge type,
- watchdog time,
- token and tie-break options,
- logical connection to a partner.

Generator target:

- instance and configuration scaffold,
- Rx/Tx and status contract,
- no automatic `%I*`/`%Q*` addressing.

## Infrastructure

### Machine-Link Adapters

- `FB_ETAB_MachineLinkEL6692`
- `FB_ETAB_MachineLinkEL6695`
- `FB_ETAB_MachineLinkExternalBridge`
- `FB_ETAB_MachineLinkDataByteMapper`

These components do not appear as independent nodes in v0.1. They are selected from the `machineLink` configuration or may later be exposed in an advanced view.

### ETAB Status and Options DUTs

Library DUTs such as `ST_ETAB_CommandStatus` or `ST_ETAB_ApplicationUnitStatus` are fixed contracts. In the editor, the user describes only additional project-specific status fields.

The generator does not modify these library DUTs. A project-specific status DUT embeds the appropriate library status as a field. For an `applicationUnit` with domain-specific typed commands, the unit lifecycle (`stUnit`) and domain operation (`stOperation`) are represented separately. Additional fields from `statusPayload` are appended afterward.

## Deferred

### FANUC Robot Interface

Library foundation:

- `FB_ETAB_FanucInterface`
- `FB_ETAB_FanucUopEtherCatDioMapper`
- FANUC command, request, status, and UOP DUTs

Rationale:

- vendor- and protocol-specific,
- not required to prove the generic ETAB model,
- intended to be added later as an integration node or plugin.

The component remains cataloged, but it is not a valid node kind in schema v0.1.

## Project Patterns from the Brush Machine

| Project Component | Classification | Treatment in the Model |
|---|---|---|
| `FB_BM_Application` | Composition Root | Project root, not automatically interpreted as an ETAB unit |
| `FB_BM_Machine` | Master Application Unit | `applicationUnit`, role `machine` |
| `FB_BM_MotionUnit` | Typed Application Unit | `applicationUnit`, role `motion` |
| `FB_BM_WorkpieceUnit` | Typed Application Unit | `applicationUnit`, role `workpiece` |
| `FB_BM_ProcessUnit` | Typed Application Unit | `applicationUnit`, role `process` |
| `FB_BM_ProcessCycle` | Sequence Coordinator | `commandUnit`, role `orchestrator` |
| `FB_BM_CommandBroker` | Operator-Command Arbitration | Project pattern, not generated in the MVP |
| `FB_BM_RecipeService` | Project-Specific Recipe Adapter | Uses `recipeManager` |
| `FB_BM_CellInterface` | Project-Specific Link Adapter | Uses `machineLink` |

## Sources in the Workspace

- `../../ET_AutomationBase_v0.1.0.3/README.md`
- `../../ET_AutomationBase_v0.1.0.3/ET_AutomationBase/ET_AutomationBase/ApplicationUnit/`
- `../../ET_AutomationBase_v0.1.0.3/ET_AutomationBase/ET_AutomationBase/RecipeManagement/`
- `../../ET_AutomationBase_v0.1.0.3/ET_AutomationBase/ET_AutomationBase/MachineInterface/`
- `../../ET_AutomationBase_v0.1.0.3/ET_AutomationBase/ET_AutomationBase/RobotInterface/Fanuc/`
