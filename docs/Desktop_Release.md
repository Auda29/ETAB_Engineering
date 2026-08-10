# Windows Desktop Release

## Scope

ETAB Engineering is packaged as a portable, self-contained Windows x64 application. The bundle contains:

- the WPF desktop host,
- Microsoft WebView2 Loader components,
- the production React editor under `wwwroot`,
- the reusable ASP.NET editor service,
- `ETAB.Engineering.Core`,
- the JSON schema and BrushMachine reference project,
- the .NET runtime required by the application.

The desktop host starts the service in the same process on a random HTTP loopback port and opens that origin in WebView2. Navigation to any other scheme, host, or port is blocked. Closing the desktop window stops the service.

Microsoft Edge WebView2 Runtime remains a target-system prerequisite. The package does not require the .NET SDK, Node.js, a terminal, or a separately started backend.

## Local Build

Run this command from the repository root:

```powershell
.\publish-win-x64.ps1 -Version 0.1.0.0
```

The script performs these checks before producing the release archive:

1. deterministic frontend dependency installation with `npm ci`,
2. the TypeScript check,
3. .NET restore and Release tests,
4. self-contained `win-x64` publish,
5. bundle-completeness checks,
6. a smoke test executed through the published `ETAB Engineering.exe`,
7. ZIP creation and SHA-256 generation.

The smoke test verifies that the packaged executable:

- serves the bundled React entry point,
- opens the bundled BrushMachine project,
- validates it through the shared core,
- produces a read-only preview containing 14 artifacts,
- saves and reopens a temporary project without data loss,
- shuts down its loopback service cleanly.

The output files are:

```text
artifacts/ETAB-Engineering-v0.1.0.0-win-x64.zip
artifacts/ETAB-Engineering-v0.1.0.0-win-x64.zip.sha256
```

The ZIP contains one root directory so it can be extracted without scattering files into the destination directory. Keep all files and directories next to the executable as shipped.

## GitHub Release Workflow

`.github/workflows/desktop-release.yml` runs on Windows and calls the same `publish-win-x64.ps1` script used locally.

- `workflow_dispatch` builds and verifies the ZIP and checksum without creating a Release.
- A pushed `v*` tag derives the package version from the tag, builds and verifies the bundle, and creates or updates the corresponding GitHub Release with the ZIP and checksum attached directly.
- The workflow additionally attempts to retain the two files as a workflow artifact. This copy is optional so an exhausted GitHub Actions artifact quota cannot block the authoritative Release assets.

For version `0.1.0.0`, publish with:

```powershell
git tag -a v0.1.0.0 -m "ETAB Engineering v0.1.0.0"
git push origin v0.1.0.0
```

Create the tag only after the release commit has been pushed and the local packaging script has completed successfully.

## Validation Record for v0.1.0.0

Local validation on 2026-08-10 produced a 76,249,798-byte ZIP with 564 archive entries. All required bundle entries were present under one root directory. The generated SHA-256 value was:

```text
70aaab1ed2636de7dbbad9d845b66314dcf749aac474afd05cb9811de330a9e5
```

The published executable smoke test passed with 14 preview artifacts and a lossless save/reopen round trip. The extracted WPF application also started successfully and visibly rendered the complete BrushMachine editor with 7 nodes, 12 relationships, and a valid model.

This evidence proves the local packaging and application boundary. It does not prove a TwinCAT XAE open, PLC compile, PLC simulation, or machine test.
