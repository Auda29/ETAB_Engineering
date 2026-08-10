# Phase-1B-Validierungsprotokoll

## Status

- Phase: 1B – deterministische DUT-Vorschau
- Ergebnis: abgeschlossen
- Prüfdatum: 2026-08-10
- Ausführungsmodus: vollständig im Speicher, ohne Dateischreibzugriffe

Phase 1B erweitert den validierten Projektkern um die erste echte Erzeugungsstufe. Sie rendert die geplanten Command-Enums, Request-DUTs und Status-DUTs als vollständige TwinCAT-XML-Inhalte, schreibt diese aber bewusst noch nicht nach `Generated/` und verändert keine `.plcproj`.

## Umgesetzte Artefakte

Je nach `node.generate` entstehen:

| Artefaktart | Beispielname | Zielpfad der späteren Generierung |
|---|---|---|
| `command-enum` | `E_BM_ProcessCommand` | `Generated/DUTs/Commands/*.TcDUT` |
| `request-dut` | `ST_BM_ProcessRequest` | `Generated/DUTs/Requests/*.TcDUT` |
| `status-dut` | `ST_BM_ProcessStatus` | `Generated/DUTs/Status/*.TcDUT` |

Die BrushMachine-Vorschau enthält zehn Artefakte:

- drei Command-Enums,
- drei Request-DUTs,
- vier Status-DUTs.

RecipeManager, MachineLink und ProcessCycle erzeugen im Referenzmodell noch keine DUTs, weil ihre jeweiligen Generierungsflags deaktiviert sind.

## Deterministische Regeln

- Nodes: `name`, danach stabile `id`.
- Commands: `enumValue`, danach `name`, danach stabile `id`.
- Payloadfelder: unveränderte Modellreihenfolge.
- Layout: vollständig von Artefakten, GUIDs und Hashes ausgeschlossen.
- Zeilenenden: immer LF, unabhängig vom Betriebssystem.
- Inhalts-Hash: SHA-256 über den UTF-8-Inhalt ohne BOM.
- TwinCAT-GUID: UUID v5 mit dem festgelegten Generator-Namespace.

Der UUID-v5-Name entspricht dem Phase-0-Vertrag:

```text
<project-id>/<model-id>/<artifact-kind>
```

Dadurch bleibt die TwinCAT-GUID bei einer Node-Umbenennung stabil. Eine Änderung von `symbolStem` kann Dateiname und Inhalt ändern, aber nicht die aus der Node-ID abgeleitete Objekt-GUID.

## Generierte Verträge

### Command-Enum

- Attribute `qualified_only`, `strict` und `to_string`,
- feste numerische `enumValue`-Werte,
- numerisch deterministische Sortierung,
- Auto-Generated-Marker mit Node-ID und Artefaktart.

### Request-DUT

Fester Kopf:

```iecst
bExecute   : BOOL;
eCommand   : <generiertes Command-Enum>;
nCommandID : UDINT;
```

Danach folgen die Payloadfelder in Modellreihenfolge. Arraydimensionen werden beispielsweise als `ARRAY[1..3] OF LREAL` gerendert.

### Status-DUT

Der feste Kopf folgt der Node-Art:

| Node-Art | eingebetteter Status |
|---|---|
| `applicationUnit` | `stUnit : ETAB.ST_ETAB_ApplicationUnitStatus` |
| Application Unit mit Typed Request | zusätzlich `stOperation : ETAB.ST_ETAB_CommandStatus` |
| `commandUnit` | `stCommand : ETAB.ST_ETAB_CommandStatus` |
| `recipeManager` | `stRecipe : ETAB.ST_ETAB_RecipeStatus` |
| `machineLink` | `stLink : ETAB.ST_ETAB_MachineLinkStatus` |

Die Library-DUTs werden nur referenziert. `ET_AutomationBase` wurde nicht verändert.

## CLI

Kompakte Vorschau:

```powershell
dotnet run --project .\src\ETAB.Engineering.Cli\ETAB.Engineering.Cli.csproj -- preview .\examples\BrushMachine.reference.etab.json
```

Vorschau einschließlich vollständiger XML-Inhalte:

```powershell
dotnet run --project .\src\ETAB.Engineering.Cli\ETAB.Engineering.Cli.csproj -- preview .\examples\BrushMachine.reference.etab.json --content
```

Die kompakte Ausgabe enthält je Artefakt:

- Artefaktart,
- relativen Zielpfad,
- deterministische TwinCAT-GUID,
- SHA-256-Inhaltshash.

`preview` validiert das Projekt zuerst. Ein ungültiges Modell verwendet weiterhin Exit-Code 1. `--schema <datei>` kann wie bei `validate` ein explizites Schema auswählen.

## Ausgeführte Nachweise

### Automatisierte Tests

```text
dotnet test ETAB.Engineering.sln --no-restore
Bestanden: 17, Fehler: 0, Übersprungen: 0
```

Neben den sieben Phase-1A-Tests prüfen die neuen Tests:

- exakte Artefaktliste der BrushMachine,
- Golden-Snapshots für Process-Command, -Request und -Status,
- wohlgeformtes XML für jedes Artefakt,
- SHA-256 gegen den tatsächlichen UTF-8-Inhalt,
- ausschließlich LF-Zeilenenden,
- keine Ausgabeänderung durch Layoutänderungen,
- keine Ausgabeänderung durch andere Node- oder Command-Eingabereihenfolge,
- stabile TwinCAT-GUIDs bei Node-Umbenennung,
- korrekte eingebettete Library-Statusfelder für alle vier Node-Arten,
- UUID-v5-Implementierung gegen einen bekannten RFC-Testvektor.

### CLI-Positivfall

```text
PREVIEW ...\examples\BrushMachine.reference.etab.json
Project: BrushMachine
Artifacts: 10
PREVIEW_EXIT_CODE=0
```

### CLI-Fehlerpfad

```text
INVALID ...\README.md
[JSON_PARSE] line 1, byte 1: '#' is an invalid start of a value.
INVALID_PREVIEW_EXIT_CODE=1
```

### Schreibschutz

Vor und nach `preview --content` wurde der erwartete Ausgabeordner geprüft:

```text
GENERATED_EXISTS_BEFORE=False
GENERATED_EXISTS_AFTER=False
```

Damit ist für den ausgeführten Referenzlauf nachgewiesen, dass Phase 1B keinen `Generated/`-Ordner angelegt hat.

## Noch nicht nachgewiesen

- kein Schreiben der gerenderten `.TcDUT`-Dateien,
- kein Manifest und kein Vergleich mit bereits vorhandenen Dateien,
- keine Einstufung als `create`, `update`, `rename`, `delete`, `unchanged` oder `conflict`,
- keine Basis-FB-, GVL- oder PRG-Erzeugung,
- keine `.plcproj`-Integration,
- kein TwinCAT-Compile der Vorschauartefakte,
- keine Simulation, kein Online-Test und keine Maschinenvalidierung.

Diese Grenzen sind bewusst: Erst die nächste Phase ergänzt Manifest und sicheren Dateisystemvergleich. Ein schreibender `generate`-Befehl folgt erst danach.
