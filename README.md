# ETAB Engineering v0.1.0.0

Visuelles Engineering-Werkzeug zur Beschreibung einer logischen Maschine und zur späteren Erzeugung eines TwinCAT-SPS-Templates auf Basis der `ET_AutomationBase`-Library.

## Aktueller Stand

Phase 0 – Spezifikation: abgeschlossen am 2026-08-07, Architektur-Nachtrag validiert am 2026-08-10. Phase 1 – Headless Generator-Kern: abgeschlossen am 2026-08-10.

Modell, Regeln, Generierungsgrenze und Referenzklassifikation sind festgelegt und am BrushMachine-Referenzmodell geprüft. `enumValue`, der projektspezifische Statusvertrag und das Basis-FB-Vererbungsmuster sind verbindlich geklärt. Der Basis-FB-Spike kompiliert im BrushMachine-Projekt erfolgreich.

Der Headless-Kern ist als .NET-Solution umgesetzt. Er lädt `*.etab.json`, validiert JSON Schema Draft 2020-12 und die projektspezifischen Semantikregeln. Die CLI-Befehle `validate`, `preview`, `check` und `generate` stehen zur Verfügung. Der Generator rendert Command-Enums, Request- und Status-DUTs sowie schlanke ApplicationUnit-Basis-FBs, verwaltet stabile TwinCAT-GUIDs und Inhalts-Hashes im Manifest und führt konfliktfreie Änderungen transaktional ausschließlich im konfigurierten Generatorbereich aus.

Noch nicht enthalten sind der visuelle Editor, GVL-/PRG-Erzeugung und die `.plcproj`-Integration. Der echte Compile der generierten Projektartefakte ist deshalb Teil von Phase 3; das zugrunde liegende Basis-FB-Vererbungs- und Hook-Muster wurde bereits in Phase 0 erfolgreich in TwinCAT kompiliert.

## Schnellstart

```powershell
dotnet build .\ETAB.Engineering.sln
dotnet test .\ETAB.Engineering.sln --no-build
dotnet run --project .\src\ETAB.Engineering.Cli\ETAB.Engineering.Cli.csproj -- validate .\examples\BrushMachine.reference.etab.json
dotnet run --project .\src\ETAB.Engineering.Cli\ETAB.Engineering.Cli.csproj -- preview .\examples\BrushMachine.reference.etab.json --root . --content
dotnet run --project .\src\ETAB.Engineering.Cli\ETAB.Engineering.Cli.csproj -- generate .\examples\BrushMachine.reference.etab.json --root .
dotnet run --project .\src\ETAB.Engineering.Cli\ETAB.Engineering.Cli.csproj -- check .\examples\BrushMachine.reference.etab.json --root .
```

`generate` ist der explizite Schreibbefehl. Er bricht bei Konflikten vor jedem Schreibzugriff ab und verändert keine Datei außerhalb des im Modell konfigurierten Generatorbereichs.

## Dokumente

- [Gesamtplan](ETAB_Engineering_Plan.md)
- [ETAB-Bausteinkatalog v0.1](docs/Component_Catalog_v0.1.md)
- [Modellspezifikation v0.1](docs/Model_Specification_v0.1.md)
- [Generierungsvertrag v0.1](docs/Generation_Contract_v0.1.md)
- [AutomationBase-Referenzklassifikation](docs/AutomationBase_Reference_v0.1.md)
- [Phase-0-Validierungsprotokoll](docs/Phase0_Validation.md)
- [Phase-1A-Validierungsprotokoll](docs/Phase1A_Validation.md)
- [Phase-1B-Validierungsprotokoll](docs/Phase1B_Validation.md)
- [Phase-1C-Validierungsprotokoll](docs/Phase1C_Validation.md)
- [Phase-1-Abschlussvalidierung](docs/Phase1_Validation.md)
- [TwinCAT-Spike Basis-FB-Vererbung](spikes/TwinCAT_BaseFb_Inheritance.md)
- [JSON-Schema v0.1](schemas/etab-project.schema.json)
- [BrushMachine-Referenzmodell](examples/BrushMachine.reference.etab.json)

## Phase-0-Abnahme

Phase 0 ist abgeschlossen, wenn:

- der Bausteinkatalog den aktuellen öffentlichen ETAB-Stand korrekt klassifiziert,
- Struktur und Semantik des Projektmodells definiert sind,
- das Referenzmodell gegen das JSON-Schema validiert,
- Namens-, ID- und Regenerationsregeln verbindlich dokumentiert sind,
- der Bürstautomat ohne Übernahme seiner Prozessimplementierung beschreibbar ist.

Alle Kriterien wurden im [Validierungsprotokoll](docs/Phase0_Validation.md) nachgewiesen.

## Phase-1-Abnahme

Phase 1 ist abgeschlossen, wenn:

- gleiche Eingaben byte-identische SPS-Artefakte erzeugen,
- doppelte stabile Command-IDs und `enumValue`-Werte abgewiesen werden,
- ausschließlich der konfigurierte Generatorbereich verändert wird,
- geschriebene TwinCAT-XML-Artefakte strukturell gültig sind,
- Konflikte den gesamten Schreibablauf blockieren und ein Schreibfehler zurückgerollt wird.

Alle Kriterien wurden in der [Phase-1-Abschlussvalidierung](docs/Phase1_Validation.md) nachgewiesen.
