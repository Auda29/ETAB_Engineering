# ETAB Engineering – Plan for a Visual PLC Template Generator

## Document Status

- Status: implementation; Phase 0, Phase 1, Phase 2, and structural Phase 3A/3B/3C generation and project integration completed; Phase 4A collision analysis and Phase 4B external-ownership copy generation completed; XAE open, compile, and runtime acceptance remain open
- As of: 2026-08-13
- Working title: `ETAB Engineering`
- Target environment: TwinCAT 3 and `ET_AutomationBase`
- Reference project: `AutomationBase Beispiel`

## 1. Objective

`ETAB Engineering` is intended to become a visual engineering tool in which a machine is described using logical components, similar to an HMI or MTP editor.

The stored machine model will be used to generate a reproducible TwinCAT PLC template based on the `ET_AutomationBase` library. An MTP integration layer for services and procedures may optionally be generated at a later stage.

The editor describes the structure and public contracts of the machine. The actual machine, safety, motion, and process logic remains handwritten.

## 2. Guiding Principles

1. The visual machine model is the single source of truth for generated structures.
2. Identical inputs must produce byte-identical outputs.
3. Generated code and handwritten code are kept strictly separate.
4. Handwritten files must never be overwritten automatically.
5. The generator must be able to display every planned change before writing.
6. Stable object IDs must allow renaming without unnecessary changes to TwinCAT GUIDs.
7. Structural XML validation is not evidence of a successful TwinCAT compile.
8. Safety and machine behavior are not derived from a general-purpose diagram.
9. MTP is an optional integration layer and not a prerequisite for the ETAB MVP.

## 3. Product Boundary

### 3.1 Describable in the Visual Editor

- machine and unit hierarchy
- ETAB base types
- commands, stable model IDs, and fixed enum values
- request, status, and parameter data
- parent-child relationships between units
- logical dependencies and connections
- recipes and machine links
- public HMI/status structures
- optional exposure of a unit as an MTP service
- optional mapping of MTP procedures to ETAB commands

### 3.2 Not Generated Automatically

- safety logic
- collision and enable decisions
- concrete I/O addressing
- axis and hardware configuration
- machine-specific process sequences
- fault responses and safe-stop sequences
- real motion, timeout, and process parameters
- complete operator screens for a runtime HMI

The visual editor is therefore an engineering tool with HMI-like operation, but it is neither a runtime HMI nor a general-purpose safety or sequence generator.

## 4. Target Architecture

```text
Visual ETAB Editor
        ↕
Versioned Machine Model (*.etab.json)
        ↓
Generator Core
        ├─ Validation
        ├─ Change Preview
        ├─ TwinCAT PLC Template
        ├─ Generation Manifest
        └─ Optional MTP Adapter
                 ↓
       Handwritten Machine Logic
```

### 4.1 Components

#### Visual Editor

- component palette
- machine canvas
- unit hierarchy
- property inspector
- command editor
- data-structure editor
- connection editor
- validation display
- generation preview

#### Project Model

- plain, diff-friendly JSON
- versioned schema
- stable internal IDs
- separate layout and PLC data
- no TwinCAT XML details in the user interface

#### Generator Core

- usable independently of the graphical user interface
- callable through the CLI and editor
- deterministic TwinCAT XML generation
- safe management of `.plcproj` entries
- manifest and hash validation

#### TwinCAT Integration

- file-based initially
- Automation Interface optional later
- real XAE compile as a separate validation level

## 5. Project Model v0.1

The first project file is a plain JSON document, for example:

```text
BrushMachine.etab.json
```

### 5.1 Project-Wide Properties

- schema version
- project name
- PLC prefix, for example `BM`
- namespace
- requested ETAB version
- TwinCAT target version
- target project or output directory

### 5.2 Unit

Each unit has at least:

- stable internal ID
- display name
- PLC name
- ETAB base type
- parent unit
- activation options
- commands
- request fields
- status fields
- parameters
- optional child units
- optional MTP mapping

Planned ETAB component types for the MVP:

- `ApplicationUnit`
- `CommandUnit`
- `MachineLink`
- `RecipeManager`

Project-specific specializations such as `MotionUnit`, `ProcessUnit`, or `WorkpieceUnit` are initially named variants of an `ApplicationUnit` or `CommandUnit`.

### 5.3 Commands

A command contains at least:

- stable internal ID
- PLC name
- numeric enum value (`enumValue`)
- display name
- description
- permitted unit type
- optional MTP procedure reference

Stable command model IDs are globally unique. `enumValue` values must be unique within their node.

### 5.4 Relationships

Only clearly defined relationships are allowed in the first version:

- `contains`: a unit contains a child unit
- `commands`: a unit sends requests to another unit
- `observes`: a unit reads the status of another unit
- `usesRecipe`: a unit uses a RecipeManager
- `usesLink`: a unit uses a MachineLink

Safety or collision enables are not modeled as automatically executable relationships.

### 5.5 Layout Data

The position, size, and grouping of a component are stored separately. Changes to the canvas layout must not cause a PLC code diff.

## 6. Planned Generator Output

Example structure:

```text
Generated/
├─ DUTs/
│  ├─ E_BM_ProcessCommand.TcDUT
│  ├─ ST_BM_ProcessRequest.TcDUT
│  └─ ST_BM_ProcessStatus.TcDUT
├─ POUs/
│  ├─ FB_BM_ProcessUnitBase.TcPOU
│  └─ FB_BM_ProcessCommandRouter.TcPOU
├─ GVLs/
│  └─ GVL_BM_Units.TcGVL
└─ etab-generation-manifest.json

Application/
└─ FB_BM_ProcessUnit.TcPOU
```

### 6.1 TwinCAT Objects That Can Be Generated

- command enums
- request DUTs
- status DUTs
- parameter DUTs
- generated unit base function blocks
- command routers
- optional interfaces
- unit instances in a GVL
- optional PRG call structure
- folder and compile entries in the `.plcproj` file
- required ETAB library reference
- MTP adapter function blocks at a later stage

### 6.2 Regeneration Boundary

- `Generated/` is owned entirely by the generator.
- `Application/` is owned entirely by the PLC developer.
- User files may be created as an initial scaffold at most once.
- They are neither modified nor deleted afterward.
- The manifest contains the model ID, target path, TwinCAT GUID, and content hash of every generated file.
- Manual changes to generated files must be detected and reported before they are overwritten.

## 7. Editor Interaction Model

### 7.1 Left: Component Palette

- Application Unit
- Command Unit
- Machine Link
- Recipe Manager
- MTP Service and MTP Procedure at a later stage

### 7.2 Center: Machine Canvas

- place units
- display the hierarchy
- connect relationships
- select components
- form groups or machine areas

The MVP does not require a completely free-form HMI drawing tool. A clear node/tree editor is sufficient.

### 7.3 Right: Properties

- name and PLC identifier
- ETAB base type
- commands and IDs
- request and status fields
- parameters
- child units
- generation options
- MTP mapping at a later stage

### 7.4 Bottom: Generation

- validation results
- list of files to be generated
- new, modified, and deleted objects
- diff preview
- warnings about manual changes
- generation only after successful validation

## 8. Planned CLI

The generator core is also intended to be usable without the editor:

```text
etab validate BrushMachine.etab.json
etab preview  BrushMachine.etab.json
etab generate BrushMachine.etab.json
etab check    BrushMachine.etab.json
```

### Commands

- `validate`: validate schema, names, IDs, and relationships
- `preview`: display planned changes without writing
- `generate`: produce the confirmed output
- `check`: verify that the model and generated files are synchronized

## 9. Technology Recommendation

### Generator Core

- C#/.NET class library
- separate CLI application
- XML generation without text-replacement fragments
- automated unit and snapshot tests

### Editor

- local TypeScript web interface
- SVG-based node canvas
- generator calls through a local .NET service
- WPF/WebView2 desktop host with the service running in the same process
- self-contained portable Windows x64 release bundle and guided installer

### TwinCAT Integration

1. MVP: deterministic file generation
2. afterward: safe `.plcproj` integration
3. optional: TwinCAT Automation Interface for XAE integration

The graphical user interface must not contain separate, divergent generator logic. The editor and CLI use the same generator core.

## 10. Implementation Phases

### Phase 0 – Specification (completed 2026-08-07, architecture addendum 2026-08-10)

- [x] define the ETAB component catalog
- [x] design JSON schema v0.1
- [x] define naming and ID rules
- [x] establish a binding generated/user boundary
- [x] classify the current example units as a reference
- [x] clearly distinguish the command enum value `enumValue` from the runtime `nCommandID`
- [x] define the project-specific status contract without changing library DUTs
- [x] verify the inheritance and hook pattern for generated base FBs with a TwinCAT compile spike

Acceptance: the existing brush machine model can be described completely without including process code in the model.

Evidence: `docs/Phase0_Validation.md`, `examples/BrushMachine.reference.etab.json`, and `spikes/TwinCAT_BaseFb_Inheritance.md`.

### Phase 1 – Headless Generator Core (completed 2026-08-10)

- [x] load the project model (Phase 1A, 2026-08-10)
- [x] validate schema and semantics (Phase 1A, 2026-08-10)
- [x] derive stable TwinCAT GUIDs using UUID v5 and manage them in the manifest
- [x] generate command, request, and status DUTs as well as ApplicationUnit base FBs
- [x] write a deterministic manifest containing the semantic model hash and artifact hashes last
- [x] complete the CLI with `validate`, `preview`, `check`, and conflict-protected `generate`
- [x] implement snapshot, determinism, change-plan, write-boundary, and rollback tests

Evidence: `docs/Phase1A_Validation.md`, `docs/Phase1B_Validation.md`, `docs/Phase1C_Validation.md`, and `docs/Phase1_Validation.md`. `preview` and `check` remain read-only; only the explicit `generate` command writes to the configured generator-owned area. `ET_AutomationBase` is not modified.

Acceptance:

- identical input produces byte-identical output
- duplicate stable command IDs and duplicate `enumValue` values per node are rejected
- no file outside the generator-owned area is modified
- TwinCAT XML can be parsed structurally

### Phase 2 – Visual Editor MVP (completed 2026-08-10)

- [x] open and save a project
- [x] component palette
- [x] machine canvas
- [x] unit selection and property inspector
- [x] command editor
- [x] request/status field editor
- [x] relationships
- [x] live validation
- [x] generation preview

Acceptance: BrushMachine can be modeled visually, saved, closed, and reopened without data loss.

Evidence: `docs/Phase2_Validation.md`. The local .NET service and CLI share `ETAB.Engineering.Core`; editor validation and preview do not duplicate generator logic. Browser acceptance covered editing all Phase 2 contract types, invalid-to-valid live feedback, relationship creation, canvas movement, an 18-artifact read-only preview, and a successful save/reopen round-trip.

### Phase 3 – TwinCAT Project Integration

- [x] manage the ETAB library reference (Phase 3A, 2026-08-13)
- [x] manage `<Compile Include="…">` entries (Phase 3A, 2026-08-13)
- [x] create the TwinCAT folder structure (Phase 3A, 2026-08-13)
- [x] generate GVL instances (Phase 3B, 2026-08-13)
- [x] generate an optional PRG call structure (Phase 3B, 2026-08-13)
- [x] safeguard renaming and deletion of generated project entries (Phase 3A, 2026-08-13)
- [x] initially test integration only in a copy of the project (Phase 3A, 2026-08-13)
- [x] select the target root, preview, and explicitly confirm generation in the editor (Phase 3C, 2026-08-13)
- [x] define the TwinCAT task-assignment policy and inspect the reference task (2026-08-13)

Acceptance:

- the project opens in TwinCAT without structural errors
- repeated generation produces no unnecessary diff
- a real TwinCAT compile succeeds

Phase 3A evidence: `docs/Phase3A_Validation.md`. The opt-in CLI integration updates only ETAB-owned project entries, preserves compatible unmanaged entries, participates in the generation transaction, and was idempotently exercised against a temporary copy of `AutomationBase_Beispiel.plcproj`. XAE open and compile evidence remain outstanding.

Phase 3B evidence: `docs/Phase3B_Validation.md`. The generator now creates one qualified instance GVL for nodes with `instance = true`. A project-wide opt-in creates a PRG that calls only instances explicitly selected with `callInProgram`; an explicit `instanceType` binds project-specific FBs while safe ETAB/generated defaults remain available. The reference model proposes 15 artifacts, including the GVL. Current project-aware preview additionally detects seven pre-existing IEC-name conflicts in the golden sample. XAE open and compile evidence remain outstanding.

Phase 3C evidence: `docs/Phase3C_Validation.md`. The editor now exposes the exact target root, optional `.plcproj` integration, the complete shared-core plan, and a separate Generate action. Generation requires a saved model, a conflict-free current preview, an exact confirmation token, and a final user confirmation. No live reference project was written during automated validation; XAE open and compile evidence remain outstanding.

Task policy: ETAB Engineering v0.1 does not modify `.TcTTO` task objects. A generated PRG must be invoked either once from an existing cyclic program or assigned manually to exactly one task, never both. The BrushMachine reference keeps `programCallStructure = false`; its existing `PlcTask.TcTTO` calls only `MAIN`, which calls the handwritten application FB.

### Phase 4 – Golden Sample `AutomationBase Beispiel`

- [x] model `FB_BM_Machine`
- [x] model `FB_BM_MotionUnit`
- [x] model `FB_BM_WorkpieceUnit`
- [x] model `FB_BM_ProcessUnit`
- [x] compare the existing request, command, and status DUTs (Phase 4A, 2026-08-13)
- [x] define external ownership for the seven existing DUTs in a dedicated integration model (Phase 4B, 2026-08-13)
- [x] preview the integration model read-only against the real project (Phase 4B, 2026-08-13)
- [x] generate into a separate project copy and prove byte-identical regeneration (Phase 4B, 2026-08-13)
- [ ] open and compile the generated copy in XAE
- [ ] run the existing simulation/FAT regression sequence

MVP completion: the visual BrushMachine model produces a TwinCAT-compilable ETAB scaffold without overwriting handwritten code.

Phase 4A evidence: `docs/Phase4A_Reconciliation.md`. The three existing command enums and three existing request DUTs match the model semantically. The existing `ST_BM_MachineStatus` is a 42-field project aggregate and is intentionally incompatible with the lean generated six-field contract. Project-aware preview now blocks all seven duplicate IEC names before writes. The next decision is whether these seven objects remain externally owned in a golden-sample integration model or the matching command/request types are migrated into the generated ownership boundary; the existing machine status must remain external or be renamed.

Phase 4B evidence: `docs/Phase4B_CopyGeneration.md`. The approved external-ownership decision is represented by `examples/BrushMachine.integration.etab.json`. It produces eight owned artifacts without colliding with existing DUTs. The first copy generation created eight artifacts and both manifests; the second run was byte-identical and `check` reported synchronized. The original project remained unchanged. XAE open and compile are assigned to the user and are not claimed as completed evidence.

### Phase 5 – Optional MTP Extension

- [ ] expose a unit as an MTP service
- [ ] map procedures to ETAB commands
- [ ] map parameters and ReportValues
- [ ] map MTP states to ETAB sequences
- [ ] explicitly block or implement unsupported states
- [ ] keep adapters outside areas generated by TE8400
- [ ] test regeneration from both the ETAB and MTP sides

### Phase 6 – Product Readiness

- [ ] undo/redo
- [ ] copy/paste
- [ ] reusable unit templates
- [ ] schema migrations
- [ ] import of existing ETAB structures
- [ ] CI `check`
- [x] portable Windows x64 application (2026-08-10) and installer (2026-08-13)
- [ ] user documentation
- [ ] additional example projects

## 11. Validation Strategy

### Model Tests

- required fields
- naming rules
- unique IDs
- valid relationships
- no hierarchy cycles
- valid ETAB base types

### Generator Tests

- deterministic output
- stable GUIDs
- safe renaming
- safe deletion
- protection against changes to generated files
- no changes to user files

### TwinCAT Tests

1. XML structural validation
2. open the project in XAE
3. verify library resolution
4. real TwinCAT compile
5. optional simulation of the example project

Compile, simulation, and machine validation are separate forms of evidence and must not be treated as equivalent.

## 12. Main Risks

### Overwriting Handwritten Code

Mitigation: hard directory boundary, manifest, hash validation, and mandatory preview.

### Unstable TwinCAT GUIDs

Mitigation: persistent model IDs and GUID mapping in the manifest.

### Excessive Initial Scope

Mitigation: limit the MVP to unit hierarchy, commands, request/status data, and base scaffolds.

### Mixing Editor and Generator Responsibilities

Mitigation: one shared generator core for the CLI and user interface.

### Unclear State Mapping Between MTP and ETAB

Mitigation: add MTP only after the ETAB MVP is stable and map states explicitly.

### False Safety Claim

Mitigation: safety, collision handling, and safe machine responses remain outside automatic generation.

## 13. Implementation Decisions

Binding decisions for Phase 1:

- Working name and location: `ETAB Engineering` in the `ETAB_Engineering_v0.1.0.0` workspace subdirectory.
- Command enum literal in the model: `enumValue`; `nCommandID` remains exclusively the runtime ID of a request.
- Status DUTs are generated per project and embed existing library status DUTs; `ET_AutomationBase` is not changed for this purpose.
- Base function blocks follow the compiler-verified pattern `User-FB -> Generated-Base-FB -> ETAB.FB_ETAB_ApplicationUnit`, with `SUPER^()` at both levels and protected hooks.
- The JSON schema, naming and ID rules, and rename/delete behavior are defined by the Phase 0 contracts.

Binding decisions for Phase 2:

- The editor is a TypeScript/React local web application backed by a loopback ASP.NET service.
- The service transports the complete JSON model and calls the existing core for validation and generation preview.
- Project saves use UTF-8 without BOM, LF line endings, and an atomic temporary-file replacement.
- Validation remains live while editing; parseable invalid drafts may still be saved for later correction.
- MTP exposure metadata is preserved and editable, while procedure editing remains assigned to Phase 5.

Binding decisions for desktop packaging:

- The production React build is shipped in the portable bundle and served by the in-process loopback service.
- WPF hosts the editor through Microsoft WebView2 and permits navigation only to the service origin allocated for the current process.
- The `win-x64` package is self-contained for .NET; Microsoft Edge WebView2 Runtime remains a target-system prerequisite.
- `publish-win-x64.ps1` builds the verified portable ZIP, while `publish-installer-win-x64.ps1` is the complete local and CI release entry point.
- The installer uses Inno Setup, defaults to per-user installation, includes the Microsoft WebView2 Evergreen bootstrapper for missing-runtime systems, and must pass a silent install/application-smoke/uninstall test before publication.
- Tags matching `v*` create GitHub Releases containing the portable ZIP, installer, and both SHA-256 files through `.github/workflows/desktop-release.yml`.

Binding decisions for Phase 3A:

- TwinCAT project integration is an explicit CLI opt-in through `--integrate-project` and requires an explicitly selected `--root`.
- The configured `.plcproj` must currently be a direct child of that root; project roots and project files reached through reparse points are rejected.
- Project entries added by ETAB Engineering are tracked separately in `Generated/etab-project-integration-manifest.json`; compatible pre-existing entries remain unmanaged.
- Generated artifacts, both manifests, and the `.plcproj` update share one preflight, staging, backup, and rollback transaction.
- Project XML is changed through targeted element edits and parsed before and after modification so unrelated lines and existing mixed line endings remain unchanged.
- TwinCAT XAE open and compile validation remain separate acceptance evidence and are not inferred from structural XML validation.

Binding decisions for Phase 3B:

- Nodes with `instance = true` are collected in one qualified `GVL_<prefix>_Units` object in deterministic node order.
- `instanceType` is optional. If omitted, ApplicationUnits use their generated base FB when available and the remaining node kinds use the corresponding ETAB library FB.
- The optional `PRG_<prefix>_Generated` artifact is controlled by project setting `programCallStructure` and calls only nodes explicitly selected with `callInProgram`.
- PRG generation does not add the program to a TwinCAT task automatically. Task assignment or invocation from an existing program remains an explicit engineering decision.
- Project-specific FB interfaces are not inferred. Selecting a custom `instanceType` for PRG invocation is valid only when that FB can be called without mandatory `VAR_IN_OUT` arguments.
- ETAB Engineering v0.1 never edits `.TcTTO` task objects. A generated PRG has exactly one manually selected cyclic entry path: either an existing program call or one task assignment.

Binding decisions for Phase 3C:

- The editor requires a saved ETAB model before generation; the submitted document must still match the model on disk byte-semantically.
- The preview token binds the resolved target root, integration option, complete planned change set, content hashes, manifests, and proposed `.plcproj` update.
- Changing the model path, model content, target root, or project-integration option invalidates the preview and disables Generate.
- The service rebuilds and verifies the plan before execution; the core executor performs another target and hash preflight immediately before writing.
- A final editor confirmation names the exact target root. The `.plcproj` remains a separate, explicit opt-in.

Non-blocking decisions for later phases:

- Automation Interface only after file-based project integration,
- exact MTP state mapping in Phase 5.

## 14. First Implementation Slice (Completed)

The initially approved development slice comprised:

1. JSON schema v0.1
2. C# model classes
3. validation of units, stable command IDs, and `enumValue` values
4. deterministic generation of a command enum
5. deterministic generation of request and status DUTs
6. manifest with stable IDs and hashes
7. CLI `preview` and CLI `check`
8. tests based on a simplified `ProcessUnit`

Phase 1 implemented this slice in full and additionally added ApplicationUnit base FBs, the writing CLI command `generate`, transactional file operations, and rollback. The core is reproducible and accepted according to `docs/Phase1_Validation.md`. Phase 2 then added the visual editor and local service without changing the core generation boundary. Phase 3A added safe, opt-in TwinCAT project-file integration, Phase 3B added the instance GVL and optional selected PRG calls, and Phase 3C added target selection plus confirmed generation to the editor. Phase 4A added compiled IEC-name collision detection, and Phase 4B resolved the seven golden-sample ownership conflicts through a dedicated integration model and idempotent project-copy generation. XAE open and real compile evidence are still required before Phase 3 is accepted.
