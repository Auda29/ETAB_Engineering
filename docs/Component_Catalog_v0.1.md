# ETAB-Bausteinkatalog v0.1

## Zweck

Dieser Katalog legt fest, welche Bausteine der aktuellen `ET_AutomationBase`-Library im visuellen ETAB-Modell v0.1 als eigenständige Nodes erscheinen. Er basiert auf dem statisch geprüften Stand `ET_AutomationBase_v0.1.0.3`.

Die Klassifikation ist kein TwinCAT-Compile- oder Laufzeitnachweis.

## Statusklassen

- **MVP-Node:** im Projektmodell v0.1 direkt modellierbar.
- **Infrastruktur:** wird als Option oder generiertes Detail eines MVP-Nodes behandelt.
- **Zurückgestellt:** aktueller Library-Baustein, aber nicht Teil des ersten visuellen Modells.
- **Projektmuster:** Beispielcode, kein generischer ETAB-Library-Baustein.

## MVP-Nodes

### `applicationUnit`

Library-Grundlage:

- `ETAB.FB_ETAB_ApplicationUnit`
- `ETAB.I_ETAB_ApplicationUnit`
- `ETAB.ST_ETAB_ApplicationUnitOptions`
- `ETAB.ST_ETAB_ApplicationUnitStatus`

Eigenschaften:

- erweitert `ET.Statemodel_Unit`,
- enthält intern eine `FB_ETAB_CommandUnit`,
- verarbeitet die ETAB-Grundkommandos,
- stellt Unit-Modus, Unit-Zustand und Command-Status bereit,
- kann als Master- oder Subunit eingesetzt werden.

ETAB-Grundkommandos:

| Kommando | Wert |
|---|---:|
| `NoAction` | 0 |
| `Reset` | 10 |
| `Start` | 20 |
| `Homing` | 25 |
| `Stop` | 30 |
| `Abort` | 40 |
| `Clear` | 50 |
| `User` | 100 |

Modellierung v0.1:

- Maschinen-, Motion-, Workpiece- und Process-Units sind Rollen einer `applicationUnit`, keine eigenen ETAB-Klassen.
- Projektspezifische Kommandos werden standardmäßig auf `E_ETAB_UnitCommand.User` abgebildet.
- Request-Payload und Status-Payload sind projektspezifisch.
- `bExecute`, `eCommand` und `nCommandID` gehören zum impliziten Request-Vertrag und werden nicht als normale Payload-Felder gepflegt.

Generatorziel:

- Command-Enum,
- Request- und Status-DUT,
- generierter Basisbaustein,
- optional Instanz und Statusaggregation.

### `commandUnit`

Library-Grundlage:

- `ETAB.FB_ETAB_CommandUnit`
- `ETAB.I_ETAB_CommandUnit`
- `ETAB.ST_ETAB_CommandOptions`
- `ETAB.ST_ETAB_CommandStatus`

Eigenschaften:

- generischer Kommandoexecutor ohne eigenes ET-State-Model,
- verwendet `ET.SEQUENCE_HDL`,
- unterstützt Start, Finish, Abort und Reset,
- stellt Sequenzzustand, Verlauf und Fehler bereit.

Modellierung v0.1:

- geeignet für Ablaufkoordinatoren und projektspezifische Funktionsbausteine,
- Beispiel: `FB_BM_ProcessCycle`,
- die eigentliche CASE-/Sequenzlogik bleibt handgeschrieben.

Generatorziel:

- optional Command-Enum,
- Request- und Statusvertrag,
- Command-Router beziehungsweise Basisgerüst,
- keine automatische Prozesssequenz.

### `recipeManager`

Library-Grundlage:

- `ETAB.FB_ETAB_RecipeManager`
- `ETAB.E_ETAB_RecipeCommand`
- `ETAB.ST_ETAB_RecipeOptions`
- `ETAB.ST_ETAB_RecipeStatus`

Unterstützte Library-Kommandos:

| Kommando | Wert |
|---|---:|
| `NoAction` | 0 |
| `Read` | 10 |
| `Write` | 20 |
| `SaveAs` | 30 |
| `LoadDefault` | 40 |
| `Delete` | 50 |
| `Validate` | 60 |
| `Reset` | 70 |

Modellierung v0.1:

- referenziert einen projektspezifischen Rezept-Datentyp,
- beschreibt Dateiname, Pfad, XPath und Optionen,
- erzeugt keine fachliche Rezeptstruktur und keine Validierungslogik,
- Pointer, Größe und externe Validierung werden später im Projektadapter gebunden.

Generatorziel:

- Instanz- und Konfigurationsgerüst,
- öffentlicher Statusvertrag,
- keine automatische Serialisierungs- oder Rezeptfachlogik.

### `machineLink`

Library-Grundlage:

- `ETAB.FB_ETAB_MachineLink`
- `ETAB.ST_ETAB_MachineLinkData`
- `ETAB.ST_ETAB_MachineLinkOptions`
- `ETAB.ST_ETAB_MachineLinkStatus`

Bridge-Typen:

| Bridge-Typ | Wert |
|---|---:|
| `GenericBridge` | 0 |
| `EL6695` | 10 |
| `EL6692` | 20 |
| `ExternalBridge` | 30 |

Modellierung v0.1:

- Rolle Primary/Secondary,
- Bridge-Typ,
- Watchdogzeit,
- Token- und Tie-Break-Optionen,
- logische Verbindung zu einem Partner.

Generatorziel:

- Instanz- und Konfigurationsgerüst,
- Rx-/Tx- und Statusvertrag,
- keine automatische `%I*`-/`%Q*`-Adressierung.

## Infrastruktur

### Machine-Link-Adapter

- `FB_ETAB_MachineLinkEL6692`
- `FB_ETAB_MachineLinkEL6695`
- `FB_ETAB_MachineLinkExternalBridge`
- `FB_ETAB_MachineLinkDataByteMapper`

Diese Bausteine erscheinen in v0.1 nicht als unabhängige Nodes. Sie werden aus der `machineLink`-Konfiguration ausgewählt beziehungsweise später als erweiterte Ansicht sichtbar gemacht.

### ETAB-Status- und Options-DUTs

Library-DUTs wie `ST_ETAB_CommandStatus` oder `ST_ETAB_ApplicationUnitStatus` sind feste Verträge. Der Benutzer beschreibt im Editor nur zusätzliche projektspezifische Statusfelder.

Der Generator verändert diese Library-DUTs nicht. Ein projektspezifischer Status-DUT bettet den passenden Library-Status als Feld ein. Bei einer `applicationUnit` mit fachlichen Typed Commands werden Unit-Lifecycle (`stUnit`) und fachliche Operation (`stOperation`) getrennt abgebildet. Die zusätzlichen Felder aus `statusPayload` werden anschließend angefügt.

## Zurückgestellt

### FANUC Robot Interface

Library-Grundlage:

- `FB_ETAB_FanucInterface`
- `FB_ETAB_FanucUopEtherCatDioMapper`
- FANUC Command-, Request-, Status- und UOP-DUTs

Begründung:

- hersteller- und protokollspezifisch,
- nicht erforderlich, um das generische ETAB-Modell zu beweisen,
- soll später als Integrationsnode oder Plugin ergänzt werden.

Der Baustein bleibt katalogisiert, ist jedoch kein gültiger Node-Kind im Schema v0.1.

## Projektmuster aus dem Bürstautomaten

| Projektbaustein | Klassifikation | Behandlung im Modell |
|---|---|---|
| `FB_BM_Application` | Composition Root | Projektwurzel, nicht automatisch als ETAB-Unit interpretiert |
| `FB_BM_Machine` | Master Application Unit | `applicationUnit`, Rolle `machine` |
| `FB_BM_MotionUnit` | Typed Application Unit | `applicationUnit`, Rolle `motion` |
| `FB_BM_WorkpieceUnit` | Typed Application Unit | `applicationUnit`, Rolle `workpiece` |
| `FB_BM_ProcessUnit` | Typed Application Unit | `applicationUnit`, Rolle `process` |
| `FB_BM_ProcessCycle` | Ablaufkoordinator | `commandUnit`, Rolle `orchestrator` |
| `FB_BM_CommandBroker` | Bedienkommando-Arbitration | Projektmuster, im MVP nicht generiert |
| `FB_BM_RecipeService` | projektspezifischer Rezeptadapter | verwendet `recipeManager` |
| `FB_BM_CellInterface` | projektspezifischer Linkadapter | verwendet `machineLink` |

## Quellen im Workspace

- `../../ET_AutomationBase_v0.1.0.3/README.md`
- `../../ET_AutomationBase_v0.1.0.3/ET_AutomationBase/ET_AutomationBase/ApplicationUnit/`
- `../../ET_AutomationBase_v0.1.0.3/ET_AutomationBase/ET_AutomationBase/RecipeManagement/`
- `../../ET_AutomationBase_v0.1.0.3/ET_AutomationBase/ET_AutomationBase/MachineInterface/`
- `../../ET_AutomationBase_v0.1.0.3/ET_AutomationBase/ET_AutomationBase/RobotInterface/Fanuc/`
