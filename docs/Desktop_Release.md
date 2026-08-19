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
.\publish-installer-win-x64.ps1 -Version 0.1.0.7-preview.7
```

This local command creates unsigned development artifacts because no private signing identity is stored in the repository. Stable public releases are built and signed in GitHub Actions. An explicitly versioned `-preview.N` prerelease may remain unsigned while signing is deferred, but it is marked accordingly in GitHub and can trigger a Windows unknown-publisher warning. The two-phase options on `publish-win-x64.ps1` exist so CI can preserve the unpacked bundle for optional signing and package that exact bundle afterward.

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
- produces a read-only preview containing 16 artifacts,
- saves and reopens a temporary project without data loss,
- shuts down its loopback service cleanly.

The output files are:

```text
artifacts/ETAB-Engineering-v0.1.0.7-preview.7-win-x64.zip
artifacts/ETAB-Engineering-v0.1.0.7-preview.7-win-x64.zip.sha256
artifacts/ETAB-Engineering-v0.1.0.7-preview.7-win-x64-setup.exe
artifacts/ETAB-Engineering-v0.1.0.7-preview.7-win-x64-setup.exe.sha256
```

The ZIP contains one root directory so it can be extracted without scattering files into the destination directory. Keep all files and directories next to the executable as shipped.

## Installer Behavior

- Setup defaults to a per-user installation and therefore normally needs no administrator elevation.
- An administrator can select an all-users installation through the standard privilege override.
- A Start menu shortcut is installed; a desktop shortcut is optional and unchecked by default.
- The application is registered in Windows Apps and Features and can be removed completely through its uninstaller.
- Setup installs WebView2 Runtime only when registry detection reports that it is missing.
- Silent installation uses the standard Inno Setup flags, for example `setup.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-`.

Every stable GitHub Release produced by the configured workflow contains an Authenticode-signed application executable and Setup EXE. Both signatures use a SHA-256 file digest and the Microsoft Artifact Signing RFC 3161 timestamp service. The workflow rejects a missing, invalid, or non-timestamped signature for stable versions. While signing is deferred, only an explicit `vX.Y.Z.W-preview.N` tag may take the unsigned path; it is published as a GitHub prerelease with an unknown-publisher warning. Previously published unsigned assets are not modified retroactively. The embedded Microsoft WebView2 bootstrapper is separately verified as valid Microsoft-signed code during every build.

Authenticode establishes the verified publisher and file integrity. A newly established publisher can still receive a temporary SmartScreen reputation warning until Microsoft has accumulated sufficient reputation for the signing identity and downloads. Every release must therefore use the same public-trust certificate profile.

## Artifact Signing Setup

The workflow uses [Microsoft Artifact Signing](https://learn.microsoft.com/azure/artifact-signing/overview) with GitHub OpenID Connect. No client secret, certificate file, PFX password, or private key is stored in GitHub.

Perform this account setup once before running the updated workflow:

1. In Azure, create an Artifact Signing account in a supported region, complete public-trust identity validation, and create a public-trust certificate profile.
2. Create or select a Microsoft Entra application/service principal for GitHub Actions.
3. Add an OIDC federated credential whose subject is `repo:Auda29/ETAB_Engineering:environment:release-signing`. The workflow deliberately uses the `release-signing` GitHub environment so manual runs and all version tags share one narrowly scoped subject.
4. Assign that principal the **Artifact Signing Certificate Profile Signer** role for the configured signing account/profile.
5. Create the `release-signing` environment in the GitHub repository. Protect it with required reviewers or deployment restrictions if appropriate.
6. Add these environment secrets:

   - `AZURE_CLIENT_ID`
   - `AZURE_TENANT_ID`
   - `AZURE_SUBSCRIPTION_ID`

7. Add these environment variables:

   - `AZURE_ARTIFACT_SIGNING_ENDPOINT`, for example the endpoint shown on the Azure account such as `https://weu.codesigning.azure.net/`
   - `AZURE_ARTIFACT_SIGNING_ACCOUNT_NAME`
   - `AZURE_ARTIFACT_SIGNING_CERTIFICATE_PROFILE_NAME`

The endpoint must match the Azure region that contains the account and certificate profile. A partial configuration always fails closed. When all six settings are absent, only a `-preview.N` version may continue unsigned; a stable version is rejected. Once all values are present, signing and signature verification are mandatory for every run.

The GitHub configuration can be entered through **Settings → Environments → release-signing**. Keep these values out of tracked files. OIDC removes the need for an `AZURE_CLIENT_SECRET`.

## GitHub Release Workflow

`.github/workflows/desktop-release.yml` runs on Windows and installs an official Inno Setup compiler only after validating its publisher signature. Its release sequence is:

1. build, test, and smoke-test an unpacked portable bundle,
2. validate the release class and Artifact Signing configuration,
3. for configured signing, authenticate through GitHub OIDC, sign `ETAB Engineering.exe`, and require its valid timestamped signature before ZIP creation,
4. build the installer exclusively from the verified ZIP,
5. for configured signing, sign the final Setup EXE and require its valid timestamped signature,
6. run the isolated installer test and regenerate the Setup checksum after optional preview signing,
8. verify both SHA-256 sidecars before any release upload.

- `workflow_dispatch` builds and verifies the four files without creating a Release. Stable versions require signing; explicit preview versions may run unsigned.
- A pushed `v*` tag derives the package version from the tag, builds and verifies both distributions, and creates or updates the corresponding GitHub Release with all four files attached directly. An unsigned preview is marked as a prerelease and carries a warning in its release notes.
- The workflow additionally attempts to retain the files as a workflow artifact. This copy is optional so an exhausted GitHub Actions artifact quota cannot block the authoritative Release assets.

For preview version `0.1.0.7-preview.7`, publish with:

```powershell
git tag -a v0.1.0.7-preview.7 -m "ETAB Engineering v0.1.0.7-preview.7"
git push origin v0.1.0.7-preview.7
```

Create the tag only after the release commit has been pushed and the local packaging script has completed successfully.

## Validation Record for v0.1.0.0

Local validation on 2026-08-13 produced a 76,249,815-byte ZIP with 564 archive entries. All required bundle entries were present under one root directory. The generated ZIP SHA-256 value was:

```text
3c68250dae4aef96cc19b57889a2d3eca34c98ecdbf550de723e7c477e8e21e4
```

The current executable smoke test expects 16 preview artifacts, including the generated instance GVL and relation adapter, and a lossless save/reopen round trip. The previously extracted WPF application also started successfully and visibly rendered the complete BrushMachine editor with 7 nodes, 12 relationships, and a valid model.

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

## Validation Record for v0.1.0.3

Local validation on 2026-08-14 passed the TypeScript check, all 57 core tests, all 8 editor-service tests, `dotnet format --verify-no-changes`, the complete Release build, and the packaged desktop smoke test. The final local 78,801,696-byte portable release candidate verified a lossless save/reopen round trip plus a 15-artifact BrushMachine preview. Its local SHA-256 value is:

```text
9b086b056c7fc6c92f9de8c871c3094cad3330c5c921b505f99ebb98fef9e6b5
```

The user then performed the interactive acceptance with a newly created project. The direct canvas workflow successfully created directed `commands`, `observes`, and `usesRecipe` relationships; the relation legend and inspector list rendered correctly; the model remained valid; and confirmed generation completed with 21 created artifacts. This manual acceptance used the packaged desktop editor and no Playwright automation.

The `v0.1.0.3` tag workflow builds the final portable ZIP and installer from the release commit, repeats the complete automated suite and isolated installer smoke test, and publishes the authoritative final assets and checksum sidecars to the GitHub Release.

## Validation Record for v0.1.0.4

Local validation on 2026-08-14 passed the TypeScript check, all 59 core tests, all 8 editor-service tests, the complete Release publish, and the packaged desktop smoke test. The release includes drag-and-drop node placement, node context actions for relationships and commands, persistent machine areas with cross-area relationships, canvas-only zoom, and saved dark/light themes.

The 78,808,978-byte portable ZIP completed the 15-artifact BrushMachine preview and lossless save/reopen smoke test. Its SHA-256 value is:

```text
deca3c800df9e131596c5820752670b8be28a4d0a0ce16d8b082042f46c09d51
```

The installer build produced a 55,847,795-byte Setup EXE with this SHA-256 value:

```text
98a7a76c83e9ff1f271cad37a72cb12933d6ebca0e0dfffb51ec8e737d1b6164
```

Both checksum sidecars were generated with the release files. The isolated installer test installed the application into a temporary per-user directory, ran the packaged desktop smoke test successfully, uninstalled the application, and verified cleanup. No interactive browser, editor window, or Playwright automation was used. TwinCAT XAE open, PLC compile, simulation, and machine acceptance remain separate manual engineering tests.

The `v0.1.0.4` tag workflow builds the final portable ZIP and installer from the release commit, repeats the complete automated suite and isolated installer smoke test, and publishes the authoritative final assets and checksum sidecars to the GitHub Release.

## Validation Record for v0.1.0.5

Local validation on 2026-08-14 passed the TypeScript check, all 59 core tests, all 8 editor-service tests, the complete Release publish, and the packaged desktop smoke test. This release exposes rename and remove actions directly on every named area folder in the project tree. Renaming preserves the stable internal area name, while removing an area keeps all nodes and relationships and moves its nodes to **Unassigned** after confirmation. The inline controls support both dark and light themes; **Unassigned** itself is intentionally not editable.

The 78,809,587-byte portable ZIP completed the 15-artifact BrushMachine preview and lossless save/reopen smoke test. Its local SHA-256 value is:

```text
1f67b23d56d69d032ffa396c227fde95d804c4c8603a8883afa728fad4592136
```

The installer build produced a 55,855,364-byte Setup EXE with this local SHA-256 value:

```text
3caa85030f8cd7e3aead96deb98474335ea5edf9718c187744b32c834fdf72d4
```

Both checksum sidecars were generated with the release files. The isolated installer test installed the application into a temporary per-user directory, ran the packaged desktop smoke test successfully, uninstalled the application, and verified cleanup. No interactive browser, editor window, or Playwright automation was used.

The `v0.1.0.5` tag workflow builds the authoritative portable ZIP and installer from the release commit, repeats the automated suite and isolated installer smoke test, and publishes both distributions plus their checksum sidecars to the GitHub Release.

## Validation Record for v0.1.0.6

Local validation on 2026-08-14 passed the TypeScript check, all 59 Core tests, all 10 editor-service tests, and the complete Release build with zero warnings and zero errors. This release introduces the TwinCAT-first startup workflow: selecting an empty `.plcproj` creates or reopens its deterministic companion ETAB model, assigns paths without manual filename entry, and binds project integration automatically. Generated artifacts are written directly into the PLC project's `DUTs`, `POUs`, and `GVLs` hierarchy while ownership remains limited to manifest-listed files. The same release adds node renaming through the canvas context menu.

The automated service workflow creates an empty PLC project, connects it, previews and executes direct-root generation, verifies the `.plcproj` entries, preserves an unrelated handwritten file, and proves that reconnecting keeps stable model IDs. No browser automation or Playwright run was used. The `v0.1.0.6` tag workflow performs the authoritative portable bundle, installer, isolated installation, packaged smoke test, uninstall verification, checksum, and GitHub Release publication steps.

## Validation Record for v0.1.0.7-preview.1

Local validation on 2026-08-17 passed the TypeScript check and production build, all 65 Core tests, all 10 editor-service tests, .NET formatting verification, and PowerShell/YAML syntax checks. The packaged executable and isolated installed executable both passed the desktop smoke test with 16 preview artifacts and a lossless save/reopen round trip. The installer test also completed its silent per-user install and uninstall cleanup. No browser automation or Playwright run was used.

The local portable ZIP is 78,832,528 bytes with SHA-256 `4bdfe635771097c5228ef0adfad2245aedd3e2123bab532b8d3a18c1d2526c7d`. The local Setup EXE is 55,937,521 bytes with SHA-256 `671772a6f5059035fa5c3a929757e80ba2dbdf4a7aca84638fadef764a6a4917`. This prerelease remains deliberately unsigned while Artifact Signing is deferred and is intended for functional relation-wiring acceptance. Stable releases remain blocked without valid timestamped signatures. A TwinCAT XAE compile of a generated project remains a separate user acceptance step.

## Validation Record for v0.1.0.7-preview.2

Local validation on 2026-08-17 passed the TypeScript check and production build, all 73 Core tests, all 10 editor-service tests, .NET formatting, the complete Release publish, the packaged executable smoke test, and the isolated installer install/application-smoke/uninstall cycle. Both desktop smoke tests reported 16 BrushMachine reference artifacts and a lossless save/reopen round trip. No browser automation or Playwright run was used.

This prerelease adds opt-in generated runtime execution and transactional TwinCAT task integration. An isolated copy of the user's previously XAE-built `TwinCAT Project5` produced `PRG_PLC_Generated`, retained `MAIN`, added one generated `PouCall` to `PlcTask.TcTTO`, and reported synchronized on the repeated integrated CLI check. The live project was not modified; compiling and executing the new task-integrated copy in TwinCAT remain user acceptance steps.

The local portable ZIP is 76,327,302 bytes with SHA-256 `ad2f83d852cd36b5f3c124fad22e210b0a42de3a67a4d43ed770f67e6561c628`. The local Setup EXE is 55,940,767 bytes with SHA-256 `a4a61af13fe053a339f53d7bbe8d6d29e5ce9a63a6861739e7483b79e6b90e7f`. This prerelease remains deliberately unsigned while Artifact Signing is deferred; Windows can therefore show an unknown-publisher warning. Stable releases remain blocked without valid timestamped signatures.

## Validation Record for v0.1.0.7-preview.6

Local validation on 2026-08-17 passed the TypeScript check and production build, all 81 Core tests, all 10 editor-service tests, the complete self-contained Release publish, the packaged executable smoke test, and the isolated installer install/application-smoke/uninstall cycle. Both desktop smoke tests reported 16 BrushMachine reference artifacts and a lossless save/reopen round trip. No browser automation, interactive editor window, or Playwright run was used.

This prerelease renders multiple relationships between the same node pair on deterministic, separate lanes with individual labels. It also unifies the ET icon across the executable, WPF window, taskbar, shortcuts, installer, and web favicon; removes the obsolete Phase 3 header text; and subtly displays the full EngineeringToolbox AutomationBase name.

The local portable ZIP is 78,895,443 bytes with SHA-256 `a70e07c6eb3346fbfe8f60aec30faa1317d5598227f07c0c18f588736dd018da`. The local Setup EXE is 55,927,174 bytes with SHA-256 `262dde3ae00a9613cdaf14a321bb971abe7738ff4c93b11b32588c2099cb7891`. This prerelease remains deliberately unsigned while Artifact Signing is deferred; Windows can therefore show an unknown-publisher warning. Stable releases remain blocked without valid timestamped signatures.
