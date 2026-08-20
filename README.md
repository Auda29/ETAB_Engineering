# ETAB Engineering v0.1.0.7

Visual engineering tool for describing a logical machine and subsequently generating a TwinCAT PLC template based on the `ET_AutomationBase` library.

## Current Status

Phase 0 – Specification: completed on 2026-08-07, architecture addendum validated on 2026-08-10. Phase 1 – Headless Generator Core and Phase 2 – Visual Editor MVP: completed on 2026-08-10. Phase 3A – safe TwinCAT project-file integration, Phase 3B – generated instances plus an optional PRG call structure, and Phase 3C – target-aware preview and confirmed generation in the editor: completed structurally on 2026-08-13. Phase 3D adds opt-in PLC relation wiring with explicit command and status adapters while preserving the safety boundary. Phase 3E adds opt-in generated runtime execution: ETAB creates the generated PRG, detects the linked TwinCAT task, previews its exact `.TcTTO` change, and manages one task call transactionally. Phase 4A blocks duplicate IEC object names, and Phase 4B provides an external-ownership integration model that was generated idempotently into a project copy. The generated copy was subsequently opened and built successfully in TwinCAT XAE by the user. Runtime and machine acceptance remain separate. A portable Windows x64 desktop bundle and a guided Windows installer are also available.

The model, rules, generation boundary, and reference classification have been defined and verified against the BrushMachine reference model. `enumValue`, the project-specific status contract, and the base-FB inheritance pattern have been conclusively defined. The base-FB spike compiles successfully in the BrushMachine project.

The headless core is implemented as a .NET solution. It loads `*.etab.json` files and validates them against JSON Schema Draft 2020-12 and the project-specific semantic rules. The CLI commands `validate`, `preview`, `check`, and `generate` are available. The generator renders command enums, node-specific request and status DUTs, lean ApplicationUnit base FBs, a qualified project instance GVL, and an opt-in `FB_<prefix>_Relations` adapter. Generated runtime execution binds every selected node request to its ETAB FB call, applies the modeled node options, and publishes the library status into the generated status DUT. Command enums are mapped explicitly through each command's `etabCommand` value. `contains`, `usesRecipe`, and `usesLink` dependencies determine a stable parent/dependency-before-consumer call order. A `commands` relation can additionally define explicit source-to-target command routes; an unconfigured line never invents process behavior. Optional ApplicationUnit user stubs are created once, derive from the managed base FB, and are never overwritten or deleted after user edits.

The TypeScript visual editor and its loopback .NET service are implemented. Its desktop workflow starts from an empty PLC project created in TwinCAT: the startup screen selects the `.plcproj` through a native Windows dialog, creates or reopens a deterministic companion `<PLC name>.etab.json`, and assigns the PLC directory, project filename, direct TwinCAT output layout, and `.plcproj` integration automatically. Project paths, filenames, and generation targets are display-only in the UI. The editor otherwise provides a drag-and-drop component palette, hierarchy, property and contract editors, direct relationship creation and editing on the draggable SVG canvas, live validation, a target-aware generation preview, and an explicitly confirmed write action. Palette components are placed at their drop position; Enter or Space provides keyboard placement with automatic positioning. Persistent machine areas appear as canvas tabs and project-tree folders; nodes can be moved between them, and global relationships may cross area boundaries. The **Overview** tab summarizes each area as a card and aggregates cross-area relations instead of drawing the complete project graph. Opening an area shows its editable nodes and full relation graph, automatically fits them into view, and retains cross-area navigation. Right-clicking a node opens a contextual action menu for renaming its display, PLC, and generated-symbol names, starting a valid relationship, adding a generated command, or moving the node to another area. The canvas can be panned by dragging its background, by middle-dragging, or with Space plus drag. Canvas controls, the Fit action, and Ctrl+mouse-wheel zoom only the node workspace from 20 to 160 percent; the canvas grows with the stored layout and WebView page zoom remains disabled. A header toggle switches the complete editor between dark and light themes and persists the preference locally, including on the startup screen. Relationship mode highlights only valid targets, offers only valid types for the selected endpoints, shows direction arrows and a legend, prevents duplicate or cyclic hierarchy links before saving, and routes multiple relationships between the same node pair on stable, separate lanes with individual labels. The desktop executable, window, shortcuts, installer, and web favicon share the ET application icon, while the interface subtly identifies the full EngineeringToolbox AutomationBase name. Both the editor service and CLI call the same `ETAB.Engineering.Core`; the UI contains no second generator implementation.

`ETAB Engineering.exe` hosts the production React build in WebView2 and starts the existing ASP.NET service inside the same process on a random loopback port. The self-contained package requires neither the .NET SDK nor Node.js on the target computer.

The project integration manages generated `Compile` and `Folder` entries, the ETAB placeholder reference, the required direct EngineeringToolbox (`ET`) placeholder reference, and—when runtime execution is enabled—one generated `PouCall` in a detected `.TcTTO` task, without taking ownership of compatible existing entries. With multiple tasks ETAB selects the unique task that calls `MAIN`; ambiguous projects are blocked instead of guessed. Existing task calls remain unchanged, and disabling the option removes only the manifest-owned generated call. Before planning additions the integration scans the already compiled `.TcDUT`, `.TcPOU`, and `.TcGVL` objects inside the selected root and blocks duplicate case-insensitive IEC names. The desktop editor makes integration mandatory for a linked PLC project and emits directly into its `DUTs`, `POUs`, and `GVLs` hierarchy; only manifest-listed ETAB files and task call are managed even though handwritten files share the PLC root. The complete generated BrushMachine integration copy subsequently built successfully in TwinCAT XAE; runtime simulation and machine behavior remain separate acceptance levels.

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

`--integrate-project` requires `--root` and is deliberately opt-in. The configured `.plcproj` must currently be directly inside that root. ETAB Engineering records only entries it adds in `etab-project-integration-manifest.json` below the configured output root; compatible pre-existing entries remain unmanaged and untouched. If `runtimeExecution` is enabled, the preview also shows the complete proposed task XML before the managed `PRG_<prefix>_Generated` call is written.

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

Open `http://127.0.0.1:5173/` for frontend development. The editor talks only to the loopback service at `http://127.0.0.1:5079/`; native project selection is intentionally available only in `ETAB Engineering.exe`. The BrushMachine reference remains an explicit example action, not the default document.

For a new model, first create an empty PLC project in TwinCAT. On the ETAB startup screen select **Connect TwinCAT PLC Project** and choose its `.plcproj`. If the file is `PLC.plcproj`, ETAB creates `PLC.etab.json` beside it, derives a valid IEC project name and prefix, links `PLC.plcproj`, and stores the direct-output setting automatically. Selecting the same PLC project later reopens the existing companion model without replacing its stable IDs. **Open Existing ETAB Model** remains available for established models.

To generate from the editor, save the ETAB model, open **Generation preview**, and select **Refresh preview**. The target and linked `.plcproj` are read-only and already follow the startup selection. In project properties, **Enable generated runtime execution** opts into cyclic execution and selects ApplicationUnit and CommandUnit instances by default; Recipe Manager and Machine Link targets referenced through `usesRecipe` or `usesLink` are included automatically. Individual nodes can still be included or excluded with **Run cyclically in generated runtime**. A `commands` relationship exposes an **Automatic command routing** editor in the node's Relations tab. Each configured route maps one source command to one target command and forwards the execute signal and command ID; ambiguous multiple automatic drivers for the same target are rejected. **Create user stubs** emits editable ApplicationUnit derivatives only when no explicit instance type is configured. Inspect the complete conflict-protected plan, including the proposed `.plcproj` and runtime-task XML, and then select **Generate**. Generated DUTs, POUs, and GVLs are written to the corresponding TwinCAT directories, added to the `.plcproj`, and—when enabled—assigned once to the detected TwinCAT task in the same transaction. The button remains disabled for unsaved models, invalid models, conflicts, or stale previews; a final confirmation displays the exact target before any write.

Use `examples/BrushMachine.reference.etab.json` to exercise the complete 16-artifact generator output, including `FB_BM_Relations`. For integration with the existing `AutomationBase Beispiel`, open `examples/BrushMachine.integration.etab.json`; it keeps the three existing command enums, three request DUTs, and aggregate machine-status DUT externally owned and proposes nine non-conflicting generated artifacts.

### Windows Desktop Release

Create the complete Windows x64 release from the repository root:

```powershell
.\publish-installer-win-x64.ps1 -Version 0.1.0.7
```

Inno Setup 7 must be installed on the build computer. The script first builds and verifies the portable application, downloads Microsoft's signed WebView2 Evergreen bootstrapper, verifies its Authenticode signature, compiles the installer, and performs an isolated silent install, application smoke test, and uninstall. It creates:

```text
artifacts/ETAB-Engineering-v0.1.0.7-win-x64.zip
artifacts/ETAB-Engineering-v0.1.0.7-win-x64.zip.sha256
artifacts/ETAB-Engineering-v0.1.0.7-win-x64-setup.exe
artifacts/ETAB-Engineering-v0.1.0.7-win-x64-setup.exe.sha256
```

For a normal installation, start the `setup.exe`. It installs for the current user without elevation by default, creates a Start menu entry, optionally creates a desktop shortcut, and registers a complete uninstaller. If WebView2 Runtime is missing, Setup installs it through the included Microsoft Evergreen bootstrapper; that one-time case requires an internet connection.

As a portable alternative, extract the complete ZIP and start `ETAB Engineering.exe`. WebView2 Runtime must already be present when using the ZIP directly. Neither distribution requires the .NET SDK, Node.js, a terminal, or a separately started service on the target computer.

After its one-time signing setup, the GitHub Actions workflow `Desktop release` creates signed releases through Microsoft Artifact Signing. Until then, explicit `vX.Y.Z.W-preview.N` tags may publish unsigned prereleases. A stable unsigned release additionally requires the repository environment variable `ALLOW_UNSIGNED_STABLE_RELEASES=true`; partial or invalid signing configuration still fails closed. Windows SmartScreen can show an unknown-publisher warning for unsigned assets. The workflow still builds, installs, smoke-tests, uninstalls, and verifies both checksums. A manual workflow run performs the same package validation without creating a Release. Previously published assets are not changed retroactively. The required Azure and GitHub environment configuration is documented in [Windows Desktop Release](docs/Desktop_Release.md#artifact-signing-setup).

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
- [Phase 3D Relation Wiring Validation](docs/Phase3D_Relation_Wiring.md)
- [Phase 3E Runtime Execution Validation](docs/Phase3E_Runtime_Execution.md)
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
