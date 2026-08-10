# ETAB Engineering v0.1.0.0

Visual engineering tool for describing a logical machine and subsequently generating a TwinCAT PLC template based on the `ET_AutomationBase` library.

## Current Status

Phase 0 – Specification: completed on 2026-08-07, architecture addendum validated on 2026-08-10. Phase 1 – Headless Generator Core and Phase 2 – Visual Editor MVP: completed on 2026-08-10.

The model, rules, generation boundary, and reference classification have been defined and verified against the BrushMachine reference model. `enumValue`, the project-specific status contract, and the base-FB inheritance pattern have been conclusively defined. The base-FB spike compiles successfully in the BrushMachine project.

The headless core is implemented as a .NET solution. It loads `*.etab.json` files and validates them against JSON Schema Draft 2020-12 and the project-specific semantic rules. The CLI commands `validate`, `preview`, `check`, and `generate` are available. The generator renders command enums, request and status DUTs, and lean ApplicationUnit base FBs; manages stable TwinCAT GUIDs and content hashes in the manifest; and applies conflict-free changes transactionally and exclusively within the configured generator-owned area.

The TypeScript visual editor and its loopback .NET service are implemented. The editor opens and saves complete `*.etab.json` documents, provides a component palette, hierarchy, property and contract editors, relationships, a draggable SVG canvas, live validation, and a read-only generation preview. Both the editor service and CLI call the same `ETAB.Engineering.Core`; the UI contains no second generator implementation.

GVL/PRG generation and `.plcproj` integration are not yet included. A real compile of the generated project artifacts is therefore part of Phase 3; the underlying base-FB inheritance and hook pattern was already compiled successfully in TwinCAT during Phase 0.

## Quick Start

### CLI

```powershell
dotnet build .\ETAB.Engineering.sln
dotnet test .\ETAB.Engineering.sln --no-build
dotnet run --project .\src\ETAB.Engineering.Cli\ETAB.Engineering.Cli.csproj -- validate .\examples\BrushMachine.reference.etab.json
dotnet run --project .\src\ETAB.Engineering.Cli\ETAB.Engineering.Cli.csproj -- preview .\examples\BrushMachine.reference.etab.json --root . --content
dotnet run --project .\src\ETAB.Engineering.Cli\ETAB.Engineering.Cli.csproj -- generate .\examples\BrushMachine.reference.etab.json --root .
dotnet run --project .\src\ETAB.Engineering.Cli\ETAB.Engineering.Cli.csproj -- check .\examples\BrushMachine.reference.etab.json --root .
```

`generate` is the explicit write command. It aborts on conflicts before performing any write and does not modify files outside the generator-owned area configured in the model.

### Visual Editor

Start the local service in the first terminal:

```powershell
dotnet run --project .\src\ETAB.Engineering.Service\ETAB.Engineering.Service.csproj
```

Install and start the editor in a second terminal:

```powershell
npm.cmd --prefix .\src\ETAB.Engineering.Editor install
npm.cmd --prefix .\src\ETAB.Engineering.Editor run dev
```

Open `http://127.0.0.1:5173/`. The editor initially loads the BrushMachine reference model and talks only to the loopback service at `http://127.0.0.1:5079/`.

## Documents

- [Overall Plan](ETAB_Engineering_Plan.md)
- [ETAB Component Catalog v0.1](docs/Component_Catalog_v0.1.md)
- [Model Specification v0.1](docs/Model_Specification_v0.1.md)
- [Generation Contract v0.1](docs/Generation_Contract_v0.1.md)
- [AutomationBase Reference Classification](docs/AutomationBase_Reference_v0.1.md)
- [Phase 0 Validation Record](docs/Phase0_Validation.md)
- [Phase 1A Validation Record](docs/Phase1A_Validation.md)
- [Phase 1B Validation Record](docs/Phase1B_Validation.md)
- [Phase 1C Validation Record](docs/Phase1C_Validation.md)
- [Phase 1 Completion Validation](docs/Phase1_Validation.md)
- [Phase 2 Visual Editor Validation](docs/Phase2_Validation.md)
- [TwinCAT Base-FB Inheritance Spike](spikes/TwinCAT_BaseFb_Inheritance.md)
- [JSON Schema v0.1](schemas/etab-project.schema.json)
- [BrushMachine Reference Model](examples/BrushMachine.reference.etab.json)

## Phase 0 Acceptance

Phase 0 is complete when:

- the component catalog correctly classifies the current public ETAB state,
- the structure and semantics of the project model are defined,
- the reference model validates against the JSON schema,
- naming, ID, and regeneration rules are documented as binding,
- the brush machine can be described without incorporating its process implementation.

All criteria are demonstrated in the [validation record](docs/Phase0_Validation.md).

## Phase 1 Acceptance

Phase 1 is complete when:

- identical inputs produce byte-identical PLC artifacts,
- duplicate stable command IDs and `enumValue` values are rejected,
- only the configured generator-owned area is modified,
- written TwinCAT XML artifacts are structurally valid,
- conflicts block the entire write operation and a write failure is rolled back.

All criteria are demonstrated in the [Phase 1 completion validation](docs/Phase1_Validation.md).

## Phase 2 Acceptance

Phase 2 is complete when the BrushMachine model can be edited visually, validated and previewed through the shared core, saved, closed, and reopened without data loss.

The editor acceptance flow, service round-trip tests, and build evidence are recorded in the [Phase 2 validation record](docs/Phase2_Validation.md).
