# Windows Desktop Release

## Scope

ETAB Engineering is packaged both as a guided Windows x64 installer and as a portable, self-contained ZIP. The application payload contains:

- the WPF desktop host,
- Microsoft WebView2 Loader components,
- the production React editor under `wwwroot`,
- the reusable ASP.NET editor service,
- `ETAB.Engineering.Core`,
- the JSON schema plus the BrushMachine reference and external-ownership integration projects,
- the .NET runtime required by the application.

The desktop host starts the service in the same process on a random HTTP loopback port and opens that origin in WebView2. Navigation to any other scheme, host, or port is blocked. Closing the desktop window stops the service.

Microsoft Edge WebView2 Runtime remains a target-system prerequisite. Setup detects the runtime and, only if it is missing, invokes the included Microsoft Evergreen bootstrapper. The portable ZIP expects the runtime to be installed already. Neither distribution requires the .NET SDK, Node.js, a terminal, or a separately started backend.

## Local Build

Install Inno Setup 7, then run this command from the repository root:

```powershell
.\publish-installer-win-x64.ps1 -Version 0.1.0.2
```

The release script calls `publish-win-x64.ps1` and performs these checks before producing the four release files:

1. deterministic frontend dependency installation with `npm ci`,
2. the TypeScript check,
3. .NET restore and Release tests,
4. self-contained `win-x64` publish,
5. bundle-completeness checks,
6. a smoke test executed through the published `ETAB Engineering.exe`,
7. ZIP creation and SHA-256 generation,
8. download and Authenticode verification of Microsoft's WebView2 Evergreen bootstrapper,
9. Inno Setup compilation,
10. isolated silent installation,
11. smoke testing through the installed executable,
12. silent uninstallation plus file and registration cleanup checks.

The smoke test verifies that the packaged executable:

- serves the bundled React entry point,
- opens the bundled BrushMachine project,
- validates it through the shared core,
- produces a read-only preview containing 15 artifacts,
- saves and reopens a temporary project without data loss,
- shuts down its loopback service cleanly.

The output files are:

```text
artifacts/ETAB-Engineering-v0.1.0.2-win-x64.zip
artifacts/ETAB-Engineering-v0.1.0.2-win-x64.zip.sha256
artifacts/ETAB-Engineering-v0.1.0.2-win-x64-setup.exe
artifacts/ETAB-Engineering-v0.1.0.2-win-x64-setup.exe.sha256
```

The ZIP contains one root directory so it can be extracted without scattering files into the destination directory. Keep all files and directories next to the executable as shipped.

## Installer Behavior

- Setup defaults to a per-user installation and therefore normally needs no administrator elevation.
- An administrator can select an all-users installation through the standard privilege override.
- A Start menu shortcut is installed; a desktop shortcut is optional and unchecked by default.
- The application is registered in Windows Apps and Features and can be removed completely through its uninstaller.
- Setup installs WebView2 Runtime only when registry detection reports that it is missing.
- Silent installation uses the standard Inno Setup flags, for example `setup.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-`.

The release Setup EXE is not currently Authenticode-signed. Windows can therefore show an unknown-publisher or SmartScreen warning until a release code-signing certificate is configured. The embedded Microsoft WebView2 bootstrapper is separately verified as valid Microsoft-signed code during every build.

## GitHub Release Workflow

`.github/workflows/desktop-release.yml` runs on Windows and calls the same `publish-installer-win-x64.ps1` script used locally. The workflow installs a pinned official Inno Setup compiler only after validating its publisher signature.

- `workflow_dispatch` builds and verifies the four files without creating a Release.
- A pushed `v*` tag derives the package version from the tag, builds and verifies both distributions, and creates or updates the corresponding GitHub Release with all four files attached directly.
- The workflow additionally attempts to retain the files as a workflow artifact. This copy is optional so an exhausted GitHub Actions artifact quota cannot block the authoritative Release assets.

For version `0.1.0.2`, publish with:

```powershell
git tag -a v0.1.0.2 -m "ETAB Engineering v0.1.0.2"
git push origin v0.1.0.2
```

Create the tag only after the release commit has been pushed and the local packaging script has completed successfully.

## Validation Record for v0.1.0.0

Local validation on 2026-08-13 produced a 76,249,815-byte ZIP with 564 archive entries. All required bundle entries were present under one root directory. The generated ZIP SHA-256 value was:

```text
3c68250dae4aef96cc19b57889a2d3eca34c98ecdbf550de723e7c477e8e21e4
```

The current executable smoke test passes with 15 preview artifacts, including the generated instance GVL, and a lossless save/reopen round trip. The previously extracted WPF application also started successfully and visibly rendered the complete BrushMachine editor with 7 nodes, 12 relationships, and a valid model.

The installer build produced a 55,798,857-byte Setup EXE with this SHA-256 value:

```text
71bbe356e6a50c198ea96b2569bced081bbe76d279b52ec37284951715216270
```

The installer smoke test installed the application into an isolated per-user temporary directory, ran the same packaged desktop smoke test successfully, and uninstalled it again. The application directory and all checked per-user and per-machine uninstall registrations were absent afterward. No interactive editor window or Playwright test was used.

This evidence proves the local packaging and application boundary. It does not prove a TwinCAT XAE open, PLC compile, PLC simulation, or machine test.

## Validation Record for v0.1.0.1

Local validation on 2026-08-13 passed all 54 core tests, all 7 editor-service tests, the TypeScript check, the complete Release build, and the published desktop smoke test. The final portable bundle contains 565 archive entries, including both BrushMachine example models and the generated integration workflow inputs. The 76,280,735-byte ZIP has this SHA-256 value:

```text
36411060bbedadb65ffbd6e1de90e098ef2972871dae6a6000a265756df0c66b
```

The installer build produced a 55,831,231-byte Setup EXE with this SHA-256 value:

```text
5b3290d096ac3bd5b405cbd93cdcb16d790bdbefad49f86e738f940f103b928c
```

Both checksum sidecars match their release files. The installer smoke test installed the application into an isolated per-user temporary directory, executed the packaged desktop smoke test successfully, and removed the installed application and checked registrations again. The release staging directories were empty afterward. The Setup EXE remains intentionally unsigned as documented above.

No interactive editor, Playwright, or TwinCAT XAE process was started for this validation. XAE open, PLC compile, simulation, and machine acceptance remain explicitly assigned to the manual engineering test.

## Validation Record for v0.1.0.2

Local validation on 2026-08-13 passed the TypeScript check, all 54 core tests, all 8 editor-service tests, the complete Release publish, and the packaged desktop smoke test. The service tests cover the Core-validated minimal New Project template with fresh stable IDs. The desktop smoke test additionally verifies that native file-dialog support is registered in the packaged host.

The portable bundle contains 565 archive entries. The 76,287,455-byte ZIP has this SHA-256 value:

```text
24d3525567d572d8f3cc0d6d80986fc469f969dae97eff16546e7187b38d0e7c
```

The installer build produced a 55,834,848-byte Setup EXE with this SHA-256 value:

```text
ed868602e79b3994aa8423cb1a02b930e5fb95965e7f0c889d31042004a0fa96
```

Both checksum sidecars match their release files. The isolated installer test installed the application into a temporary per-user directory, ran the packaged desktop smoke test, uninstalled the application, and verified cleanup. No interactive editor window, browser, or Playwright test was used. Interactive acceptance of the native New, Open, Save, and Save As workflow remains a separate UI-validation step.
