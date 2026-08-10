# ETAB-Projektmodell – Spezifikation v0.1

## 1. Geltungsbereich

Diese Spezifikation definiert das persistente Projektmodell für den ersten ETAB-Engineering-MVP. Die normative Maschinenstruktur wird in einer JSON-Datei mit der Endung `.etab.json` gespeichert.

Das Modell beschreibt SPS-Verträge und logische Beziehungen. Es beschreibt keine ausführbare Safety-, Bewegungs- oder Prozesslogik.

Normative Strukturprüfung: `../schemas/etab-project.schema.json`.

## 2. Grundstruktur

```json
{
  "schemaVersion": "0.1",
  "project": {},
  "nodes": [],
  "relations": [],
  "layout": { "nodes": [] }
}
```

### Bereiche

- `project`: projektweite Namens-, Versions- und Ausgabeeinstellungen.
- `nodes`: ETAB-Komponenten und projektspezifische Rollen.
- `relations`: normative logische Verbindungen.
- `layout`: ausschließlich visuelle Position und Größe.

Änderungen unter `layout` dürfen keine Änderung der SPS-Ausgabe verursachen.

## 3. Identitäten

### 3.1 Modell-ID

- Jede persistente Entität besitzt eine UUID.
- UUIDs werden beim Anlegen zufällig erzeugt und danach nicht mehr geändert.
- Eine Umbenennung ändert keine Modell-ID.
- IDs werden nicht aus Namen oder Positionen abgeleitet.
- IDs werden im JSON kleingeschrieben gespeichert.

### 3.2 TwinCAT-Objekt-ID

Die TwinCAT-GUID eines generierten Objekts wird deterministisch als UUID v5 aus folgenden Bestandteilen abgeleitet:

1. fester ETAB-Engineering-Generator-Namespace,
2. Projekt-ID,
3. Modell-ID,
4. Artefaktart, beispielsweise `command-enum` oder `request-dut`.

Damit bleibt die TwinCAT-GUID bei Umbenennungen und auf unterschiedlichen Engineering-Rechnern stabil. Das Generierungsmanifest protokolliert die abgeleitete GUID, ist aber nicht deren einzige Quelle.

Fester UUID-v5-Namespace für ETAB Engineering v0.x:

```text
8d487292-cc21-4f2e-8c6e-3c4742e1d8a1
```

Der zu hashende Name wird UTF-8-codiert und in dieser Form aufgebaut:

```text
<project-id>/<model-id>/<artifact-kind>
```

## 4. Namen

### 4.1 `project.prefix`

- Großbuchstaben und Ziffern,
- beginnt mit einem Großbuchstaben,
- zwei bis sechzehn Zeichen,
- Beispiel: `BM`.

### 4.2 SPS-Namen

- IEC-Bezeichner ohne Leerzeichen,
- beginnen mit Buchstabe oder Unterstrich,
- enthalten nur Buchstaben, Ziffern und Unterstriche,
- bevorzugt PascalCase für FBs und Typstämme.

### 4.3 Node-Namen

- `name`: Name des Funktionsbausteins ohne `FB_<Prefix>_`, beispielsweise `MotionUnit`.
- `symbolStem`: Stamm für Command-, Request- und Status-DUT, beispielsweise `Motion`.
- `displayName`: frei lesbarer Anzeigename.
- `role`: semantische Projektrolle wie `machine`, `motion`, `workpiece`, `process` oder `orchestrator`.

### 4.4 Generierte Namen

Für Präfix `BM`, Node-Name `MotionUnit` und `symbolStem` `Motion`:

| Artefakt | Name |
|---|---|
| Command-Enum | `E_BM_MotionCommand` |
| Request-DUT | `ST_BM_MotionRequest` |
| Status-DUT | `ST_BM_MotionStatus` |
| Basis-FB | `FB_BM_MotionUnitBase` |
| Command-Router (geplant ab Phase 3) | `FB_BM_MotionCommandRouter` |

Namenskollisionen werden als Validierungsfehler behandelt.

## 5. Nodes

Gültige `kind`-Werte in v0.1:

- `applicationUnit`
- `commandUnit`
- `recipeManager`
- `machineLink`

Spezialisierungen werden über `role` beschrieben und erzeugen keinen neuen Library-Basistyp.

### 5.1 Generierungsoptionen

Jeder Node definiert explizit:

- `commandEnum`
- `requestType`
- `statusType`
- `baseFunctionBlock`
- `instance` (im Modell gespeichert; Instanzerzeugung erst ab Phase 3)

Nicht sinnvolle Kombinationen werden semantisch abgewiesen. Ein `recipeManager` erzeugt beispielsweise im MVP kein projektspezifisches Command-Enum.

### 5.2 `applicationUnit`

Kann folgende Einstellungen tragen:

- Start-, Homing- und Stop-Modus,
- Remote-Control-Verhalten,
- Fehlerübernahme in den Unit-Fehlerhandler,
- Startzustand und Reset-on-Start des internen Command Handlers.

### 5.3 `commandUnit`

Kann Startzustand und Reset-on-Start konfigurieren. Die eigentliche Sequenzimplementierung ist nicht Bestandteil des Modells.

### 5.4 `recipeManager`

Referenziert den projektspezifischen Rezeptdatentyp und beschreibt Datei-/XPath-Vorgaben. Pointer, Speichergröße und fachliche Validierung bleiben Projektcode.

### 5.5 `machineLink`

Beschreibt Bridge-Typ, Rolle, Watchdog und Protokolloptionen. Hardwareadressen bleiben außerhalb des Modells.

## 6. Kommandos

Ein Kommando enthält:

- stabile `id`,
- `name`,
- numerischen `enumValue`,
- lesbaren Namen und optionale Beschreibung,
- Abbildung auf ein `ETAB.E_ETAB_UnitCommand`.

Gültige ETAB-Ziele:

- `NoAction`
- `Reset`
- `Start`
- `Homing`
- `Stop`
- `Abort`
- `Clear`
- `User`

### Semantische Regeln

- Stabile Command-`id` ist global eindeutig; `name` und `enumValue` sind innerhalb eines Nodes eindeutig.
- Wenn `commandEnum = true`, existiert genau ein `NoAction` mit Wert `0`.
- Projektspezifische Typed Commands werden in v0.1 grundsätzlich auf `User` abgebildet.
- Eine direkte Abbildung auf `Stop` oder `Abort` ist nur für echte ETAB-Unit-Kommandos vorgesehen.
- Numerische Werte werden bei der Generierung aufsteigend sortiert; bei Gleichstand ist das Modell ungültig.

## 7. Request- und Status-Payload

### 7.1 Impliziter Request-Kopf

Bei `requestType = true` erzeugt der Generator automatisch:

```iecst
bExecute   : BOOL;
eCommand   : <generiertes Command-Enum>;
nCommandID : UDINT;
```

Diese Felder dürfen deshalb nicht noch einmal unter `requestPayload` definiert werden.

### 7.2 Generierter Statusvertrag

Bei `statusType = true` erzeugt der Generator einen projektspezifischen Status-DUT. Er ändert oder dupliziert keine DUT-Definition der `ET_AutomationBase`-Library, sondern bindet deren öffentlichen Status als Feld ein und ergänzt ausschließlich die unter `statusPayload` beschriebenen Projektfelder.

Für eine `applicationUnit` lautet der feste Kopf:

```iecst
stUnit : ETAB.ST_ETAB_ApplicationUnitStatus;
```

Wenn die Unit zusätzlich ein projektspezifisches Command-Enum und einen Request-Vertrag besitzt, kommt der Status des fachlichen Kommandos getrennt hinzu:

```iecst
stOperation : ETAB.ST_ETAB_CommandStatus;
```

`stUnit.stCommand` bleibt der Lifecycle-/Unit-Command-Status der Library. `stOperation` gehört dagegen zum projektspezifischen Kommando wie `HomeAll`, `MeasureLength` oder `ParkAll`.

Für die übrigen Node-Arten verwendet der Generator folgende feste Köpfe:

| Node-Art | eingebetteter Library-Status |
|---|---|
| `commandUnit` | `stCommand : ETAB.ST_ETAB_CommandStatus` |
| `recipeManager` | `stRecipe : ETAB.ST_ETAB_RecipeStatus` |
| `machineLink` | `stLink : ETAB.ST_ETAB_MachineLinkStatus` |

Die Namen `stUnit`, `stOperation`, `stCommand`, `stRecipe` und `stLink` sind abhängig von der Node-Art reserviert und dürfen im `statusPayload` nicht erneut vorkommen.

Beispiel:

```iecst
TYPE ST_BM_MotionStatus :
STRUCT
    stUnit      : ETAB.ST_ETAB_ApplicationUnitStatus;
    stOperation : ETAB.ST_ETAB_CommandStatus;
    bAllHomed   : BOOL;
    bAllSafe    : BOOL;
END_STRUCT
END_TYPE
```

Diese Struktur wird im Zielprojekt unter `Generated/` erzeugt. Dafür ist keine Änderung und keine neue Version der `ET_AutomationBase`-Library erforderlich.

### 7.3 Payload-Feld

Ein Feld enthält:

- stabile ID,
- IEC-Name,
- TwinCAT-Datentyp,
- optionale Arraydimensionen,
- optionale Beschreibung,
- optionalen Initialwert als TwinCAT-Literal.

Arrays werden aus Basistyp und `arrayDimensions` aufgebaut. Beispiel:

```json
{
  "name": "aBrushSpeed",
  "dataType": "LREAL",
  "arrayDimensions": [{ "lower": 1, "upper": 3 }]
}
```

Generiertes ST:

```iecst
aBrushSpeed : ARRAY[1..3] OF LREAL;
```

## 8. Beziehungen

Gültige Relationstypen:

| Typ | Bedeutung |
|---|---|
| `contains` | hierarchische Master-/Subunit-Zuordnung |
| `commands` | Quelle erzeugt Requests für Ziel |
| `observes` | Quelle liest Status des Ziels |
| `usesRecipe` | Quelle verwendet einen RecipeManager |
| `usesLink` | Quelle verwendet einen MachineLink |

### Semantische Regeln

- Quelle und Ziel müssen existieren.
- Selbstbeziehungen sind unzulässig.
- `contains` darf keine Zyklen bilden.
- Ein Node besitzt höchstens einen Parent über `contains`.
- Ziel von `usesRecipe` ist ein `recipeManager`.
- Ziel von `usesLink` ist ein `machineLink`.
- Ziel von `commands` ist eine `applicationUnit` oder `commandUnit`.

Safety- und Kollisionsfreigaben sind kein Relationstyp v0.1.

## 9. Layout

Das Layout referenziert Nodes ausschließlich über `nodeId` und speichert:

- `x`, `y`,
- optionale Breite und Höhe,
- optionale Gruppierung.

Ein Node darf höchstens einen Layouteintrag besitzen. Fehlendes Layout beeinflusst die Modellgültigkeit nicht; der Editor darf dann automatisch positionieren.

## 10. MTP-Vorbereitung

Ein Node kann optional einen `mtp`-Block tragen:

- `exposed`,
- Service-Name,
- Procedures mit stabiler ID, Procedure-ID und referenzierter Command-ID.

In v0.1 werden diese Angaben gespeichert und validiert, aber noch nicht generiert. Die Zustandsabbildung wird erst in Phase 5 verbindlich implementiert.

## 11. Deterministische Reihenfolge

Der Generator verwendet folgende Sortierung:

- Nodes: `name`, danach `id`.
- Commands: `enumValue`, danach `name`, danach `id`.
- Felder: Reihenfolge im Modell ist SPS-Reihenfolge und damit semantisch.
- Beziehungen: `kind`, `sourceNodeId`, `targetNodeId`, `id`.
- Layout ist von der SPS-Generierung ausgeschlossen.

## 12. Validierung außerhalb des JSON-Schemas

Der in Phase 1 implementierte semantische Validator prüft zusätzlich:

- globale Eindeutigkeit aller IDs,
- referenzielle Integrität,
- Namenskollisionen generierter Artefakte,
- Eindeutigkeit stabiler Command-IDs und der `enumValue`-Werte je Node,
- Arraygrenzen,
- Relationstypen passend zum Node-Kind,
- zyklusfreie Hierarchie,
- gültige Generatoroptionen je Node-Kind,
- keine Kollision von Status-Payload-Feldern mit den für die Node-Art reservierten Library-Statusfeldern,
- MTP-Procedure-ID und Command-Referenzen.

## 13. Einwegprinzip des MVP

Im MVP gilt ausschließlich:

```text
.etab.json → generierte TwinCAT-Objekte
```

Manuell veränderter SPS-Code wird nicht zurück in das Modell importiert. Ein späterer Import vorhandener Projekte ist eine eigene Funktion und kein Roundtrip des Generators.
