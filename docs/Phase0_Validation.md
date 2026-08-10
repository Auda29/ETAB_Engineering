# Phase-0-Validierungsprotokoll

## Status

- Phase: 0 – Spezifikation
- Ergebnis: abgeschlossen
- Prüfdatum: 2026-08-10
- Implementierungsstand: kein Generator- oder Editorcode; isolierter, nicht instanziierter TwinCAT-Compile-Spike vorhanden

## Erzeugte Spezifikationsartefakte

- `docs/Component_Catalog_v0.1.md`
- `docs/Model_Specification_v0.1.md`
- `docs/Generation_Contract_v0.1.md`
- `docs/AutomationBase_Reference_v0.1.md`
- `schemas/etab-project.schema.json`
- `examples/BrushMachine.reference.etab.json`
- `spikes/TwinCAT_BaseFb_Inheritance.md`

## Architektur-Nachtrag vom 2026-08-10

- Das Command-Enum-Literal heißt im Modell und Schema `enumValue`. Es ist ausdrücklich nicht das Laufzeitfeld `nCommandID`.
- Generierte projektspezifische Status-DUTs betten die unveränderten öffentlichen Library-Status-DUTs ein und ergänzen nur `statusPayload`.
- Für Application Units mit fachlichen Typed Commands werden `stUnit : ETAB.ST_ETAB_ApplicationUnitStatus` und `stOperation : ETAB.ST_ETAB_CommandStatus` getrennt geführt.
- Die Quellen unter `ET_AutomationBase_v0.1.0.3` wurden für diesen Nachtrag nicht verändert.

## Referenzinventur

Statisch geprüft wurden:

- öffentliche ApplicationUnit-/CommandUnit-Verträge,
- RecipeManager-Verträge,
- MachineLink-Verträge und Bridge-Typen,
- FANUC-Bausteine zur Abgrenzung des MVP,
- `FB_BM_Application`,
- Master-, Motion-, Workpiece- und ProcessUnit,
- ProcessCycle und CommandBroker,
- fachliche Command-Enums sowie Request-/Statusverträge.

## JSON-Schema-Prüfung

Validator:

- JSON Schema Draft 2020-12
- Python-Paket `jsonschema` mit `Draft202012Validator`
- UUID-Formatprüfung aktiviert

Ergebnis:

```text
VALID
```

## Semantikprüfung des Referenzmodells

Geprüfte Modellwerte:

| Wert | Ergebnis |
|---|---:|
| Nodes | 7 |
| Beziehungen | 12 |
| persistente IDs insgesamt | 102 |
| generierbare Artefaktnamen | 14 |
| `contains`-Parentbeziehungen | 4 |

Geprüfte Regeln:

- alle IDs eindeutig,
- alle Relation-Endpunkte vorhanden,
- keine Selbstbeziehungen,
- Command-Namen und `enumValue`-Werte je Node eindeutig,
- `NoAction = 0` für generierte Command-Enums,
- gültige Arraygrenzen,
- Layouteinträge referenzieren existierende Nodes,
- höchstens ein Parent je Node,
- keine `contains`-Zyklen,
- Relationstypen passen zum Ziel-Node,
- implizite Request-Felder nicht als Payload dupliziert,
- Request-DUT wird nur zusammen mit Command-Enum erzeugt,
- keine Kollision generierter Artefaktnamen,
- keine Kollision projektspezifischer Statusfelder mit reservierten Library-Statusfeldern.

Ergebnis:

```text
EXTENDED_SEMANTIC_CHECKS_VALID
```

## Abnahme gegen Phase-0-Kriterien

- [x] aktueller öffentlicher ETAB-Stand katalogisiert
- [x] Projektmodell strukturell definiert
- [x] semantische Zusatzregeln definiert
- [x] JSON-Schema v0.1 gültig
- [x] BrushMachine-Referenzmodell schemafähig
- [x] Namensregeln verbindlich
- [x] Modell- und TwinCAT-ID-Regeln verbindlich
- [x] Generator-/User-Eigentumsgrenze verbindlich
- [x] Beispiel-Units klassifiziert
- [x] Safety-, IO- und Prozessimplementierung außerhalb der Generierung gehalten
- [x] `enumValue` und Laufzeit-`nCommandID` eindeutig getrennt
- [x] Statusaggregation ohne Library-Änderung festgelegt
- [x] Basis-FB-Vererbung und Hook-Überschreibung compilerseitig bestätigt

## TwinCAT-Compile-Spike

Compile-Host:

- `AutomationBase_Beispiel.sln`
- Konfiguration `Release | TwinCAT RT (x64)`
- drei nicht instanziierte POUs unter `POUs/Spikes/ETABEngineering/`

Ausführung über die lokale Beckhoff XAE-DTE-Automation:

```text
TcXaeShell.DTE.15.0
LastBuildInfo=0
COMPILE_SUCCESS
```

Der Compile bestätigt die gültige Kette `FB_ETABENG_UserUnit -> FB_ETABENG_GeneratedUnitBase -> ETAB.FB_ETAB_ApplicationUnit`, geerbte Ein-/Ausgänge und den überschreibbaren geschützten Hook `OnExecuteOperation`.

## Nicht nachgewiesen

- kein Generatorlauf,
- keine durch einen Generator erzeugten `.TcPOU`-/`.TcDUT`-Dateien; die drei Spike-POUs sind bewusst handgeschriebene Testartefakte,
- kein Online-/Runtime-Nachweis des Spike-Testtreibers,
- keine Simulation,
- keine Maschinenvalidierung.

Diese Nachweise gehören in spätere Phasen und werden durch die Phase-0-Prüfungen nicht ersetzt.
