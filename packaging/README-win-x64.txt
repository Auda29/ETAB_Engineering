ETAB Engineering {VERSION}
===========================

This is the self-contained Windows x64 desktop bundle.

Start
-----

1. Extract the complete ZIP archive.
2. Start "ETAB Engineering.exe".
3. Keep the schemas, examples, and wwwroot folders next to the executable.

No .NET SDK, Node.js installation, terminal, or separately started service is
required. Microsoft Edge WebView2 Runtime is required. It is preinstalled on
Windows 11 and most supported Windows 10 systems.

Projects
--------

The bundled BrushMachine reference project opens automatically. Save edited
projects under a new *.etab.json path so the reference model remains unchanged.

The application binds its internal service exclusively to a random local
loopback port and stops that service when the desktop window closes.

Validation boundary
-------------------

The editor and CLI use ETAB.Engineering.Core for schema validation, semantic
validation, and generation preview. This bundle does not prove a TwinCAT XAE
open, PLC compile, simulation, or machine test.
