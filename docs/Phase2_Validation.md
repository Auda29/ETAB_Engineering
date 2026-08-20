# Phase 2 Visual Editor Validation

## Status

- Phase: 2 – Visual Editor MVP
- Result: completed
- Validated: 2026-08-10
- Reference model: `examples/BrushMachine.reference.etab.json`

## Implemented Architecture

The editor is a TypeScript/React application in `src/ETAB.Engineering.Editor`. It communicates with the reusable loopback ASP.NET service in `src/ETAB.Engineering.Service`; `src/ETAB.Engineering.Service.Host` is the development executable, while the WPF desktop application hosts the same service in process.

The service is a facade over `ETAB.Engineering.Core` and exposes project session, new, open, save, validate, and preview operations. Validation uses `ProjectValidator`; preview uses `ArtifactPreviewGenerator` and `GenerationPlanBuilder`. The CLI and editor therefore share one model, validator, planner, and generator implementation.

The service transports the complete JSON document rather than a reduced editor DTO. This preserves schema-valid fields that are not presented by the current inspector, including later-phase MTP procedure mappings. Save operations enforce the `.etab.json` extension and write UTF-8 without BOM with LF line endings through a temporary-file replacement.

## Editor Scope

The implemented editor provides:

- an explicit TwinCAT-first startup without an implicitly opened example;
- a Core-validated minimal companion model with fresh stable IDs, derived automatically from the selected `.plcproj`;
- native Windows `.plcproj` and existing-model selection with read-only resolved paths and filenames;
- project save, dirty-state protection, `Ctrl+S`, and save validation feedback;
- a drag-and-drop palette for Application Unit, Command Unit, Recipe Manager, and Machine Link nodes, with keyboard placement as an accessibility fallback;
- a searchable hierarchy and a draggable machine canvas with directed SVG relationship paths;
- project and node property inspection;
- add, edit, delete, and reorder operations for commands and request/status fields;
- relationship creation, editing, and removal for all model relationship kinds;
- generation flags and type-specific node settings;
- debounced live schema and semantic validation through the service;
- a read-only generation plan, artifact list, manifest, and complete generated-content viewer.

## Automated Verification

The complete solution builds without warnings or errors. The current test suite contains 59 core tests and 8 service tests, all passing.

The service tests prove that:

1. the complete BrushMachine reference document opens through the service;
2. a full JSON document round-trips losslessly at the JSON data level;
3. files are saved as UTF-8 without BOM and with LF line endings;
4. parseable invalid drafts can be saved together with validation feedback;
5. the reference preview contains the expected artifacts and does not write them;
6. selecting an empty `.plcproj` creates and reopens its deterministic companion model;
7. direct PLC-root generation writes into `DUTs`, `POUs`, and `GVLs` and updates the `.plcproj` transactionally.

The editor passes TypeScript checking and a production Vite build.

## Browser Acceptance Flow

A real browser acceptance flow was executed against the running local service and editor:

1. The BrushMachine reference opened as a valid model with 7 nodes and 12 relationships.
2. A command, request field, and status field were added and remained live-valid.
3. A new Application Unit was added from the palette and renamed to `QA Unit`.
4. Changing its PLC name temporarily to the existing `Machine` name produced `NODE_NAME_DUPLICATE` immediately; restoring `Application8` returned the model to valid state.
5. A `contains` relationship from Brush Machine to QA Unit was created and rendered.
6. QA Unit was dragged on the canvas. Its stored layout became `x = 1032`, `y = 364`.
7. The shared core produced a read-only preview of 18 artifacts for the edited model.
8. The model was saved to a separate QA file and reopened without a dirty-state prompt.
9. The reopened file remained valid with 8 nodes, 13 relationships, 9 machine commands, 1 machine request field, 5 machine status fields, exactly one QA Unit relationship, and the persisted canvas layout.
10. A fresh browser session reported zero console errors and zero warnings.

The original BrushMachine reference file was not modified by this acceptance flow.

## File Workflow Follow-up – 2026-08-13

The first independent UI-validation pass identified that the editor always opened the same bundled BrushMachine path and required manual path editing. The initial follow-up introduced generic **New Project**, **Open**, and **Save As** actions through native dialogs. The later TwinCAT-first follow-up below supersedes that generic new/save-as workflow for the production desktop: a `.plcproj` selection now determines the companion model and all target paths without typed values.

The minimal template is produced by the .NET service and validated through the shared Core before it reaches the editor. Its automated test proves a valid single-machine model, fresh project and node IDs on every creation, ETAB `0.1.0.3`, five standard commands, and a conflict-free five-artifact preview. The service suite now contains 8 passing tests. TypeScript checking and the complete Release build pass with zero warnings and zero errors. Interactive dialog acceptance remains assigned to the user's UI-validation session; no Playwright test was used.

## TwinCAT-First File Workflow Follow-up – 2026-08-14

The production desktop workflow now begins with an empty PLC project created in TwinCAT. **Connect TwinCAT PLC Project** opens a native `.plcproj` picker. The service validates the selected file and creates or reopens a deterministic companion `<PLC name>.etab.json` in the same directory. For a newly created companion it derives the IEC project name, display name, prefix, namespace, linked PLC filename, project root, and direct-output layout automatically. Reconnecting preserves the existing model and its stable IDs.

The UI no longer accepts typed model paths, save filenames, PLC filenames, target roots, or generation-root values. The top bar and generation panel display the resolved values read-only; PLC project integration is always enabled for the connected workflow. The project inspector also exposes the selected PLC project and output layout as read-only information.

For these linked projects, `project.generation.generatedRoot = "."` is an explicit direct-layout mode. Node-owned artifacts use `Application/<area>/<node>`, shared objects use `ETAB/Shared`, and the optional generated PRG uses `ETAB/Runtime`. No additional `Generated` folder is created. Transaction and ownership safety remain manifest-based: only planned, manifest-listed ETAB artifacts and project entries are modified, while unrelated files in the same PLC root remain unmanaged.

Automated evidence now consists of all 59 Core tests and 10 service tests. The new end-to-end service test creates an empty `.plcproj`, connects it, previews the direct paths, performs the confirmed transaction, verifies the files in the TwinCAT folders, verifies the matching `.plcproj` include, and proves that no `Generated` directory was created. A second test reconnects the same PLC project and verifies that its project ID is unchanged. TypeScript checking also passes. No browser automation or Playwright run was performed.

## Relationship Editing Follow-up – 2026-08-14

Relationship editing is now available directly on the machine canvas. Each unit node exposes a **Connect** action. After selecting a source, the editor highlights only valid targets and dims invalid nodes. The endpoint pair then determines the available relationship types. Human-readable labels and descriptions are shown alongside the technical model names, direction arrows terminate at node boundaries, and a persistent legend explains the line colors.

Clicking a relationship line or label opens an editor for changing its type or optional line label, or deleting it. The inspector remains available as the detailed list and creation view and uses the same filtered choices. The editor prevents self-relations, duplicates, invalid source/target kinds, multiple `contains` parents, and `contains` cycles. The Core validator enforces the same semantic boundary for manually edited or external project files through `RELATION_SOURCE_KIND`, `RELATION_TARGET_KIND`, `RELATION_DUPLICATE`, and the existing hierarchy diagnostics.

Automated evidence includes TypeScript checking plus three new Core tests for invalid source kinds, invalid target kinds, and duplicate relationships. No browser automation or Playwright run was performed for this follow-up; interactive canvas acceptance remains part of the user's UI-validation session.

## Palette Drag-and-Drop Follow-up – 2026-08-14

Palette cards are now dragged onto the machine canvas instead of creating nodes on a mouse click. The canvas highlights as a valid drop target, shows a placement hint, and creates the component centered at the dropped position on the existing four-pixel layout grid. Dropped positions are clamped to the canvas bounds. Enter or Space on a focused palette card remains available for keyboard users and applies the existing automatic placement.

Verification for this follow-up consists of TypeScript checking, the production editor build, the complete Release solution build and automated .NET tests, plus the packaged application's non-interactive smoke test. No browser automation or Playwright run is part of this follow-up; interactive drag-and-drop acceptance remains assigned to the user's UI-validation session.

## Node Context Menu Follow-up – 2026-08-14

Right-clicking a canvas node now opens a contextual action menu. **Rename node** opens a compact form for the display name, PLC name, and symbol stem so visual labels, generated instance names, and generated DUT/FB names can be changed together without changing the stable node ID. **Create relationship** enters the existing filtered connection workflow and is disabled when no valid target exists. **Add command** creates the next stable default command, selects the node, and opens the Commands inspector tab; the action is disabled for node configurations without command-enum generation. The menu closes on outside interaction, canvas scrolling, window resize, focus loss, or Escape.

The implementation reuses the existing relation rules and command factory rather than introducing separate context-menu semantics. TypeScript checking, the production editor build, the complete Release solution build, and the packaged application's non-interactive smoke test cover the static and packaging boundary. Interactive right-click acceptance remains part of the user's UI-validation session; no browser automation or Playwright run is used.

## Machine Areas and Canvas Zoom Follow-up – 2026-08-14

The editor treats layout grouping as persistent machine areas. Area declarations carry a stable IEC-compatible name and a separate display name. They appear as folders in the project tree and as tabs above the canvas. Users can create, rename, and remove areas; removing one makes its nodes unassigned without deleting nodes or relationships. Palette drops inherit the active area, and the node context menu moves existing nodes between areas. The **Overview** tab shows compact area cards, node counts, and aggregated cross-area relations instead of rendering the complete project graph.

Relations remain global and may connect nodes assigned to different areas. During direct connection mode the source remains active while the user changes tabs to choose a valid target. An area view lists its cross-area relationships and links to the remote node and area. The Core rejects duplicate declared area names, undeclared references, invalid Windows folder names, duplicate area folders, and duplicate node folders within an area. Area folders and assignments participate in the semantic hash because they change generated paths. Canvas coordinates do not affect generated content.

## TwinCAT area folder mapping follow-up - 2026-08-20

The generator now mirrors the editor tree in the TwinCAT project. Every declared area and every node receives a `Folder` entry, including empty areas and nodes that only contribute an instance to the shared GVL. Unassigned nodes use `Application/Unassigned`. Shared objects remain outside any machine area under `ETAB/Shared`, while the optional generated PRG uses `ETAB/Runtime`.

Area renames, node display-name changes, and node moves produce manifest-backed rename operations. Managed artifacts keep their deterministic TwinCAT GUIDs. User-owned stubs are moved with their exact existing bytes and an actual-hash preflight check; an IEC FB-name change remains blocked for manual reconciliation. The complete automated result is 91 passing Core tests and 10 passing service tests. No browser automation or Playwright run was used.

Zoom controls and Ctrl+mouse-wheel scale only the detailed canvas world, its grid, nodes, and relationship paths from 20 to 160 percent. The Fit action automatically selects and centers a suitable scale when an area opens. The canvas dimensions grow with stored node positions, and users can pan by dragging empty canvas space, middle-dragging, or holding Space while dragging. Drag and drop coordinates and node movement are normalized against the active scale. Desktop WebView page zoom is disabled, and the development UI suppresses page-level wheel and keyboard zoom shortcuts. TypeScript checking and all 81 Core plus 10 service tests pass. Interactive area, cross-tab relationship, overview, panning, and zoom acceptance remains assigned to the user's UI-validation session; no browser automation or Playwright run is used.

## Dark and Light Theme Follow-up – 2026-08-14

The startup screen and editor header now expose a dark/light theme toggle. The selection is applied before paint, stored in local browser data, and restored on the next application start. Both themes cover the complete application shell, palette and tree, area tabs, canvas and relations, inspector forms, context menus, validation and generation panels, notices, and the startup workflow. Native form controls receive the matching color scheme.

TypeScript checking and the production editor build cover the theme-switch implementation. Visual acceptance of contrast and component coverage in both themes remains assigned to the user's UI-validation session; no browser automation or Playwright run is used.

## Acceptance Conclusion

The BrushMachine model can be opened, modeled visually, validated, previewed, saved, closed, and reopened without loss of the edited model data. Phase 2 acceptance is satisfied.

This is application, automated-test, and browser evidence. It is not evidence of a TwinCAT XAE open, PLC compile, simulation, or machine test. Those validation levels remain part of Phase 3 and later work.
