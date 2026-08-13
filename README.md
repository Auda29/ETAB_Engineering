# ETAB Engineering v0.1.0.1

Visual engineering tool for describing a logical machine and subsequently generating a TwinCAT PLC template based on the `ET_AutomationBase` library.

## Current Status

Phase 0 – Specification: completed on 2026-08-07, architecture addendum validated on 2026-08-10. Phase 1 – Headless Generator Core and Phase 2 – Visual Editor MVP: completed on 2026-08-10. Phase 3A – safe TwinCAT project-file integration, Phase 3B – generated instances plus an optional PRG call structure, and Phase 3C – target-aware preview and confirmed generation in the editor: completed structurally on 2026-08-13. Phase 4A blocks duplicate IEC object names, and Phase 4B provides an external-ownership integration model that was generated idempotently into a project copy. XAE open and compile remain intentionally outstanding. A portable Windows x64 desktop bundle and a guided Windows installer are also available.

The model, rules, generation boundary, and reference classification have been defined and verified against the BrushMachine reference model. `enumValue`, the project-specific status contract, and the base-FB inheritance pattern have been conclusively defined. The base-FB spike compiles successfully in the BrushMachine project.

The headless core is implemented as a .NET solution. It loads `*.etab.json` files and validates them against JSON Schema Draft 2020-12 and the project-specific semantic rules. The CLI commands `validate`, `preview`, `check`, and `generate` are available. The generator renders command enums, request and status DUTs, lean ApplicationUnit base FBs, and a qualified project instance GVL. An optional generated PRG can call explicitly selected instances. The core manages stable TwinCAT GUIDs and content hashes in the manifest and applies conflict-free changes transactionally within the configured ownership boundary.

The TypeScript visual editor and its loopback .NET service are implemented. The editor opens and saves complete `*.etab.json` documents, provides a component palette, hierarchy, property and contract editors, relationships, a draggable SVG canvas, live validation, a target-aware generation preview, and an explicitly confirmed write action. Both the editor service and CLI call the same `ETAB.Engineering.Core`; the UI contains no second generator implementation.

`ETAB Engineering.exe` hosts the production React build in WebView2 and starts the existing ASP.NET service inside the same process on a random loopback port. The self-contained package requires neither the .NET SDK nor Node.js on the target computer.

The opt-in project integration manages generated `Compile` and `Folder` entries plus the ETAB placeholder reference without taking ownership of compatible existing entries. Before planning additions it scans the already compiled `.TcDUT`, `.TcPOU`, and `.TcGVL` objects inside the selected root and blocks duplicate case-insensitive IEC names. The editor exposes instance type, PRG-call selection, the project-wide PRG option, target-root selection, optional `.plcproj` integration, preview, and confirmed generation. A real compile of the complete generated project remains part of Phase 3; the underlying base-FB inheritance and hook pattern was already compiled successfully in TwinCAT during Phase 0.

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

`generate` is the explicit write command. It aborts on conflicts before performing any write. Without `--integrate-project`, it does not modify files outside the generator-owned area configured in the model.

To include the configured TwinCAT `.plcproj` in the same preview/check/generate transaction, select the actual PLC project directory explicitly:

```powershell
dotnet run --project .\src\ETAB.Engineering.Cli\ETAB.Engineering.Cli.csproj -- preview .\examples\BrushMachine.reference.etab.json --root "C:\Path\To\PLC" --integrate-project
dotnet run --project .\src\ETAB.Engineering.Cli\ETAB.Engineering.Cli.csproj -- generate .\examples\BrushMachine.reference.etab.json --root "C:\Path\To\PLC" --integrate-project
dotnet run --project .\src\ETAB.Engineering.Cli\ETAB.Engineering.Cli.csproj -- check .\examples\BrushMachine.reference.etab.json --root "C:\Path\To\PLC" --integrate-project
```

`--integrate-project` requires `--root` and is deliberately opt-in. The configured `.plcproj` must currently be directly inside that root. ETAB Engineering records only entries it adds in `Generated/etab-project-integration-manifest.json`; compatible pre-existing entries remain unmanaged and untouched.

### Visual Editor

Start the local service in the first terminal:

```powershell
dotnet run --project .\src\ETAB.Engineering.Service.Host\ETAB.Engineering.Service.Host.csproj
```

Install and start the editor in a second terminal:

```powershell
npm.cmd --prefix .\src\ETAB.Engineering.Editor install
npm.cmd --prefix .\src\ETAB.Engineering.Editor run dev
```

Open `http://127.0.0.1:5173/`. The editor initially loads the BrushMachine reference model and talks only to the loopback service at `http://127.0.0.1:5079/`.

To generate from the editor, save the ETAB model first, open **Generation preview**, enter the PLC project directory under **Target**, optionally enable **.plcproj**, and select **Refresh preview**. Inspect the complete conflict-protected plan and then select **Generate**. The button remains disabled for unsaved models, invalid models, conflicts, or stale previews; a final confirmation displays the exact target before any write.

Use `examples/BrushMachine.reference.etab.json` to exercise the complete 15-artifact generator output. For integration with the existing `AutomationBase Beispiel`, open `examples/BrushMachine.integration.etab.json`; it keeps the three existing command enums, three request DUTs, and aggregate machine-status DUT externally owned and proposes eight non-conflicting generated artifacts.

### Windows Desktop Release

Create the complete Windows x64 release from the repository root:

```powershell
.\publish-installer-win-x64.ps1 -Version 0.1.0.1
```

Inno Setup 7 must be installed on the build computer. The script first builds and verifies the portable application, downloads Microsoft's signed WebView2 Evergreen bootstrapper, verifies its Authenticode signature, compiles the installer, and performs an isolated silent install, application smoke test, and uninstall. It creates:

```text
artifacts/ETAB-Engineering-v0.1.0.1-win-x64.zip
artifacts/ETAB-Engineering-v0.1.0.1-win-x64.zip.sha256
artifacts/ETAB-Engineering-v0.1.0.1-win-x64-setup.exe
artifacts/ETAB-Engineering-v0.1.0.1-win-x64-setup.exe.sha256
```

For a normal installation, start the `setup.exe`. It installs for the current user without elevation by default, creates a Start menu entry, optionally creates a desktop shortcut, and registers a complete uninstaller. If WebView2 Runtime is missing, Setup installs it through the included Microsoft Evergreen bootstrapper; that one-time case requires an internet connection.

As a portable alternative, extract the complete ZIP and start `ETAB Engineering.exe`. WebView2 Runtime must already be present when using the ZIP directly. Neither distribution requires the .NET SDK, Node.js, a terminal, or a separately started service on the target computer.

The GitHub Actions workflow `Desktop release` runs the complete installer script. A `v*` tag attaches the ZIP, Setup EXE, and both checksums directly to a GitHub Release. A manual workflow run builds and verifies all four files without creating a Release. The Setup EXE is not currently code-signed, so Windows may display an unknown-publisher warning until release signing is configured.

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
- [Phase 3A TwinCAT Project Integration Validation](docs/Phase3A_Validation.md)
- [Phase 3B Instance and PRG Validation](docs/Phase3B_Validation.md)
- [Phase 3C Editor Generation Validation](docs/Phase3C_Validation.md)
- [Phase 4A Golden-Sample Reconciliation](docs/Phase4A_Reconciliation.md)
- [Phase 4B Project-Copy Generation](docs/Phase4B_CopyGeneration.md)
- [Windows Desktop Release](docs/Desktop_Release.md)
- [TwinCAT Base-FB Inheritance Spike](spikes/TwinCAT_BaseFb_Inheritance.md)
- [JSON Schema v0.1](schemas/etab-project.schema.json)
- [BrushMachine Reference Model](examples/BrushMachine.reference.etab.json)
- [BrushMachine Integration Model](examples/BrushMachine.integration.etab.json)

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
