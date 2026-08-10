# Phase-1-Abschlussvalidierung

## Status

- Phase: 1 – Headless Generator-Kern
- Ergebnis: abgeschlossen
- Prüfdatum: 2026-08-10
- Referenzmodell: `examples/BrushMachine.reference.etab.json`

Phase 1 stellt einen deterministischen, dateibasierten Generator-Kern bereit. Er validiert das Projektmodell, plant alle Änderungen read-only und schreibt erst nach dem expliziten CLI-Befehl `generate`. Die visuelle Modellierung beginnt in Phase 2; die Einbindung in eine `.plcproj` und der Compile der tatsächlich generierten Projektartefakte folgen in Phase 3.

## Umgesetzter Umfang

Der BrushMachine-Referenzlauf erzeugt 14 SPS-Artefakte:

- drei Command-Enums,
- drei Request-DUTs,
- vier projektspezifische Status-DUTs,
- vier ApplicationUnit-Basis-FBs.

Hinzu kommt `Generated/etab-generation-manifest.json`. Das Manifest enthält Projekt- und Schemaversion, semantischen Modellhash sowie je Artefakt Modell-ID, Art, Name, stabile TwinCAT-GUID, relativen Pfad und SHA-256-Inhaltshash.

Die Basis-FBs erweitern `ETAB.FB_ETAB_ApplicationUnit`, rufen `SUPER^()` und anschließend den geschützten Hook `OnExecuteOperation()` auf. Der generierte Hook bleibt absichtlich leer. Safety-, Bewegungs- und Prozesslogik wird nicht generiert.

## CLI

Verfügbar sind:

```text
etab validate <project-file> [--schema <schema-file>]
etab preview  <project-file> [--schema <schema-file>] [--root <directory>] [--content]
etab check    <project-file> [--schema <schema-file>] [--root <directory>]
etab generate <project-file> [--schema <schema-file>] [--root <directory>]
```

`preview` und `check` schreiben nicht. `check` liefert Exit-Code 0 nur für einen vollständig synchronen Stand und Exit-Code 1 für einen konfliktfreien, aber veralteten Stand oder einen Konflikt. `generate` führt nur einen konfliktfreien Plan aus.

## Schreib- und Konfliktsicherheit

Vor dem ersten Schreibzugriff werden Zielroot, relative Pfade, Reparse Points, Zielbelegung und die seit der Planung erwarteten Hashes erneut geprüft. Der Schreibablauf:

1. neue Inhalte in einem UUID-basierten Staging-Verzeichnis unter `Generated/` vorbereiten,
2. geänderte oder zu löschende verwaltete Dateien in ein separates Backup-Verzeichnis verschieben,
3. `create`, `update`, `rename` und `delete` anwenden,
4. das neue Manifest zuletzt schreiben,
5. Transaktionsverzeichnisse nur nach erneuter Pfad- und Reparse-Point-Prüfung entfernen.

Bei einem Fehler werden bereits geschriebene Ziele entfernt und Backups einzeln zurückgesichert. Scheitert eine Rücksicherung, bleibt das betreffende Backup zur manuellen Wiederherstellung erhalten und der Lauf wird als fehlgeschlagen gemeldet. Konflikte blockieren den gesamten Schreibablauf vor der Transaktion.

## Automatisierte Nachweise

```text
dotnet test ETAB.Engineering.sln --configuration Release --no-restore
Bestanden: 35, Fehler: 0, Übersprungen: 0
```

Abgedeckt sind unter anderem:

- Schema- und Semantikvalidierung einschließlich doppelter stabiler Command-IDs und `enumValue`-Werte,
- Snapshot- und Determinismusprüfung aller Artefaktarten,
- stabile UUID-v5-TwinCAT-GUIDs,
- Manifest- und semantischer Modellhash,
- `create`, `update`, `rename`, `delete`, `unchanged` und `conflict`,
- Erkennung manuell veränderter oder nach der Vorschau belegter Dateien,
- vollständiger No-op bei unveränderter Regeneration,
- byte-identische Ausgabe bei gleicher Eingabe,
- Transaktionsrollback nach künstlichem Fehler mitten in einer Update-/Rename-Folge,
- unveränderte Benutzerdatei außerhalb von `Generated/`,
- UTF-8 ohne BOM, LF-Zeilenenden und passende Manifest-Inhaltshashes,
- strukturelles Parsen aller tatsächlich geschriebenen `.TcDUT`- und `.TcPOU`-Dateien,
- global eindeutige TwinCAT-XML-`Id`-Attribute im Referenzlauf.

## Isolierter CLI-End-to-End-Lauf

Der Release-Build wurde gegen einen frisch erzeugten UUID-Unterordner von `%TEMP%\etab-engineering-cli-verification` ausgeführt. Das Verzeichnis wurde vor der Bereinigung auf Pfadgrenze und Reparse Points geprüft.

```text
CLI_VERIFY check-before=1 generate-first=0 check-after=0 generate-second=0 files=15 byte-identical=true outside-generated-files=0
```

Damit sind der erwartete Out-of-date-Status vor der Generierung, das erste Schreiben, der synchrone Folgestand, die No-op-Regeneration und die Schreibgrenze praktisch nachgewiesen.

## Phase-1-Abnahmekriterien

- [x] Gleiche Eingabe erzeugt byte-identische Ausgabe.
- [x] Doppelte stabile Command-IDs und doppelte `enumValue`-Werte je Node werden abgewiesen.
- [x] Keine Datei außerhalb des Generatorbereichs wird verändert.
- [x] Geschriebene TwinCAT-XML-Artefakte lassen sich strukturell parsen.

## Bewusste Abgrenzung

Phase 1 verändert weder `ET_AutomationBase` noch eine `.plcproj`. Der Phase-0-Spike hat das verwendete Vererbungs- und Hook-Muster bereits mit TwinCAT XAE erfolgreich kompiliert. Die 14 durch diesen Generator erzeugten Dateien sind strukturell validiert, aber noch nicht in ein Projekt eingebunden und daher noch nicht selbst durch XAE kompiliert. Dieser stärkere Nachweis gehört zusammen mit `<Compile Include="…">`, GVL-/PRG-Strukturen und Projektkopie ausdrücklich zu Phase 3.

Nicht nachgewiesen sind außerdem Simulation, Online-Test und Maschinenverhalten.
