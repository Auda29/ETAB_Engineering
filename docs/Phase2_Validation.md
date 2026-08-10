# Phase 2 Visual Editor Validation

## Status

- Phase: 2 – Visual Editor MVP
- Result: completed
- Validated: 2026-08-10
- Reference model: `examples/BrushMachine.reference.etab.json`

## Implemented Architecture

The editor is a TypeScript/React application in `src/ETAB.Engineering.Editor`. It communicates with the loopback ASP.NET service in `src/ETAB.Engineering.Service`.

The service is a facade over `ETAB.Engineering.Core` and exposes project session, open, save, validate, and preview operations. Validation uses `ProjectValidator`; preview uses `ArtifactPreviewGenerator` and `GenerationPlanBuilder`. The CLI and editor therefore share one model, validator, planner, and generator implementation.

The service transports the complete JSON document rather than a reduced editor DTO. This preserves schema-valid fields that are not presented by the current inspector, including later-phase MTP procedure mappings. Save operations enforce the `.etab.json` extension and write UTF-8 without BOM with LF line endings through a temporary-file replacement.

## Editor Scope

The implemented editor provides:

- project open, save, dirty-state protection, `Ctrl+S`, and save validation feedback;
- a palette for Application Unit, Command Unit, Recipe Manager, and Machine Link nodes;
- a searchable hierarchy and a draggable machine canvas with SVG relationship paths;
- project and node property inspection;
- add, edit, delete, and reorder operations for commands and request/status fields;
- relationship creation and removal for all model relationship kinds;
- generation flags and type-specific node settings;
- debounced live schema and semantic validation through the service;
- a read-only generation plan, artifact list, manifest, and complete generated-content viewer.

## Automated Verification

The complete solution builds without warnings or errors. The test suite contains 35 core tests and 4 service tests, all passing.

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

## Acceptance Conclusion

The BrushMachine model can be opened, modeled visually, validated, previewed, saved, closed, and reopened without loss of the edited model data. Phase 2 acceptance is satisfied.

This is application, automated-test, and browser evidence. It is not evidence of a TwinCAT XAE open, PLC compile, simulation, or machine test. Those validation levels remain part of Phase 3 and later work.
