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

- explicit startup actions without an implicitly opened example;
- a Core-validated minimal New Project template with fresh stable IDs;
- native Windows Open, first-Save, and Save As dialogs in the desktop application, plus manual-path fallback for development browsers;
- project save, dirty-state protection, `Ctrl+S`, and save validation feedback;
- a palette for Application Unit, Command Unit, Recipe Manager, and Machine Link nodes;
- a searchable hierarchy and a draggable machine canvas with directed SVG relationship paths;
- project and node property inspection;
- add, edit, delete, and reorder operations for commands and request/status fields;
- relationship creation, editing, and removal for all model relationship kinds;
- generation flags and type-specific node settings;
- debounced live schema and semantic validation through the service;
- a read-only generation plan, artifact list, manifest, and complete generated-content viewer.

## Automated Verification

The complete solution builds without warnings or errors. The current test suite contains 57 core tests and 8 service tests, all passing.

The service tests prove that:

1. the complete BrushMachine reference document opens through the service;
2. a full JSON document round-trips losslessly at the JSON data level;
3. files are saved as UTF-8 without BOM and with LF line endings;
4. parseable invalid drafts can be saved together with validation feedback;
5. the reference preview contains 14 artifacts and does not write them.

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

The first independent UI-validation pass identified that the editor always opened the same bundled BrushMachine path, required manual path editing, and offered neither New Project nor Save As. The desktop workflow now starts without a loaded document and provides explicit **New Project**, **Open Project**, and optional **Open BrushMachine example** actions. The top bar provides **New**, **Open**, **Save**, and **Save As**. Desktop file selection is implemented through native Windows dialogs; no arbitrary path must be typed.

The minimal template is produced by the .NET service and validated through the shared Core before it reaches the editor. Its automated test proves a valid single-machine model, fresh project and node IDs on every creation, ETAB `0.1.0.3`, five standard commands, and a conflict-free five-artifact preview. The service suite now contains 8 passing tests. TypeScript checking and the complete Release build pass with zero warnings and zero errors. Interactive dialog acceptance remains assigned to the user's UI-validation session; no Playwright test was used.

## Relationship Editing Follow-up – 2026-08-14

Relationship editing is now available directly on the machine canvas. Each unit node exposes a **Connect** action. After selecting a source, the editor highlights only valid targets and dims invalid nodes. The endpoint pair then determines the available relationship types. Human-readable labels and descriptions are shown alongside the technical model names, direction arrows terminate at node boundaries, and a persistent legend explains the line colors.

Clicking a relationship line or label opens an editor for changing its type or optional line label, or deleting it. The inspector remains available as the detailed list and creation view and uses the same filtered choices. The editor prevents self-relations, duplicates, invalid source/target kinds, multiple `contains` parents, and `contains` cycles. The Core validator enforces the same semantic boundary for manually edited or external project files through `RELATION_SOURCE_KIND`, `RELATION_TARGET_KIND`, `RELATION_DUPLICATE`, and the existing hierarchy diagnostics.

Automated evidence includes TypeScript checking plus three new Core tests for invalid source kinds, invalid target kinds, and duplicate relationships. No browser automation or Playwright run was performed for this follow-up; interactive canvas acceptance remains part of the user's UI-validation session.

## Acceptance Conclusion

The BrushMachine model can be opened, modeled visually, validated, previewed, saved, closed, and reopened without loss of the edited model data. Phase 2 acceptance is satisfied.

This is application, automated-test, and browser evidence. It is not evidence of a TwinCAT XAE open, PLC compile, simulation, or machine test. Those validation levels remain part of Phase 3 and later work.
