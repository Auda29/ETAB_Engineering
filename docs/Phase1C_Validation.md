# Phase-1C-Validierungsprotokoll

> Dieses Dokument hält den damaligen read-only Zwischenstand 1C fest. Die inzwischen abgeschlossene schreibende Phase 1 einschließlich Basis-FBs, `check`, `generate` und Rollback ist in der [Phase-1-Abschlussvalidierung](Phase1_Validation.md) dokumentiert.

## Status

- Phase: 1C – Manifest- und Dateisystemplanung
- Ergebnis: abgeschlossen
- Prüfdatum: 2026-08-10
- Ausführungsmodus: read-only außerhalb temporärer automatisierter Tests

Phase 1C erweitert die reine Artefaktvorschau um einen sicheren Vergleich mit einem vorhandenen Generatorstand. Der Kern erzeugt ein deterministisches Manifest im Speicher, liest ein gegebenenfalls vorhandenes Manifest und prüft exakt die darin verwalteten Dateien. Es werden weiterhin keine Projekt- oder Generatorausgaben geschrieben, umbenannt oder gelöscht.

## Manifest v0.1

Der vorgeschlagene Inhalt von `Generated/etab-generation-manifest.json` enthält:

- `manifestVersion`,
- `generatorVersion`,
- `schemaVersion`,
- `projectId`,
- `semanticModelHash`,
- pro Artefakt `sourceModelId`, `kind`, `name`, `twinCatGuid`, `relativePath` und `contentHash`.

Das Manifest ist deterministisch sortiert, verwendet zwei Leerzeichen Einrückung und ausschließlich LF-Zeilenenden. Es enthält weder Zeitstempel noch Benutzer-, Rechner- oder absolute Pfadangaben.

## Semantischer Modellhash

Der Modellhash ist SHA-256 über eine kanonische JSON-Repräsentation des typisierten Modells.

Ausgeschlossen:

- der vollständige `layout`-Block.

Kanonisch sortiert:

- Nodes nach `name` und `id`,
- Commands nach `enumValue`, `name` und `id`,
- Relationen nach `kind`, `sourceNodeId`, `targetNodeId` und `id`,
- MTP-Procedures nach `procedureId`, `name` und `id`,
- JSON-Objekte nach Propertynamen.

Payloadfelder behalten ihre Modellreihenfolge, da diese laut Modellspezifikation SPS-semantisch ist.

## Zielroot und Pfadsicherheit

Ohne zusätzliche Option verwendet die CLI das Verzeichnis der `.etab.json`-Projektdatei als Projektroot. Für Tests gegen einen anderen Zielstand kann er explizit gesetzt werden:

```powershell
etab preview BrushMachine.etab.json --root C:\TwinCAT\BrushMachine
```

Sicherheitsregeln:

- der Projektroot muss als Verzeichnis existieren,
- `project.generation.generatedRoot` muss relativ sein,
- der aufgelöste Generatorroot muss ein echtes Unterverzeichnis des Projektroots sein,
- `.`- und `..`-Segmente sind nicht zulässig,
- jeder aktuelle und alte Manifestpfad wird absolut aufgelöst und erneut gegen den Generatorroot geprüft,
- es werden keine rekursiven Dateisystemscans oder Globs verwendet.

## Änderungsarten

| Status | Voraussetzung |
|---|---|
| `create` | kein alter Manifest-Eintrag und Zielpfad frei |
| `unchanged` | verwaltete Datei entspricht altem und neuem Inhaltshash |
| `update` | verwaltete Datei entspricht dem alten Hash, der neue Inhalt ist anders |
| `rename` | gleiche Modell-ID und Artefaktart, stabile GUID, alter Pfad unverändert, neuer Pfad frei |
| `delete` | alter Manifest-Eintrag ist entfallen und die konkrete Altdatei ist unverändert |
| `conflict` | sicherer automatischer Folgeschritt ist nicht möglich |

Mindestens folgende Zustände werden als Konflikt behandelt:

- manifestierte Datei fehlt,
- manifestierte Datei wurde außerhalb von ETAB Engineering verändert,
- nicht manifestierte Datei belegt einen Zielpfad,
- Rename-Ziel ist belegt,
- Manifest ist syntaktisch oder semantisch ungültig,
- Manifest gehört zu einer anderen Projekt- oder Schema-ID,
- alte oder neue Pfade verlassen den Generatorbereich,
- gespeicherte TwinCAT-GUID stimmt nicht mit der deterministischen UUID v5 überein.

Bei einem Artefaktkonflikt wird auch der Manifeststatus als `conflict` ausgegeben. Ein späterer `generate`-Befehl darf dann nicht schreiben.

## CLI

Read-only Vergleich gegen den Standardroot neben der Projektdatei:

```powershell
dotnet run --project .\src\ETAB.Engineering.Cli\ETAB.Engineering.Cli.csproj -- preview .\examples\BrushMachine.reference.etab.json
```

Vergleich gegen einen expliziten Projektroot und Ausgabe aller geplanten Inhalte:

```powershell
dotnet run --project .\src\ETAB.Engineering.Cli\ETAB.Engineering.Cli.csproj -- preview .\examples\BrushMachine.reference.etab.json --root . --content
```

`--content` zeigt zusätzlich sämtliche geplanten `.TcDUT`-Inhalte und den vollständigen vorgeschlagenen Manifestinhalt. Es schreibt diese Inhalte nicht.

Exit-Codes:

- 0: Vorschau erfolgreich und konfliktfrei,
- 1: Projektvalidierung fehlgeschlagen oder Vorschau enthält Konflikte,
- 2: ungültige CLI-Argumente,
- 3: unerwarteter Ausführungsfehler.

## Automatisierte Nachweise

```text
dotnet test ETAB.Engineering.sln --no-restore
Bestanden: 27, Fehler: 0, Übersprungen: 0
```

Die zehn neuen Phase-1C-Tests prüfen:

- leerer Root ergibt zehn `create`-Artefakte und Manifest `create`,
- vollständig materialisierter, unveränderter Stand ergibt ausschließlich `unchanged`,
- fachliche Payloadänderung aktualisiert nur das betroffene DUT,
- `symbolStem`-Änderung ergibt drei `rename`-Operationen bei stabilen GUIDs,
- deaktiviertes Artefakt ergibt ein sicheres `delete`,
- manuell veränderte verwaltete Datei ergibt `conflict`,
- nicht manifestierter belegter Zielpfad ergibt `conflict`,
- ungültiges Manifest blockiert den Vergleich,
- ausbrechender Generatorroot wird vor dem Vergleich abgewiesen,
- Modellhash bleibt bei Layout- und nicht-semantischen Eingabereihenfolgen identisch.

Die Tests materialisieren Vergleichszustände nur in UUID-basierten Unterordnern von `%TEMP%\etab-engineering-tests`. Vor dem rekursiven Entfernen wird der aufgelöste Pfad erneut gegen genau diesen Testroot geprüft.

## Referenzlauf BrushMachine

Ohne vorhandenen Generatorstand:

```text
Project: BrushMachine
Artifacts: 10
Manifest: [create] Generated/etab-generation-manifest.json
10 x [create]
PREVIEW_EXIT_CODE=0
```

Vor und nach der CLI-Vorschau wurden sowohl der Standardroot als auch der explizite Root auf einen neu angelegten `Generated/`-Ordner geprüft. Die Vorschau legt keinen solchen Ordner an.

## Noch nicht nachgewiesen

- kein produktives Schreiben des Manifests oder der DUT-Dateien,
- keine produktiven Rename- oder Delete-Operationen,
- kein transaktionaler Schreibablauf oder Rollback,
- kein CLI-`generate` und kein CLI-`check`,
- keine Basis-FB-, GVL-, PRG- oder `.plcproj`-Erzeugung,
- kein TwinCAT-Compile der Vorschauartefakte,
- keine Simulation, kein Online-Test und keine Maschinenvalidierung.

Der nächste sichere Schnitt ist ein `check`-Befehl, der denselben Plan als CI-Prüfung auswertet. Erst danach sollte ein schreibender, konfliktgesperrter `generate`-Befehl umgesetzt werden.
