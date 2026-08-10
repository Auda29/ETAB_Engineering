# Phase-1A-Validierungsprotokoll

## Status

- Phase: 1A – Modell- und Validierungskern
- Ergebnis: abgeschlossen
- Prüfdatum: 2026-08-10
- Zielruntime: .NET 10
- Gepinntes SDK: 10.0.302 über `global.json`

Phase 1A ist ein rein lesender Entwicklungsschnitt. Er erzeugt noch keine TwinCAT-Objekte, schreibt kein Manifest und verändert weder ein TwinCAT-Projekt noch `ET_AutomationBase`.

## Umgesetzte Struktur

```text
ETAB.Engineering.sln
├─ src/ETAB.Engineering.Core
│  ├─ Model
│  └─ Validation
├─ src/ETAB.Engineering.Cli
└─ tests/ETAB.Engineering.Core.Tests
```

- `ETAB.Engineering.Core`: typisiertes Projektmodell sowie Schema- und Semantikvalidierung
- `ETAB.Engineering.Cli`: Headless-Einstiegspunkt mit dem Befehl `validate`
- `ETAB.Engineering.Core.Tests`: Positiv- und Negativtests gegen das BrushMachine-Referenzmodell
- `JsonSchema.Net` 9.4.0: Auswertung des vorhandenen Schemas als JSON Schema Draft 2020-12

## Validierungskette

Ein Projekt durchläuft vier Stufen:

1. JSON-Syntax parsen
2. Dokument gegen `schemas/etab-project.schema.json` prüfen
3. in das typisierte C#-Projektmodell deserialisieren
4. projektübergreifende Semantikregeln prüfen

Semantisch geprüft werden:

- globale Eindeutigkeit aller stabilen `id`-Werte,
- Node-, Command- und Payload-Namen ohne IEC-Groß-/Kleinschreibungsduplikate,
- eindeutige `enumValue`-Werte je Node,
- genau ein `NoAction` mit `enumValue = 0` bei generierten Command-Enums,
- gültige Arraygrenzen,
- Kopplung von Request-DUT und Command-Enum,
- Schutz der impliziten Request- und eingebetteten Library-Statusfelder,
- MTP-Procedure-IDs und lokale Command-Referenzen,
- vorhandene Relation-Endpunkte, keine Selbstbeziehungen und passende Zieltypen,
- höchstens ein `contains`-Parent und keine Hierarchiezyklen,
- gültige und eindeutige Layoutreferenzen,
- kollisionsfreie generierte TwinCAT-Artefaktnamen.

## CLI

Aufruf aus diesem Ordner:

```powershell
dotnet run --project .\src\ETAB.Engineering.Cli\ETAB.Engineering.Cli.csproj -- validate .\examples\BrushMachine.reference.etab.json
```

Alternativ akzeptiert `validate` mit `--schema <datei>` einen expliziten Schemapfad. Ohne diese Option verwendet die CLI das mitkopierte Schema.

Exit-Codes:

| Code | Bedeutung |
|---:|---|
| 0 | Projekt gültig |
| 1 | Validierung fehlgeschlagen |
| 2 | ungültige CLI-Argumente |
| 3 | unerwarteter Ausführungsfehler |

Jeder Validierungsfehler enthält einen stabilen Fehlercode, einen JSON-Pfad und eine Beschreibung, zum Beispiel:

```text
[JSON_PARSE] line 1, byte 1: '#' is an invalid start of a value.
```

## Ausgeführte Nachweise

### Restore und Build

```text
dotnet restore ETAB.Engineering.sln
Restore erfolgreich für 3 Projekte.

dotnet build ETAB.Engineering.sln --no-restore
0 Warnungen, 0 Fehler
```

### Automatisierte Tests

```text
dotnet test ETAB.Engineering.sln --no-build --no-restore
Bestanden: 7, Fehler: 0, Übersprungen: 0
```

Abgedeckte Fälle:

- gültiges BrushMachine-Referenzmodell,
- veraltetes Command-Feld `value` statt `enumValue`,
- doppelte stabile ID,
- doppelter `enumValue`,
- Kollision mit einem reservierten Library-Statusfeld,
- unbekannter Relation-Endpunkt,
- invertierte Arraygrenze.

### CLI-Positivfall

```text
VALID ...\examples\BrushMachine.reference.etab.json
Project: BrushMachine
Nodes: 7
Relations: 12
```

### CLI-Fehlerpfad

Eine Nicht-JSON-Datei wurde absichtlich als Projekt übergeben:

```text
INVALID ...\README.md
[JSON_PARSE] line 1, byte 1: '#' is an invalid start of a value.
CLI_EXIT_CODE=1
```

## Noch nicht nachgewiesen

- keine Erzeugung von `.TcDUT`, `.TcPOU`, `.TcGVL` oder `.plcproj`-Einträgen,
- keine GUID- oder Manifestverwaltung,
- keine `preview`-, `generate`- oder `check`-Befehle,
- keine Snapshot- oder Determinismusprüfung generierter Dateien,
- kein neuer TwinCAT-Compile in Phase 1A,
- keine Simulation, kein Online-Test und keine Maschinenvalidierung.

Diese Punkte gehören zu den folgenden Schnitten von Phase 1 beziehungsweise zu Phase 3.
