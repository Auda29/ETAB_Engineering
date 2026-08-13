# Phase 4B – External Ownership and Project-Copy Generation

## Integration Model

`examples/BrushMachine.integration.etab.json` represents the existing BrushMachine project boundary. It keeps these seven PLC objects externally owned:

- `E_BM_MotionCommand`, `E_BM_WorkpieceCommand`, `E_BM_ProcessCommand`,
- `ST_BM_MotionRequest`, `ST_BM_WorkpieceRequest`, `ST_BM_ProcessRequest`,
- `ST_BM_MachineStatus`.

The integration model remains the same logical BrushMachine project but disables generation for these artifacts. It produces eight ETAB-owned artifacts:

- four ApplicationUnit base FBs,
- `ST_BM_MotionStatus`, `ST_BM_WorkpieceStatus`, and `ST_BM_ProcessStatus`,
- `GVL_BM_Units`.

The full `examples/BrushMachine.reference.etab.json` remains unchanged as the 15-artifact generator reference and desktop smoke fixture.

## Real-Project Read-Only Preview

On 2026-08-13, the integration model was previewed with `.plcproj` integration against the real `AutomationBase_Beispiel` PLC root. The result was conflict-free and proposed eight compile entries plus five generated folders. No file was written.

The original project file SHA-256 before and after all copy work was:

```text
412c42eacc60e766ff7916e6b324ba15b7705a185d9aa673ff6a88b816b5db5d
```

The original PLC root still had no `Generated/` directory afterward.

## Generated XAE Handoff Copy

A copy excluding the disposable `.vs` directory was created at:

```text
C:\Users\NiklasW\Desktop\PLC\EngineeringToolbox\ET_DEV\AutomationBase Beispiel\ETAB_Generated_Test_20260813
```

The solution for the manual XAE step is:

```text
C:\Users\NiklasW\Desktop\PLC\EngineeringToolbox\ET_DEV\AutomationBase Beispiel\ETAB_Generated_Test_20260813\AutomationBase_Beispiel.sln
```

First generation result:

```text
create=8 update=0 rename=0 delete=0
```

The transaction added eight generated `Compile` entries, five generated `Folder` entries, the eight artifacts, and two manifests. No staging or backup transaction directory remained.

The second generation reported every artifact, both manifests, and the `.plcproj` as `unchanged`:

```text
create=0 update=0 rename=0 delete=0
```

A complete before/after hash comparison of the copied PLC root was byte-identical for the second run, and CLI `check --integrate-project` reported `SYNCHRONIZED`.

## Validation Boundary

Automated result: 54 core tests and 7 service tests passed, the Release build completed with 0 warnings and 0 errors, TypeScript and format checks passed, and the embedded desktop smoke test passed without Playwright.

The copy has not been opened or compiled in TwinCAT XAE. That manual validation step belongs to the user. No PLC simulation or machine test is claimed.
