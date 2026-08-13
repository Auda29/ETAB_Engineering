[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$InstallerPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$resolvedInstaller = (Resolve-Path -LiteralPath $InstallerPath).Path
$expectedParent = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) 'ETAB Engineering Installer Smoke'))
$testRoot = Join-Path $expectedParent ([Guid]::NewGuid().ToString('N'))
$installDirectory = Join-Path $testRoot 'app'
$logDirectory = Join-Path $testRoot 'logs'
$installLog = Join-Path $logDirectory 'install.log'
$desktopSmokeLog = Join-Path $logDirectory 'desktop-smoke.log'
$uninstallLog = Join-Path $logDirectory 'uninstall.log'
$uninstallKeys = @(
    'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{42C1067E-48AA-4AA3-B465-51190687A7BD}_is1',
    'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{42C1067E-48AA-4AA3-B465-51190687A7BD}_is1',
    'HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\{42C1067E-48AA-4AA3-B465-51190687A7BD}_is1'
)
$setupCompleted = $false
$uninstallSucceeded = $false

function Start-CheckedProcess {
    param(
        [Parameter(Mandatory)]
        [string]$FilePath,

        [Parameter(Mandatory)]
        [string[]]$ArgumentList
    )

    $process = Start-Process `
        -FilePath $FilePath `
        -ArgumentList $ArgumentList `
        -WindowStyle Hidden `
        -Wait `
        -PassThru
    if ($process.ExitCode -ne 0) {
        throw "'$FilePath' failed with exit code $($process.ExitCode)."
    }
}

function Remove-ValidatedTestRoot {
    param([Parameter(Mandatory)][string]$Path)

    $resolved = [IO.Path]::GetFullPath($Path)
    $boundary = $expectedParent.TrimEnd('\') + '\'
    if (-not $resolved.StartsWith($boundary, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove test path '$resolved' outside '$expectedParent'."
    }
    if (-not (Test-Path -LiteralPath $resolved)) {
        return
    }

    $items = @(
        Get-Item -LiteralPath $resolved -Force
        Get-ChildItem -LiteralPath $resolved -Recurse -Force
    )
    $reparsePoints = @($items | Where-Object {
        ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0
    })
    if ($reparsePoints.Count -ne 0) {
        throw "Refusing to remove installer smoke-test data containing reparse points."
    }

    Remove-Item -LiteralPath $resolved -Recurse -Force

    $remainingTestRoots = @(Get-ChildItem -LiteralPath $expectedParent -Force)
    if ($remainingTestRoots.Count -eq 0) {
        $parentItem = Get-Item -LiteralPath $expectedParent -Force
        if (($parentItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Refusing to remove reparse-point parent '$expectedParent'."
        }
        Remove-Item -LiteralPath $expectedParent -Force
    }
}

$existingRegistrations = @($uninstallKeys | Where-Object { Test-Path -LiteralPath $_ })
if ($existingRegistrations.Count -ne 0) {
    throw "ETAB Engineering is already installed; refusing to alter that installation during the smoke test: $($existingRegistrations -join ', ')"
}

New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
try {
    $setupArguments = @(
        '/VERYSILENT',
        '/SUPPRESSMSGBOXES',
        '/NORESTART',
        '/SP-',
        ('/DIR="' + $installDirectory + '"'),
        ('/LOG="' + $installLog + '"')
    )
    Start-CheckedProcess -FilePath $resolvedInstaller -ArgumentList $setupArguments
    $setupCompleted = $true

    $desktopExecutable = Join-Path $installDirectory 'ETAB Engineering.exe'
    $uninstaller = Join-Path $installDirectory 'unins000.exe'
    $requiredFiles = @(
        $desktopExecutable,
        $uninstaller,
        (Join-Path $installDirectory 'wwwroot\index.html'),
        (Join-Path $installDirectory 'schemas\etab-project.schema.json'),
        (Join-Path $installDirectory 'examples\BrushMachine.reference.etab.json')
    )
    foreach ($requiredFile in $requiredFiles) {
        if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
            throw "Installer smoke test is missing '$requiredFile'."
        }
    }
    $createdRegistrations = @($uninstallKeys | Where-Object { Test-Path -LiteralPath $_ })
    if ($createdRegistrations.Count -ne 1 -or
        $createdRegistrations[0] -ne $uninstallKeys[0]) {
        throw "Installer did not create exactly the expected current-user registration: $($createdRegistrations -join ', ')"
    }

    Start-CheckedProcess `
        -FilePath $desktopExecutable `
        -ArgumentList @('--smoke-test', '--smoke-test-log', ('"' + $desktopSmokeLog + '"'))
    if (-not (Test-Path -LiteralPath $desktopSmokeLog -PathType Leaf)) {
        throw 'Installed desktop executable did not produce its smoke-test log.'
    }

    Start-CheckedProcess `
        -FilePath $uninstaller `
        -ArgumentList @(
            '/VERYSILENT',
            '/SUPPRESSMSGBOXES',
            '/NORESTART',
            ('/LOG="' + $uninstallLog + '"')
        )

    if (Test-Path -LiteralPath $installDirectory) {
        throw "Uninstaller left the application directory behind: '$installDirectory'."
    }
    $remainingRegistrations = @($uninstallKeys | Where-Object { Test-Path -LiteralPath $_ })
    if ($remainingRegistrations.Count -ne 0) {
        throw "Uninstaller left registration behind: $($remainingRegistrations -join ', ')"
    }
    $uninstallSucceeded = $true

    Get-Content -LiteralPath $desktopSmokeLog -Encoding UTF8
    Write-Host 'Installer smoke test passed.'
    Write-Host "Install/uninstall root: $testRoot"
} finally {
    $uninstaller = Join-Path $installDirectory 'unins000.exe'
    $currentRegistrations = @($uninstallKeys | Where-Object { Test-Path -LiteralPath $_ })
    $installationObserved = $setupCompleted -or
        $currentRegistrations.Count -ne 0 -or
        (Test-Path -LiteralPath $uninstaller -PathType Leaf)
    if ($installationObserved -and -not $uninstallSucceeded) {
        if (Test-Path -LiteralPath $uninstaller -PathType Leaf) {
            try {
                Start-CheckedProcess `
                    -FilePath $uninstaller `
                    -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART')
                $uninstallSucceeded = $true
            } catch {
                Write-Warning "Automatic installer-smoke cleanup failed: $($_.Exception.Message)"
            }
        }
    }

    $currentRegistrations = @($uninstallKeys | Where-Object { Test-Path -LiteralPath $_ })
    $installationRemains = $currentRegistrations.Count -ne 0 -or
        (Test-Path -LiteralPath $installDirectory)
    if ($installationRemains -and -not $uninstallSucceeded) {
        Write-Warning "Installer smoke-test state remains at '$testRoot'; no recursive cleanup was attempted."
    } else {
        Remove-ValidatedTestRoot -Path $testRoot
    }
}
