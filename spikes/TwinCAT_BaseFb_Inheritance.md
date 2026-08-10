# TwinCAT-Spike: generierter Basis-FB und Benutzer-FB

## Ziel

Der Spike prüft das geplante Eigentumsmuster für Unit-Bausteine:

```text
FB_ETABENG_UserUnit
  -> SUPER^()
FB_ETABENG_GeneratedUnitBase
  -> SUPER^()
ETAB.FB_ETAB_ApplicationUnit
```

Der generierte Basis-FB gehört zu `Generated/`. Der abgeleitete Benutzer-FB gehört zu `Application/` und wird bei einer Regenerierung nicht verändert.

## Compile-Host

Die drei Spike-POUs sind im vorhandenen Projekt `AutomationBase_Beispiel.plcproj` unter `POUs/Spikes/ETABEngineering/` eingebunden:

- `FB_ETABENG_GeneratedUnitBase`: generatorverwaltete Zwischenschicht mit `SUPER^()` und geschütztem Hook `OnExecuteOperation`.
- `FB_ETABENG_UserUnit`: benutzerverwaltete Ableitung, die ebenfalls `SUPER^()` aufruft und den Hook überschreibt.
- `FB_ETABENG_BaseFbInheritanceSpike`: nicht instanziierter Testtreiber mit Zählern für Aufrufkette und Hook-Dispatch.

Keine Spike-POU ist einer Task zugeordnet oder wird von `MAIN` instanziiert. Der Spike verändert deshalb kein Laufzeitverhalten des BrushMachine-Beispiels.

## Abnahmestufen

1. XML-Strukturprüfung aller drei `.TcPOU`-Dateien und der `.plcproj`.
2. TwinCAT-Compile des vorhandenen Projekts.
3. Optionaler späterer Online-/Simulationstest durch gezielte Instanziierung des Testtreibers.

Ein erfolgreicher Compile weist die gültige Vererbung, geerbte Ein-/Ausgänge, die zweistufige `SUPER^()`-Kette und die zulässige Hook-Überschreibung nach. Er beweist noch nicht die Laufzeitwerte der Zähler. Diese werden erst durch den optionalen Online-/Simulationstest nachgewiesen.

## Ergebnis

Ausgeführt am 2026-08-10 mit der lokal installierten Beckhoff TwinCAT XAE Shell über `TcXaeShell.DTE.15.0`:

```text
Solution:      AutomationBase_Beispiel.sln
Configuration: Release | TwinCAT RT (x64)
LastBuildInfo: 0
Result:        COMPILE_SUCCESS
```

Damit sind die zweistufige `SUPER^()`-Kette, die geerbten Ein-/Ausgänge und die geschützte Hook-Überschreibung compilerseitig bestätigt. Der Testtreiber wurde nicht einer Task zugeordnet; Hook-Dispatch und Zählerwerte wurden deshalb nicht online oder in einer Simulation ausgeführt.
