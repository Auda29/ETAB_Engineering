# Phase 3C Validation – Confirmed Editor Generation

## Scope

Phase 3C adds the explicit write workflow to the visual editor. The bottom generation panel now exposes:

- the exact PLC target root,
- optional integration of the configured TwinCAT `.plcproj`,
- a target-aware read-only preview,
- a separate Generate action with final confirmation.

The editor and service do not implement a second generator. Preview and execution use the same `ETAB.Engineering.Core` planner, TwinCAT project integration, and transactional executor as the CLI.

## Confirmation and Safety Boundary

Generation from the editor requires all of the following:

1. the ETAB model is structurally and semantically valid,
2. the current editor model has been saved and still matches the model on disk,
3. the target root is explicit and passes the existing root and reparse-point checks,
4. the displayed plan contains no conflicts,
5. the service-provided confirmation token still matches the complete rebuilt plan,
6. the user confirms the exact target in the final dialog.

The confirmation token binds the resolved target root, `.plcproj` integration option, every planned operation, artifact and manifest content hashes, and the expected and proposed project-file hashes. Changing the model, project path, target root, or integration option clears the preview. A filesystem change after preview is rejected by token validation or by the executor's immediate preflight.

The `.plcproj` remains opt-in. With the option disabled, generation stays inside the configured generator-owned area. With it enabled, generated artifacts, both manifests, and the project-file update share one staging, backup, and rollback transaction.

## Automated Coverage

The service tests verify that:

- a confirmed preview writes exactly the planned generated files,
- the resulting project becomes synchronized,
- missing confirmation is rejected without writes,
- a stale or changed target is rejected without writes,
- an unsaved editor model is rejected without writes,
- optional project integration writes exactly the previewed `.plcproj` and integration manifest.

Current result: 54 core tests and 7 service tests passed. The complete Release build finished with 0 warnings and 0 errors, the TypeScript check and format verification passed, and the embedded desktop-service smoke test completed successfully with a 15-artifact read-only preview and lossless save/reopen flow. No Playwright test was run.

## Acceptance Boundary

The automated tests write only to isolated temporary directories. They do not generate into the live `AutomationBase Beispiel` workspace and do not interact with a running editor instance. This validates editor-to-service planning, confirmation, and transactional file execution; it does not prove TwinCAT XAE open, task assignment, a complete PLC compile, simulation, or machine behavior.
