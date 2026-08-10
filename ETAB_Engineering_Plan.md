# ETAB Engineering – Plan für einen visuellen SPS-Template-Generator

## Dokumentstatus

- Status: Umsetzung; Phase 0 und Phase 1 abgeschlossen, nächster Umsetzungsschritt ist Phase 2
- Stand: 2026-08-10
- Arbeitstitel: `ETAB Engineering`
- Zielumgebung: TwinCAT 3 und `ET_AutomationBase`
- Referenzprojekt: `AutomationBase Beispiel`

## 1. Ziel

`ETAB Engineering` soll ein visuelles Engineering-Werkzeug werden, mit dem eine Maschine ähnlich wie in einem HMI- oder MTP-Editor aus logischen Bausteinen beschrieben wird.

Aus dem gespeicherten Maschinenmodell soll ein reproduzierbares TwinCAT-SPS-Template auf Basis der `ET_AutomationBase`-Library erzeugt werden. Optional soll später eine MTP-Integrationsschicht für Services und Procedures generiert werden können.

Der Editor beschreibt die Struktur und die öffentlichen Verträge der Maschine. Die eigentliche Maschinen-, Safety-, Bewegungs- und Prozesslogik bleibt handgeschrieben.

## 2. Leitprinzipien

1. Das visuelle Maschinenmodell ist die einzige Quelle für generierte Strukturen.
2. Gleiche Eingaben müssen byte-identische Ausgaben erzeugen.
3. Generierter Code und handgeschriebener Code werden strikt getrennt.
4. Handgeschriebene Dateien dürfen niemals automatisch überschrieben werden.
5. Der Generator muss alle geplanten Änderungen vor dem Schreiben anzeigen können.
6. Stabile Objekt-IDs müssen Umbenennungen ohne unnötige TwinCAT-GUID-Wechsel erlauben.
7. Eine strukturelle XML-Prüfung ist kein TwinCAT-Compile-Nachweis.
8. Safety- und Maschinenverhalten werden nicht aus einem allgemeinen Diagramm abgeleitet.
9. MTP ist eine optionale Integrationsschicht und keine Voraussetzung für den ETAB-MVP.

## 3. Produktgrenze

### 3.1 Im visuellen Editor beschreibbar

- Maschinen- und Unit-Hierarchie
- ETAB-Basistypen
- Kommandos, stabile Modell-IDs und feste Enum-Werte
- Request-, Status- und Parameterdaten
- Eltern-/Kind-Beziehungen zwischen Units
- logische Abhängigkeiten und Verbindungen
- Rezepte und Machine Links
- öffentliche HMI-/Statusstrukturen
- optionale Freigabe einer Unit als MTP-Service
- optionale Zuordnung von MTP-Procedures zu ETAB-Kommandos

### 3.2 Nicht automatisch generiert

- Safety-Logik
- Kollisions- und Freigabeentscheidungen
- konkrete IO-Adressierung
- Achs- und Hardwarekonfiguration
- maschinenspezifische Prozesssequenzen
- Fehlerreaktionen und sichere Stoppabläufe
- reale Bewegungs-, Timeout- und Prozessparameter
- vollständige Bedienbilder für eine Runtime-HMI

Der visuelle Editor ist damit ein Engineering-Werkzeug mit HMI-artiger Bedienung, aber keine Runtime-HMI und kein allgemeiner Safety- oder Ablaufgenerator.

## 4. Zielarchitektur

```text
Visueller ETAB-Editor
        ↕
Versionsfähiges Maschinenmodell (*.etab.json)
        ↓
Generator-Kern
        ├─ Validierung
        ├─ Änderungsvorschau
        ├─ TwinCAT-SPS-Template
        ├─ Generierungsmanifest
        └─ optionaler MTP-Adapter
                 ↓
       Handgeschriebene Maschinenlogik
```

### 4.1 Komponenten

#### Visueller Editor

- Bausteinpalette
- Maschinen-Canvas
- Unit-Hierarchie
- Eigenschaftsinspektor
- Kommandoeditor
- Datenstruktur-Editor
- Verbindungseditor
- Validierungsanzeige
- Generierungsvorschau

#### Projektmodell

- normales, diffbares JSON
- versioniertes Schema
- stabile interne IDs
- getrennte Layout- und SPS-Daten
- keine TwinCAT-XML-Details in der Benutzeroberfläche

#### Generator-Kern

- unabhängig von der grafischen Oberfläche nutzbar
- über CLI und Editor aufrufbar
- deterministische TwinCAT-XML-Erzeugung
- sichere Verwaltung der `.plcproj`-Einträge
- Manifest und Hash-Prüfung

#### TwinCAT-Integration

- zunächst dateibasiert
- Automation Interface später optional
- realer XAE-Compile als gesonderte Validierungsstufe

## 5. Projektmodell v0.1

Die erste Projektdatei soll ein normales JSON-Dokument sein, beispielsweise:

```text
BrushMachine.etab.json
```

### 5.1 Projektweite Angaben

- Schema-Version
- Projektname
- SPS-Präfix, beispielsweise `BM`
- Namespace
- gewünschte ETAB-Version
- TwinCAT-Zielversion
- Zielprojekt beziehungsweise Ausgabeverzeichnis

### 5.2 Unit

Jede Unit erhält mindestens:

- stabile interne ID
- Anzeigename
- SPS-Name
- ETAB-Basistyp
- übergeordnete Unit
- Aktivierungsoptionen
- Kommandos
- Request-Felder
- Status-Felder
- Parameter
- optionale Kind-Units
- optionale MTP-Zuordnung

Vorgesehene ETAB-Bausteintypen für den MVP:

- `ApplicationUnit`
- `CommandUnit`
- `MachineLink`
- `RecipeManager`

Projektbezogene Spezialisierungen wie `MotionUnit`, `ProcessUnit` oder `WorkpieceUnit` sind zunächst benannte Ausprägungen einer `ApplicationUnit` beziehungsweise `CommandUnit`.

### 5.3 Kommandos

Ein Kommando enthält mindestens:

- stabile interne ID
- SPS-Name
- numerischer Enum-Wert (`enumValue`)
- Anzeigename
- Beschreibung
- zulässiger Unit-Typ
- optionaler MTP-Procedure-Bezug

Stabile Command-Modell-IDs sind global eindeutig. `enumValue`-Werte müssen innerhalb ihres Nodes eindeutig sein.

### 5.4 Beziehungen

Für die erste Version werden nur klar definierte Beziehungen zugelassen:

- `contains`: Unit enthält Kind-Unit
- `commands`: Unit sendet Requests an eine andere Unit
- `observes`: Unit liest Status einer anderen Unit
- `usesRecipe`: Unit verwendet einen RecipeManager
- `usesLink`: Unit verwendet einen MachineLink

Safety- oder Kollisionsfreigaben werden nicht als automatisch ausführbare Beziehung modelliert.

### 5.5 Layoutdaten

Position, Größe und Gruppierung eines Bausteins werden separat gespeichert. Änderungen am Canvas-Layout dürfen keinen SPS-Code-Diff verursachen.

## 6. Geplante Generatorausgabe

Beispielstruktur:

```text
Generated/
├─ DUTs/
│  ├─ E_BM_ProcessCommand.TcDUT
│  ├─ ST_BM_ProcessRequest.TcDUT
│  └─ ST_BM_ProcessStatus.TcDUT
├─ POUs/
│  ├─ FB_BM_ProcessUnitBase.TcPOU
│  └─ FB_BM_ProcessCommandRouter.TcPOU
├─ GVLs/
│  └─ GVL_BM_Units.TcGVL
└─ etab-generation-manifest.json

Application/
└─ FB_BM_ProcessUnit.TcPOU
```

### 6.1 Generierbare TwinCAT-Objekte

- Command-Enums
- Request-DUTs
- Status-DUTs
- Parameter-DUTs
- generierte Unit-Basisbausteine
- Command-Router
- optionale Interfaces
- Unit-Instanzen in einer GVL
- optionale PRG-Aufrufstruktur
- Ordner- und Compile-Einträge im `.plcproj`
- erforderliche ETAB-Library-Referenz
- später MTP-Adapterbausteine

### 6.2 Regenerationsgrenze

- `Generated/` gehört vollständig dem Generator.
- `Application/` gehört vollständig dem SPS-Entwickler.
- User-Dateien dürfen höchstens einmal als Startgerüst angelegt werden.
- Danach werden sie weder verändert noch gelöscht.
- Das Manifest enthält Modell-ID, Zielpfad, TwinCAT-GUID und Inhalts-Hash jeder generierten Datei.
- Manuelle Änderungen in generierten Dateien müssen vor dem Überschreiben erkannt und gemeldet werden.

## 7. Bedienkonzept des Editors

### 7.1 Linke Seite: Bausteinpalette

- Application Unit
- Command Unit
- Machine Link
- Recipe Manager
- später MTP Service und MTP Procedure

### 7.2 Mitte: Maschinen-Canvas

- Units platzieren
- Hierarchie darstellen
- Beziehungen verbinden
- Bausteine auswählen
- Gruppen beziehungsweise Maschinenbereiche bilden

Der MVP benötigt kein vollständig freies HMI-Zeichenprogramm. Ein klarer Node-/Baumeditor ist ausreichend.

### 7.3 Rechte Seite: Eigenschaften

- Name und SPS-Bezeichner
- ETAB-Basistyp
- Kommandos und IDs
- Request- und Statusfelder
- Parameter
- Kind-Units
- Generierungsoptionen
- spätere MTP-Zuordnung

### 7.4 Unterer Bereich: Generierung

- Validierungsergebnisse
- Liste der zu erzeugenden Dateien
- neue, geänderte und zu löschende Objekte
- Diff-Vorschau
- Warnungen vor manuellen Änderungen
- Generieren erst nach erfolgreicher Validierung

## 8. Geplante CLI

Der Generator-Kern soll auch ohne Editor nutzbar sein:

```text
etab validate BrushMachine.etab.json
etab preview  BrushMachine.etab.json
etab generate BrushMachine.etab.json
etab check    BrushMachine.etab.json
```

### Befehle

- `validate`: Schema, Namen, IDs und Beziehungen prüfen
- `preview`: geplante Änderungen ohne Schreiben anzeigen
- `generate`: bestätigte Ausgabe erzeugen
- `check`: prüfen, ob Modell und generierte Dateien synchron sind

## 9. Technologieempfehlung

### Generator-Kern

- C#/.NET-Klassenbibliothek
- separate CLI-Anwendung
- XML-Erzeugung ohne Text-Ersetzungsfragmente
- automatisierte Unit- und Snapshot-Tests

### Editor

- lokale Weboberfläche
- TypeScript
- SVG- oder Node-basierter Canvas
- Generator-Aufruf über einen lokalen .NET-Dienst
- spätere Verpackung als Desktop-Anwendung möglich

### TwinCAT-Anbindung

1. MVP: deterministische Dateierzeugung
2. danach: sichere `.plcproj`-Integration
3. optional: TwinCAT Automation Interface für XAE-Integration

Die grafische Oberfläche darf keine eigene, abweichende Generatorlogik enthalten. Editor und CLI verwenden denselben Generator-Kern.

## 10. Umsetzungsphasen

### Phase 0 – Spezifikation (abgeschlossen 2026-08-07, Architektur-Nachtrag 2026-08-10)

- [x] ETAB-Bausteinkatalog festlegen
- [x] JSON-Schema v0.1 entwerfen
- [x] Namens- und ID-Regeln festlegen
- [x] Generiert/User-Grenze verbindlich definieren
- [x] aktuelle Beispiel-Units als Referenz klassifizieren
- [x] Command-Enum-Wert eindeutig als `enumValue` vom Laufzeit-`nCommandID` abgrenzen
- [x] projektspezifischen Statusvertrag ohne Änderung der Library-DUTs festlegen
- [x] Vererbungs- und Hook-Muster für generierte Basis-FBs per TwinCAT-Compile-Spike prüfen

Abnahme: Das vorhandene Bürstautomatenmodell kann vollständig beschrieben werden, ohne Prozesscode in das Modell aufzunehmen.

Nachweis: `docs/Phase0_Validation.md`, `examples/BrushMachine.reference.etab.json` und `spikes/TwinCAT_BaseFb_Inheritance.md`.

### Phase 1 – Headless Generator-Kern (abgeschlossen 2026-08-10)

- [x] Projektmodell laden (Phase 1A, 2026-08-10)
- [x] Schema und Semantik validieren (Phase 1A, 2026-08-10)
- [x] stabile TwinCAT-GUIDs per UUID v5 ableiten und im Manifest verwalten
- [x] Command-, Request- und Status-DUTs sowie ApplicationUnit-Basis-FBs erzeugen
- [x] deterministisches Manifest mit semantischem Modellhash und Artefakthashes zuletzt schreiben
- [x] CLI mit `validate`, `preview`, `check` und konfliktgesperrtem `generate` vervollständigen
- [x] Snapshot-, Determinismus-, Änderungsplan-, Schreibgrenzen- und Rollback-Tests umsetzen

Nachweise: `docs/Phase1A_Validation.md`, `docs/Phase1B_Validation.md`, `docs/Phase1C_Validation.md` und `docs/Phase1_Validation.md`. `preview` und `check` bleiben read-only; ausschließlich der explizite Befehl `generate` schreibt in den konfigurierten Generatorbereich. `ET_AutomationBase` wird nicht verändert.

Abnahme:

- gleiche Eingabe erzeugt byte-identische Ausgabe
- doppelte stabile Command-IDs und doppelte `enumValue`-Werte je Node werden abgewiesen
- keine Datei außerhalb des Generatorbereichs wird verändert
- TwinCAT-XML lässt sich strukturell parsen

### Phase 2 – Visueller Editor MVP

- [ ] Projekt öffnen und speichern
- [ ] Bausteinpalette
- [ ] Maschinen-Canvas
- [ ] Unit-Auswahl und Eigenschaftsinspektor
- [ ] Kommandoeditor
- [ ] Request-/Statusfeldeditor
- [ ] Beziehungen
- [ ] Live-Validierung
- [ ] Generierungsvorschau

Abnahme: Die BrushMachine kann visuell modelliert, gespeichert, geschlossen und verlustfrei wieder geöffnet werden.

### Phase 3 – TwinCAT-Projektintegration

- [ ] ETAB-Library-Referenz verwalten
- [ ] `<Compile Include="…">` verwalten
- [ ] TwinCAT-Ordnerstruktur erzeugen
- [ ] GVL-Instanzen erzeugen
- [ ] optionale PRG-Aufrufstruktur erzeugen
- [ ] Umbenennen und Löschen generierter Objekte absichern
- [ ] Integration zunächst ausschließlich in einer Projektkopie testen

Abnahme:

- Projekt öffnet in TwinCAT ohne Strukturfehler
- wiederholte Generierung erzeugt keinen unnötigen Diff
- echter TwinCAT-Compile ist erfolgreich

### Phase 4 – Golden Sample `AutomationBase Beispiel`

- [ ] `FB_BM_Machine` modellieren
- [ ] `FB_BM_MotionUnit` modellieren
- [ ] `FB_BM_WorkpieceUnit` modellieren
- [ ] `FB_BM_ProcessUnit` modellieren
- [ ] vorhandene Request-, Command- und Status-DUTs vergleichen
- [ ] Struktur und öffentliche Schnittstellen gegen den handgeschriebenen Stand prüfen

MVP-Ende: Aus dem visuellen BrushMachine-Modell entsteht ein TwinCAT-kompilierbares ETAB-Grundgerüst, ohne Handcode zu überschreiben.

### Phase 5 – Optionale MTP-Erweiterung

- [ ] Unit als MTP-Service freigeben
- [ ] Procedures zu ETAB-Kommandos zuordnen
- [ ] Parameter und ReportValues abbilden
- [ ] MTP-Zustände auf ETAB-Abläufe abbilden
- [ ] nicht unterstützte Zustände explizit sperren oder implementieren
- [ ] Adapter außerhalb der TE8400-generierten Bereiche halten
- [ ] Regeneration von ETAB- und MTP-Seite testen

### Phase 6 – Produktreife

- [ ] Undo/Redo
- [ ] Copy/Paste
- [ ] wiederverwendbare Unit-Vorlagen
- [ ] Schema-Migrationen
- [ ] Import bestehender ETAB-Strukturen
- [ ] CI-`check`
- [ ] Installer oder portable Anwendung
- [ ] Benutzerdokumentation
- [ ] weitere Beispielprojekte

## 11. Validierungsstrategie

### Modelltests

- Pflichtfelder
- Namensregeln
- eindeutige IDs
- gültige Beziehungen
- keine Hierarchiezyklen
- gültige ETAB-Basistypen

### Generatortests

- deterministische Ausgabe
- stabile GUIDs
- sichere Umbenennung
- sichere Löschung
- Schutz vor Änderungen in generierten Dateien
- keine Änderungen an User-Dateien

### TwinCAT-Tests

1. XML-Strukturprüfung
2. Projekt in XAE öffnen
3. Library-Auflösung prüfen
4. echter TwinCAT-Compile
5. optional Simulation des Beispielprojekts

Compile, Simulation und Maschinenvalidierung sind getrennte Nachweise und dürfen nicht gleichgesetzt werden.

## 12. Hauptrisiken

### Überschreiben von Handcode

Gegenmaßnahme: harte Verzeichnisgrenze, Manifest, Hash-Prüfung und Vorschaupflicht.

### Instabile TwinCAT-GUIDs

Gegenmaßnahme: persistente Modell-IDs und GUID-Zuordnung im Manifest.

### Zu große erste Version

Gegenmaßnahme: MVP auf Unit-Hierarchie, Kommandos, Request/Status und Basisgerüste begrenzen.

### Vermischung von Editor und Generator

Gegenmaßnahme: ein gemeinsamer Generator-Kern für CLI und Benutzeroberfläche.

### Unklare Zustandsabbildung zwischen MTP und ETAB

Gegenmaßnahme: MTP erst nach stabilem ETAB-MVP ergänzen und Zustände explizit mappen.

### Falscher Sicherheitsanspruch

Gegenmaßnahme: Safety, Kollision und sichere Maschinenreaktionen bleiben außerhalb der automatischen Generierung.

## 13. Umsetzungsentscheidungen

Für Phase 1 verbindlich entschieden:

- Arbeitsname und Ablage: `ETAB Engineering` im Workspace-Unterordner `ETAB_Engineering_v0.1.0.0`.
- Command-Enum-Literal im Modell: `enumValue`; `nCommandID` bleibt ausschließlich die Laufzeit-ID einer Anforderung.
- Status-DUTs werden projektspezifisch generiert und betten vorhandene Library-Status-DUTs ein; `ET_AutomationBase` wird dafür nicht geändert.
- Basisbausteine folgen dem compilergeprüften Muster `User-FB -> Generated-Base-FB -> ETAB.FB_ETAB_ApplicationUnit` mit `SUPER^()` auf beiden Ebenen und geschützten Hooks.
- JSON-Schema, Benennung, ID-Regeln sowie Umbenennen und Löschen sind durch die Phase-0-Verträge festgelegt.

Nicht blockierend und in späteren Phasen zu entscheiden:

- genauer Umfang automatisch erzeugter GVL- und PRG-Strukturen in Phase 3,
- Canvas-Komponente in Phase 2,
- Automation Interface erst nach der dateibasierten Projektintegration,
- genaue MTP-Zustandsabbildung in Phase 5.

## 14. Erster Umsetzungsschnitt (abgeschlossen)

Der ursprünglich freigegebene Entwicklungsschnitt umfasste:

1. JSON-Schema v0.1
2. C#-Modellklassen
3. Validierung von Units, stabilen Command-IDs und `enumValue`-Werten
4. deterministische Erzeugung eines Command-Enums
5. deterministische Erzeugung eines Request- und Status-DUTs
6. Manifest mit stabilen IDs und Hashes
7. CLI-`preview` und CLI-`check`
8. Tests anhand einer vereinfachten `ProcessUnit`

Phase 1 hat diesen Schnitt vollständig umgesetzt und zusätzlich ApplicationUnit-Basis-FBs, den schreibenden CLI-Befehl `generate`, transaktionale Dateioperationen und Rollback ergänzt. Der Kern ist reproduzierbar und gemäß `docs/Phase1_Validation.md` abgenommen. Als nächster Umsetzungsschritt beginnt Phase 2 mit dem visuellen Editor.
