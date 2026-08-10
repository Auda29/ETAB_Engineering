# Phase 0 Validation Record

## Status

- Phase: 0 – Specification
- Result: completed
- Validation date: 2026-08-10
- Implementation state: no generator or editor code; an isolated, non-instantiated TwinCAT compile spike is available

## Specification Artifacts Produced

- `docs/Component_Catalog_v0.1.md`
- `docs/Model_Specification_v0.1.md`
- `docs/Generation_Contract_v0.1.md`
- `docs/AutomationBase_Reference_v0.1.md`
- `schemas/etab-project.schema.json`
- `examples/BrushMachine.reference.etab.json`
- `spikes/TwinCAT_BaseFb_Inheritance.md`

## Architecture Addendum from 2026-08-10

- The command enum literal is named `enumValue` in the model and schema. It is explicitly not the runtime field `nCommandID`.
- Generated project-specific status DUTs embed the unchanged public library status DUTs and add only `statusPayload`.
- For Application Units with domain-specific typed commands, `stUnit : ETAB.ST_ETAB_ApplicationUnitStatus` and `stOperation : ETAB.ST_ETAB_CommandStatus` are maintained separately.
- The sources under `ET_AutomationBase_v0.1.0.3` were not modified for this addendum.

## Reference Inventory

The following were reviewed statically:

- public ApplicationUnit/CommandUnit contracts,
- RecipeManager contracts,
- MachineLink contracts and bridge types,
- FANUC components to delimit the MVP,
- `FB_BM_Application`,
- Master, Motion, Workpiece, and Process units,
- ProcessCycle and CommandBroker,
- domain-specific command enums and request/status contracts.

## JSON Schema Validation

Validator:

- JSON Schema Draft 2020-12
- Python package `jsonschema` with `Draft202012Validator`
- UUID format validation enabled

Result:

```text
VALID
```

## Semantic Validation of the Reference Model

Validated model values:

| Value | Result |
|---|---:|
| Nodes | 7 |
| Relationships | 12 |
| Total persistent IDs | 102 |
| Generatable artifact names | 14 |
| `contains` parent relationships | 4 |

Validated rules:

- all IDs are unique,
- all relationship endpoints exist,
- no self-relations,
- command names and `enumValue` values are unique within each node,
- `NoAction = 0` for generated command enums,
- valid array bounds,
- layout entries reference existing nodes,
- at most one parent per node,
- no `contains` cycles,
- relationship types match the target node,
- implicit request fields are not duplicated as payload,
- a request DUT is generated only together with a command enum,
- no collisions among generated artifact names,
- no collisions between project-specific status fields and reserved library-status fields.

Result:

```text
EXTENDED_SEMANTIC_CHECKS_VALID
```

## Acceptance Against Phase 0 Criteria

- [x] current public ETAB state cataloged
- [x] project model structurally defined
- [x] additional semantic rules defined
- [x] JSON schema v0.1 valid
- [x] BrushMachine reference model conforms to the schema
- [x] naming rules established as binding
- [x] model and TwinCAT ID rules established as binding
- [x] generator/user ownership boundary established as binding
- [x] example units classified
- [x] safety, I/O, and process implementations kept outside generation
- [x] `enumValue` clearly distinguished from runtime `nCommandID`
- [x] status aggregation defined without a library change
- [x] base-FB inheritance and hook override confirmed at compile time

## TwinCAT Compile Spike

Compile host:

- `AutomationBase_Beispiel.sln`
- configuration `Release | TwinCAT RT (x64)`
- three non-instantiated POUs under `POUs/Spikes/ETABEngineering/`

Execution through the local Beckhoff XAE DTE automation:

```text
TcXaeShell.DTE.15.0
LastBuildInfo=0
COMPILE_SUCCESS
```

The compile confirms the valid `FB_ETABENG_UserUnit -> FB_ETABENG_GeneratedUnitBase -> ETAB.FB_ETAB_ApplicationUnit` chain, inherited inputs and outputs, and the overridable protected `OnExecuteOperation` hook.

## Not Demonstrated

- no generator run,
- no `.TcPOU`/`.TcDUT` files produced by a generator; the three spike POUs are deliberately handwritten test artifacts,
- no online/runtime evidence for the spike test driver,
- no simulation,
- no machine validation.

These forms of evidence belong to later phases and are not replaced by the Phase 0 validations.
