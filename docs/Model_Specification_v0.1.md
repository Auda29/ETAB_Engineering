# ETAB Project Model – Specification v0.1

## 1. Scope

This specification defines the persistent project model for the first ETAB Engineering MVP. The normative machine structure is stored in a JSON file with the `.etab.json` extension.

The model describes PLC contracts and logical relationships. It does not describe executable safety, motion, or process logic.

Normative structural validation: `../schemas/etab-project.schema.json`.

## 2. Basic Structure

```json
{
  "schemaVersion": "0.1",
  "project": {},
  "nodes": [],
  "relations": [],
  "layout": { "nodes": [] }
}
```

### Sections

- `project`: project-wide naming, version, and output settings.
- `nodes`: ETAB components and project-specific roles.
- `relations`: normative logical connections.
- `layout`: visual position and size only.

Changes under `layout` must not cause any change to the PLC output.

## 3. Identities

### 3.1 Model ID

- Every persistent entity has a UUID.
- UUIDs are generated randomly when an entity is created and are not changed afterward.
- Renaming does not change a model ID.
- IDs are not derived from names or positions.
- IDs are stored in lowercase in JSON.

### 3.2 TwinCAT Object ID

The TwinCAT GUID of a generated object is derived deterministically as a UUID v5 from the following components:

1. fixed ETAB Engineering generator namespace,
2. project ID,
3. model ID,
4. artifact kind, for example `command-enum` or `request-dut`.

This keeps the TwinCAT GUID stable across renames and on different engineering machines. The generation manifest records the derived GUID, but is not its only source.

Fixed UUID v5 namespace for ETAB Engineering v0.x:

```text
8d487292-cc21-4f2e-8c6e-3c4742e1d8a1
```

The name to be hashed is UTF-8 encoded and constructed as follows:

```text
<project-id>/<model-id>/<artifact-kind>
```

## 4. Names

### 4.1 `project.prefix`

- uppercase letters and digits,
- starts with an uppercase letter,
- two to sixteen characters,
- example: `BM`.

### 4.2 PLC Names

- IEC identifiers without spaces,
- start with a letter or underscore,
- contain only letters, digits, and underscores,
- PascalCase is preferred for FBs and type stems.

### 4.3 Node Names

- `name`: function block name without `FB_<Prefix>_`, for example `MotionUnit`.
- `symbolStem`: stem for the command, request, and status DUTs, for example `Motion`.
- `displayName`: freely readable display name.
- `role`: semantic project role such as `machine`, `motion`, `workpiece`, `process`, or `orchestrator`.

### 4.4 Generated Names

For prefix `BM`, node name `MotionUnit`, and `symbolStem` `Motion`:

| Artifact | Name |
|---|---|
| Command Enum | `E_BM_MotionCommand` |
| Request DUT | `ST_BM_MotionRequest` |
| Status DUT | `ST_BM_MotionStatus` |
| Base FB | `FB_BM_MotionUnitBase` |
| Command Router (planned for Phase 3 onward) | `FB_BM_MotionCommandRouter` |

Naming collisions inside the model are treated as validation errors. When `.plcproj` integration is selected, case-insensitive collisions with IEC objects already compiled from other project paths are planning conflicts and block all writes.

## 5. Nodes

Valid `kind` values in v0.1:

- `applicationUnit`
- `commandUnit`
- `recipeManager`
- `machineLink`

Specializations are described through `role` and do not create a new library base type.

### 5.1 Generation Options

Each node explicitly defines:

- `commandEnum`
- `requestType`
- `statusType`
- `baseFunctionBlock`
- `instance`
- optional `instanceType` for a project-specific function-block type
- optional `callInProgram` selection for the generated PRG

Invalid combinations are rejected semantically. For example, a `recipeManager` does not generate a project-specific command enum in the MVP.

A disabled artifact flag means that ETAB Engineering does not own or emit that artifact. This supports integration with existing PLC contracts: `examples/BrushMachine.integration.etab.json` disables the three command enums, three request DUTs, and aggregate machine-status DUT that are already compiled from handwritten project paths. The full reference model remains available separately as the complete generator example.

Phase 3B collects all enabled instances in a deterministic, qualified `GVL_<prefix>_Units`. If `instanceType` is omitted, an ApplicationUnit uses its generated base FB when available; otherwise the matching ETAB library FB is used. The project-wide `programCallStructure` option creates `PRG_<prefix>_Generated`, which invokes only nodes selected with `callInProgram`. This does not assign the PRG to a TwinCAT task.

### 5.2 `applicationUnit`

May contain the following settings:

- start, homing, and stop mode,
- remote-control behavior,
- propagation of errors to the unit error handler,
- initial state and reset-on-start behavior of the internal command handler.

### 5.3 `commandUnit`

May configure the initial state and reset-on-start behavior. The actual sequence implementation is not part of the model.

### 5.4 `recipeManager`

References the project-specific recipe data type and describes file/XPath settings. Pointers, memory size, and domain validation remain project code.

### 5.5 `machineLink`

Describes bridge type, role, watchdog, and protocol options. Hardware addresses remain outside the model.

## 6. Commands

A command contains:

- stable `id`,
- `name`,
- numeric `enumValue`,
- readable name and optional description,
- mapping to an `ETAB.E_ETAB_UnitCommand`.

Valid ETAB targets:

- `NoAction`
- `Reset`
- `Start`
- `Homing`
- `Stop`
- `Abort`
- `Clear`
- `User`

### Semantic Rules

- The stable command `id` is globally unique; `name` and `enumValue` are unique within a node.
- If `commandEnum = true`, exactly one `NoAction` with value `0` exists.
- Project-specific typed commands are always mapped to `User` in v0.1.
- Direct mapping to `Stop` or `Abort` is intended only for actual ETAB unit commands.
- Numeric values are sorted in ascending order during generation; equal values make the model invalid.

## 7. Request and Status Payload

### 7.1 Implicit Request Header

When `requestType = true`, the generator automatically creates:

```iecst
bExecute   : BOOL;
eCommand   : <generated command enum>;
nCommandID : UDINT;
```

These fields must therefore not be defined again under `requestPayload`.

### 7.2 Generated Status Contract

When `statusType = true`, the generator creates a project-specific status DUT. It neither changes nor duplicates any DUT definition from the `ET_AutomationBase` library; instead, it embeds the library's public status as a field and adds only the project fields described under `statusPayload`.

For an `applicationUnit`, the fixed header is:

```iecst
stUnit : ETAB.ST_ETAB_ApplicationUnitStatus;
```

If the unit also has a project-specific command enum and request contract, the status of the domain-specific command is added separately:

```iecst
stOperation : ETAB.ST_ETAB_CommandStatus;
```

`stUnit.stCommand` remains the library lifecycle/unit-command status. In contrast, `stOperation` belongs to a project-specific command such as `HomeAll`, `MeasureLength`, or `ParkAll`.

For the other node kinds, the generator uses the following fixed headers:

| Node Kind | Embedded Library Status |
|---|---|
| `commandUnit` | `stCommand : ETAB.ST_ETAB_CommandStatus` |
| `recipeManager` | `stRecipe : ETAB.ST_ETAB_RecipeStatus` |
| `machineLink` | `stLink : ETAB.ST_ETAB_MachineLinkStatus` |

Depending on the node kind, the names `stUnit`, `stOperation`, `stCommand`, `stRecipe`, and `stLink` are reserved and must not appear again in `statusPayload`.

Example:

```iecst
TYPE ST_BM_MotionStatus :
STRUCT
    stUnit      : ETAB.ST_ETAB_ApplicationUnitStatus;
    stOperation : ETAB.ST_ETAB_CommandStatus;
    bAllHomed   : BOOL;
    bAllSafe    : BOOL;
END_STRUCT
END_TYPE
```

This structure is generated under `Generated/` in the target project. No change or new version of the `ET_AutomationBase` library is required.

### 7.3 Payload Field

A field contains:

- stable ID,
- IEC name,
- TwinCAT data type,
- optional array dimensions,
- optional description,
- optional initial value as a TwinCAT literal.

Arrays are built from the base type and `arrayDimensions`. Example:

```json
{
  "name": "aBrushSpeed",
  "dataType": "LREAL",
  "arrayDimensions": [{ "lower": 1, "upper": 3 }]
}
```

Generated ST:

```iecst
aBrushSpeed : ARRAY[1..3] OF LREAL;
```

## 8. Relationships

Valid relationship types:

| Type | Meaning |
|---|---|
| `contains` | hierarchical master/subunit assignment |
| `commands` | source creates requests for the target |
| `observes` | source reads the target's status |
| `usesRecipe` | source uses a RecipeManager |
| `usesLink` | source uses a MachineLink |

### Semantic Rules

- Source and target must exist.
- Self-relations are not permitted.
- `contains` must not form cycles.
- A node has at most one parent through `contains`.
- The target of `usesRecipe` is a `recipeManager`.
- The target of `usesLink` is a `machineLink`.
- The target of `commands` is an `applicationUnit` or `commandUnit`.

Safety and collision enables are not relationship types in v0.1.

## 9. Layout

The layout references nodes exclusively through `nodeId` and stores:

- `x`, `y`,
- optional width and height,
- optional grouping.

A node may have at most one layout entry. Missing layout does not affect model validity; in that case, the editor may position the node automatically.

## 10. MTP Preparation

A node may optionally contain an `mtp` block:

- `exposed`,
- service name,
- procedures with a stable ID, procedure ID, and referenced command ID.

In v0.1, these properties are stored and validated, but not yet generated. State mapping becomes binding only when it is implemented in Phase 5.

## 11. Deterministic Ordering

The generator uses the following ordering:

- Nodes: `name`, then `id`.
- Commands: `enumValue`, then `name`, then `id`.
- Fields: model order is PLC order and therefore semantic.
- Relationships: `kind`, `sourceNodeId`, `targetNodeId`, `id`.
- Layout is excluded from PLC generation.

## 12. Validation Outside the JSON Schema

The semantic validator implemented in Phase 1 additionally checks:

- global uniqueness of all IDs,
- referential integrity,
- naming collisions of generated artifacts,
- uniqueness of stable command IDs and `enumValue` values within each node,
- array bounds,
- relationship types appropriate to the node kind,
- acyclic hierarchy,
- valid generator options for each node kind,
- no collision between status-payload fields and library-status fields reserved for the node kind,
- MTP procedure ID and command references.

## 13. One-Way Principle of the MVP

The MVP supports only:

```text
.etab.json → generated TwinCAT objects
```

Manually modified PLC code is not imported back into the model. A future import of existing projects is a separate feature, not a generator round trip.
