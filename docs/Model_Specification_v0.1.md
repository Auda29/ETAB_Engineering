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
- `layout`: visual position and size plus generated TwinCAT area-folder assignments.

Canvas coordinates and sizes do not change PLC output. Area declarations and node assignments change generated paths and TwinCAT folder entries without changing generated object content or deterministic GUIDs.

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
- optional `relationStatusMember` when a custom RecipeManager or MachineLink wrapper exposes its ETAB status under a member other than `stStatus`
- optional `callInProgram` selection for the generated PRG

Invalid combinations are rejected semantically. A `recipeManager` and `machineLink` do not generate project-specific command enums; their optional request DUTs instead embed the corresponding ETAB library command or link input contract.

A disabled artifact flag means that ETAB Engineering does not own or emit that artifact. This supports integration with existing PLC contracts: `examples/BrushMachine.integration.etab.json` disables the three command enums, three request DUTs, and aggregate machine-status DUT that are already compiled from handwritten project paths. The full reference model remains available separately as the complete generator example.

Phase 3B collects all enabled instances and their generated request/status contracts in a deterministic, qualified `GVL_<prefix>_Units`. If `instanceType` is omitted, an ApplicationUnit uses its generated base FB, or its one-time user stub when `createUserStubs` is enabled; the remaining kinds use the matching ETAB library FB. The legacy project-wide `programCallStructure` option emits no-argument calls without assigning the PRG to a task. The preferred `runtimeExecution` option generates typed runtime bindings and, during linked project integration, manages exactly one corresponding `PouCall` in the detected TwinCAT task. The PRG invokes relation wiring first, maps request commands, applies node settings, calls the ETAB FBs in dependency order, and publishes generated status contracts.

Task selection is deterministic. A project with one compiled `.TcTTO` uses that task. With multiple task objects, exactly one must already call `MAIN`; otherwise generation is blocked as ambiguous. Existing `PouCall` entries are preserved. ETAB owns only the generated call recorded in the project-integration manifest and removes only that call when runtime execution is disabled.

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
- Every project command maps explicitly to one ETAB target through `etabCommand`; domain-specific operations normally use `User`.
- Direct mapping to `Stop` or `Abort` is intended only for actual ETAB unit commands.
- Numeric values are sorted in ascending order during generation; equal values make the model invalid.

## 7. Request and Status Payload

### 7.1 Implicit Request Header

For `applicationUnit` and `commandUnit`, `requestType = true` creates:

```iecst
bExecute   : BOOL;
eCommand   : <generated command enum>;
nCommandID : UDINT;
```

These fields must therefore not be defined again under `requestPayload`.

`recipeManager` instead receives `bExecute`, `ETAB.E_ETAB_RecipeCommand`, external-validation state, and an optional Save-As filename. `machineLink` receives enable, local token/busy/error/state, Rx data, and bridge availability. Both kinds may still add project fields through `requestPayload` without generating a project command enum.

### 7.2 Generated Status Contract

When `statusType = true`, the generator creates a project-specific status DUT. It neither changes nor duplicates any DUT definition from the `ET_AutomationBase` library; instead, it embeds the library's public status as a field and adds only the project fields described under `statusPayload`.

For an `applicationUnit`, the fixed header is:

```iecst
stUnit : ETAB.ST_ETAB_ApplicationUnitStatus;
```

If the unit defines project commands, the status of the domain-specific command is added separately:

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

| Type | Source kind | Target kind | Meaning |
|---|---|---|---|
| `contains` | `applicationUnit` | `applicationUnit` or `commandUnit` | hierarchical parent/child assignment |
| `commands` | `applicationUnit` or `commandUnit` | `applicationUnit` or `commandUnit` | source creates requests for the target |
| `observes` | `applicationUnit` or `commandUnit` | `applicationUnit` or `commandUnit` | source reads the target's status |
| `usesRecipe` | `applicationUnit` or `commandUnit` | `recipeManager` | source uses a RecipeManager |
| `usesLink` | `applicationUnit` or `commandUnit` | `machineLink` | source uses a MachineLink |

### Semantic Rules

- Source and target must exist.
- Self-relations are not permitted.
- Duplicate relations with the same type, source, and target are not permitted.
- `contains` must not form cycles.
- A node has at most one parent through `contains`.
- Source and target kinds must match the table above.

Safety and collision enables are not relationship types in v0.1.

### 8.1 Optional PLC Relation Wiring

`project.generation.relationWiring = true` generates `FB_<prefix>_Relations` and its qualified `GVL_<prefix>_Units.fbEtabRelationWiring` instance. Every relation endpoint must then have `generate.instance = true`; otherwise semantic validation rejects the model. Omitting the option or setting it to `false` keeps relations as logical documentation only for backward compatibility.

The generated adapter has a deliberately narrow runtime contract:

- `contains` assigns `rUnit.ipMasterUnit` for ApplicationUnit children; CommandUnit children remain structural because they have no ET state-model parent reference.
- `commands` creates a typed method that accepts the target's generated request DUT. Optional `commandRoutes` require runtime execution and copy execute, the configured target enum literal, and command ID from a source request to the target request before cyclic calls.
- `observes` returns the target's generated status DUT when available, otherwise its configured ETAB status member.
- `usesRecipe` and `usesLink` return the generated target status when available and make the target a runtime dependency of the source.

The default status member for RecipeManager and MachineLink instances is `stStatus`. A custom wrapper may select another IEC member with `generate.relationStatusMember`; the BrushMachine recipe wrapper uses `stManagerStatus`.

The adapter selects a target command only when a specific `commandRoutes` entry exists. It never maps custom request payload fields, derives interlocks, or creates safety, motion, process, recovery, or I/O behavior. Those decisions remain handwritten project logic. With `runtimeExecution`, relation wiring runs first, dependency targets run before consumers, and ApplicationUnit parents run before children. Otherwise project code may call `fbEtabRelationWiring()` explicitly.

## 9. Layout

The layout references nodes exclusively through `nodeId` and stores:

- `x`, `y`,
- optional width and height,
- an optional `group` identifier assigning the node to an editor area.

`layout.groups` optionally declares persistent editor areas with a unique IEC-compatible `name` and a user-facing `displayName`. The declaration permits empty areas to survive save/reopen and produces a corresponding TwinCAT folder. Legacy models that only use `nodeLayout.group` remain valid; the editor derives their display names and materializes declarations when an area is edited.

Relations remain global node-to-node contracts and may connect nodes in different areas. Area display names and node assignments determine `Application/<area>/<node>` output paths and participate in the semantic model hash. Positions, sizes, and canvas zoom remain visual only. Moving a node does not change the artifact content or TwinCAT GUID.

A node may have at most one layout entry. A node without an assigned declared area is generated below `Application/Unassigned/<node>`. Area and node display names must be valid Windows folder names and unique within their generated parent folder.

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
- Canvas coordinates, sizes, and input order are excluded from PLC generation. Area display names and assignments determine output paths.

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
