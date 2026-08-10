# TwinCAT Spike: Generated Base FB and User FB

## Objective

The spike verifies the planned ownership pattern for unit function blocks:

```text
FB_ETABENG_UserUnit
  -> SUPER^()
FB_ETABENG_GeneratedUnitBase
  -> SUPER^()
ETAB.FB_ETAB_ApplicationUnit
```

The generated base FB belongs to `Generated/`. The derived user FB belongs to `Application/` and is not modified during regeneration.

## Compile Host

The three spike POUs are included in the existing `AutomationBase_Beispiel.plcproj` project under `POUs/Spikes/ETABEngineering/`:

- `FB_ETABENG_GeneratedUnitBase`: generator-managed intermediate layer with `SUPER^()` and the protected `OnExecuteOperation` hook.
- `FB_ETABENG_UserUnit`: user-managed derivative that also calls `SUPER^()` and overrides the hook.
- `FB_ETABENG_BaseFbInheritanceSpike`: non-instantiated test driver with counters for the call chain and hook dispatch.

None of the spike POUs is assigned to a task or instantiated by `MAIN`. The spike therefore does not alter the runtime behavior of the BrushMachine example.

## Acceptance Levels

1. XML structural validation of all three `.TcPOU` files and the `.plcproj` file.
2. TwinCAT compile of the existing project.
3. Optional later online/simulation test by deliberately instantiating the test driver.

A successful compile demonstrates valid inheritance, inherited inputs and outputs, the two-level `SUPER^()` chain, and a permitted hook override. It does not yet prove the runtime values of the counters. Those values are demonstrated only by the optional online/simulation test.

## Result

Executed on 2026-08-10 with the locally installed Beckhoff TwinCAT XAE Shell via `TcXaeShell.DTE.15.0`:

```text
Solution:      AutomationBase_Beispiel.sln
Configuration: Release | TwinCAT RT (x64)
LastBuildInfo: 0
Result:        COMPILE_SUCCESS
```

The two-level `SUPER^()` chain, inherited inputs and outputs, and protected hook override are therefore confirmed at compile time. The test driver was not assigned to a task; hook dispatch and counter values were consequently not executed online or in a simulation.
