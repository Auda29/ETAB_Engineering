# AutomationBase-Referenzklassifikation v0.1

## 1. Zweck und Grenze

Das Projekt `AutomationBase Beispiel` dient als Golden Sample für das Modell v0.1. Diese Klassifikation beschreibt Struktur, Verantwortung und öffentliche Verträge. Sie übernimmt keine Prozessimplementierung in den Generator.

Prüfstand: statische Quellprüfung im Workspace. Kein TwinCAT-Compile, keine Simulation und keine Maschinenvalidierung wurden für dieses Dokument ausgeführt.

## 2. Logische Struktur

```text
FB_BM_Application                         Composition Root
├─ FB_BM_Machine                         Master Application Unit
│  ├─ FB_BM_MotionUnit                   Subunit / Achskommandos
│  ├─ FB_BM_WorkpieceUnit                Subunit / Werkstückhandling
│  ├─ FB_BM_ProcessUnit                  Subunit / Bürsten und Absaugung
│  └─ FB_BM_ProcessCycle                 Ablaufkoordinator
├─ FB_BM_RecipeService                   Recipe-Adapter
└─ FB_BM_CellInterface                   Machine-Link-Adapter
```

Im Quellcode werden Motion-, Workpiece- und ProcessUnit über `rUnit.ipMasterUnit` an `FB_BM_Machine` gebunden. `FB_BM_ProcessCycle` erzeugt typisierte Requests für die drei fachlichen Units.

## 3. Node-Klassifikation

### `FB_BM_Application`

- Klassifikation: Composition Root.
- ETAB-Kind: keiner.
- Aufgabe: Aufrufreihenfolge, Providerwahl, Request-Arbitration, Statusaggregation und Ausgabeübergabe.
- Generatorgrenze: kein automatisch generierter Ablaufkörper.
- Modellbehandlung: Projektwurzel und Quelle des visuellen Gesamtlayouts.

### `FB_BM_Machine`

- Klassifikation: Master Application Unit.
- ETAB-Kind: `applicationUnit`.
- Rolle: `machine`.
- Basis: `EXTENDS ETAB.FB_ETAB_ApplicationUnit`.
- enthält die drei fachlichen Subunits und den Ablaufkoordinator.
- projektspezifischer Status: Ready-for-cycle, Recovery-Anforderung, letzter Mode und State.

### `FB_BM_MotionUnit`

- Klassifikation: Typed Application Unit.
- ETAB-Kind: `applicationUnit`.
- Rolle: `motion`.
- exklusive Verantwortung: sechs Achskommandos.
- verwendet intern `ETAB.FB_ETAB_CommandUnit`.
- alle fachlichen Kommandos außer `NoAction` werden auf `E_ETAB_UnitCommand.User` abgebildet.

Request-Payload:

- `stJobData : ST_BM_JobData`

Status-Payload:

- `bAllHomed : BOOL`
- `bAllSafe : BOOL`
- `bAllStandstill : BOOL`
- `bSynchronized : BOOL`
- `fMeasuredLength : LREAL`

### `FB_BM_WorkpieceUnit`

- Klassifikation: Typed Application Unit.
- ETAB-Kind: `applicationUnit`.
- Rolle: `workpiece`.
- exklusive Verantwortung: Tor, Vakuum, Ausblasen und Laser.
- verwendet intern `ETAB.FB_ETAB_CommandUnit`.
- alle fachlichen Kommandos außer `NoAction` werden auf `E_ETAB_UnitCommand.User` abgebildet.

Request-Payload:

- `aVacuumZone : ARRAY[1..13] OF BOOL`
- `fVacuumMinimum : LREAL`
- `tVacuumTimeout : TIME`
- `tBlowTime : TIME`

Status-Payload:

- `bDoorSafe : BOOL`
- `bWorkpieceClamped : BOOL`
- `bVacuumReleased : BOOL`
- `bBlowComplete : BOOL`
- `fMeasuredLength : LREAL`

### `FB_BM_ProcessUnit`

- Klassifikation: Typed Application Unit.
- ETAB-Kind: `applicationUnit`.
- Rolle: `process`.
- exklusive Verantwortung: Bürsten und Absaugung.
- verwendet intern `ETAB.FB_ETAB_CommandUnit`.
- alle fachlichen Kommandos außer `NoAction` werden auf `E_ETAB_UnitCommand.User` abgebildet.

Request-Payload:

- `aBrushSpeed : ARRAY[1..3] OF LREAL`
- `tBrushTimeout : TIME`
- `tExhaustTimeout : TIME`
- `tExhaustRunOn : TIME`

Status-Payload:

- `bExhaustAvailable : BOOL`
- `bSeamBrushesAtSpeed : BOOL`
- `bEndBrushAtSpeed : BOOL`
- `bAllBrushesStopped : BOOL`

### `FB_BM_ProcessCycle`

- Klassifikation: Ablaufkoordinator.
- ETAB-Kind: `commandUnit`.
- Rolle: `orchestrator`.
- verwendet `ETAB.FB_ETAB_CommandUnit`.
- erzeugt Motion-, Workpiece- und ProcessRequests.
- beobachtet die Command-Status der drei Units.
- verwendet Rezept-, Job- und Interlockdaten.
- besitzt kontrollierte Stop-, Abort- und Recovery-Pfade.
- die Sequenzschritte bleiben vollständig handgeschrieben.

### `FB_BM_CommandBroker`

- Klassifikation: Projektmuster für Bedienkommando-Arbitration.
- priorisiert Abort, Stop, Reset, Recover, Start, Home und Clear.
- wird in v0.1 nicht als generischer ETAB-Node modelliert.
- kann später als wiederverwendbares Editor-/Generator-Pattern bewertet werden.

### Rezept und Machine Link

- `FB_BM_RecipeService` wird als projektspezifischer Adapter um einen `recipeManager` betrachtet.
- `FB_BM_CellInterface` wird als projektspezifischer Adapter um einen `machineLink` betrachtet.
- Projekt-Datentypen, Hardwarebezug und Fachvalidierung bleiben Handcode.

## 4. Beziehungen des Referenzmodells

| Quelle | Beziehung | Ziel |
|---|---|---|
| Machine | `contains` | MotionUnit |
| Machine | `contains` | WorkpieceUnit |
| Machine | `contains` | ProcessUnit |
| Machine | `contains` | ProcessCycle |
| ProcessCycle | `commands` | MotionUnit |
| ProcessCycle | `commands` | WorkpieceUnit |
| ProcessCycle | `commands` | ProcessUnit |
| ProcessCycle | `observes` | MotionUnit |
| ProcessCycle | `observes` | WorkpieceUnit |
| ProcessCycle | `observes` | ProcessUnit |
| ProcessCycle | `usesRecipe` | RecipeManager |
| Machine | `usesLink` | CellLink |

## 5. Generierbarer Anteil

- Command-Enums der fachlichen Units,
- Request-DUTs einschließlich implizitem ETAB-Request-Kopf,
- projektspezifische Status-DUTs,
- Basisgerüste für ApplicationUnit und CommandUnit,
- Instanz- und Beziehungsgerüste,
- spätere Statusaggregation.

## 6. Handgeschriebener Anteil

- `FB_BM_Application`-Aufruf- und Arbitrationlogik,
- komplette Sequenzlogik der Units,
- `FB_BM_ProcessCycle`-Ablauf,
- Safety- und Interlockauswertung,
- Output-Arbitration,
- IO-/Hardwareprovider,
- Simulation,
- Recovery- und sichere Stoppdetails,
- fachliche Rezept- und Jobvalidierung.

## 7. Quellpfade

- `../../AutomationBase Beispiel/AutomationBase_Beispiel/AutomationBase_Beispiel/AutomationBase_Beispiel/POUs/Application/FB_BM_Application.TcPOU`
- `../../AutomationBase Beispiel/AutomationBase_Beispiel/AutomationBase_Beispiel/AutomationBase_Beispiel/POUs/Units/FB_BM_Machine.TcPOU`
- `../../AutomationBase Beispiel/AutomationBase_Beispiel/AutomationBase_Beispiel/AutomationBase_Beispiel/POUs/Units/FB_BM_MotionUnit.TcPOU`
- `../../AutomationBase Beispiel/AutomationBase_Beispiel/AutomationBase_Beispiel/AutomationBase_Beispiel/POUs/Units/FB_BM_WorkpieceUnit.TcPOU`
- `../../AutomationBase Beispiel/AutomationBase_Beispiel/AutomationBase_Beispiel/AutomationBase_Beispiel/POUs/Units/FB_BM_ProcessUnit.TcPOU`
- `../../AutomationBase Beispiel/AutomationBase_Beispiel/AutomationBase_Beispiel/AutomationBase_Beispiel/POUs/Commands/FB_BM_ProcessCycle.TcPOU`
- `../../AutomationBase Beispiel/AutomationBase_Beispiel/AutomationBase_Beispiel/AutomationBase_Beispiel/POUs/Commands/FB_BM_CommandBroker.TcPOU`
