[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')]
    [string]$Version = '0.1.0.2',

    [string]$OutputDirectory = '',

    [string]$InnoCompilerPath = '',

    [string]$WebView2BootstrapperPath = '',

    [switch]$SkipTests,

    [switch]$SkipPortableBuild,

    [switch]$SkipInstallerTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$OutputDirectory = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    Join-Path $repositoryRoot 'artifacts'
} else {
    $OutputDirectory
}
$outputRoot = if ([IO.Path]::IsPathRooted($OutputDirectory)) {
    [IO.Path]::GetFullPath($OutputDirectory)
} else {
    [IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputDirectory))
}
$portablePackageName = "ETAB-Engineering-v$Version-win-x64"
$portableZip = Join-Path $outputRoot "$portablePackageName.zip"
$portableChecksum = "$portableZip.sha256"
$installerName = "ETAB-Engineering-v$Version-win-x64-setup.exe"
$finalInstaller = Join-Path $outputRoot $installerName
$finalChecksum = "$finalInstaller.sha256"
$stagingRoot = Join-Path $outputRoot '.installer-staging'
$stagingDirectory = Join-Path $stagingRoot ([Guid]::NewGuid().ToString('N'))
$extractionRoot = Join-Path $stagingDirectory 'payload'
$stagingInstaller = Join-Path $stagingDirectory $installerName
$bootstrapper = Join-Path $stagingDirectory 'MicrosoftEdgeWebview2Setup.exe'
$webView2BootstrapperUrl = 'https://go.microsoft.com/fwlink/p/?LinkId=2124703'

function Assert-SafeChildPath {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Parent
    )

    $resolvedPath = [IO.Path]::GetFullPath($Path)
    $resolvedParent = [IO.Path]::GetFullPath($Parent).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    $boundary = $resolvedParent + [IO.Path]::DirectorySeparatorChar
    if (-not $resolvedPath.StartsWith($boundary, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify '$resolvedPath' because it is outside '$resolvedParent'."
    }
}

function Remove-ValidatedItem {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Parent,
        [switch]$Recurse
    )

    Assert-SafeChildPath -Path $Path -Parent $Parent
    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    $item = Get-Item -LiteralPath $Path -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Refusing to remove reparse point '$($item.FullName)'."
    }

    if ($Recurse) {
        $nestedReparsePoints = @(Get-ChildItem -LiteralPath $item.FullName -Recurse -Force |
            Where-Object { ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 })
        if ($nestedReparsePoints.Count -ne 0) {
            throw "Refusing to remove '$($item.FullName)' because it contains reparse points."
        }
        Remove-Item -LiteralPath $item.FullName -Recurse -Force
    } else {
        Remove-Item -LiteralPath $item.FullName -Force
    }
}

function Resolve-InnoCompiler {
    if (-not [string]::IsNullOrWhiteSpace($InnoCompilerPath)) {
        $resolved = (Resolve-Path -LiteralPath $InnoCompilerPath).Path
        if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
            throw "Inno Setup compiler was not found at '$resolved'."
        }
        return $resolved
    }

    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    $knownPaths = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 7\ISCC.exe'),
        'C:\Program Files\Inno Setup 7\ISCC.exe',
        'C:\Program Files (x86)\Inno Setup 7\ISCC.exe',
        'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
        'C:\Program Files\Inno Setup 6\ISCC.exe'
    )
    foreach ($knownPath in $knownPaths) {
        if (Test-Path -LiteralPath $knownPath -PathType Leaf) {
            return $knownPath
        }
    }

    throw 'Inno Setup compiler ISCC.exe was not found. Install Inno Setup 7 or pass -InnoCompilerPath.'
}

function Assert-MicrosoftBootstrapper {
    param([Parameter(Mandatory)][string]$Path)

    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    $subject = if ($null -eq $signature.SignerCertificate) {
        '<no signer certificate>'
    } else {
        $signature.SignerCertificate.Subject
    }
    if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid -or
        $subject -notmatch 'O=Microsoft Corporation(?:,|$)') {
        throw "WebView2 bootstrapper signature is not valid Microsoft code: status=$($signature.Status), subject=$subject"
    }
}

function Assert-PortablePackage {
    if (-not (Test-Path -LiteralPath $portableZip -PathType Leaf) -or
        -not (Test-Path -LiteralPath $portableChecksum -PathType Leaf)) {
        throw "Portable package or checksum is missing under '$outputRoot'."
    }

    $actualHash = (Get-FileHash -LiteralPath $portableZip -Algorithm SHA256).Hash.ToLowerInvariant()
    $declaredHash = (Get-Content -LiteralPath $portableChecksum -Raw).Trim().Split(' ')[0].ToLowerInvariant()
    if ($actualHash -ne $declaredHash) {
        throw "Portable ZIP checksum mismatch: actual=$actualHash, declared=$declaredHash"
    }
}

New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
New-Item -ItemType Directory -Path $stagingDirectory -Force | Out-Null

try {
    if (-not $SkipPortableBuild) {
        $publishArguments = @{
            Version = $Version
            OutputDirectory = $outputRoot
        }
        if ($SkipTests) {
            $publishArguments.SkipTests = $true
        }
        & (Join-Path $repositoryRoot 'publish-win-x64.ps1') @publishArguments
    }
    Assert-PortablePackage

    Write-Host 'Extracting the verified portable payload...'
    New-Item -ItemType Directory -Path $extractionRoot -Force | Out-Null
    Expand-Archive -LiteralPath $portableZip -DestinationPath $extractionRoot
    $payloadRoot = Join-Path $extractionRoot $portablePackageName
    if (-not (Test-Path -LiteralPath (Join-Path $payloadRoot 'ETAB Engineering.exe') -PathType Leaf)) {
        throw "Portable payload was not extracted as expected under '$payloadRoot'."
    }

    if ([string]::IsNullOrWhiteSpace($WebView2BootstrapperPath)) {
        Write-Host 'Downloading the Microsoft WebView2 Evergreen bootstrapper...'
        Invoke-WebRequest `
            -UseBasicParsing `
            -Uri $webView2BootstrapperUrl `
            -OutFile $bootstrapper
    } else {
        $resolvedBootstrapper = (Resolve-Path -LiteralPath $WebView2BootstrapperPath).Path
        Copy-Item -LiteralPath $resolvedBootstrapper -Destination $bootstrapper
    }
    Assert-MicrosoftBootstrapper -Path $bootstrapper

    $compiler = Resolve-InnoCompiler
    $installerDefinition = Join-Path $repositoryRoot 'installer\ETAB Engineering.iss'
    $previousEnvironment = @{
        Version = $env:ETAB_INSTALLER_VERSION
        Source = $env:ETAB_INSTALLER_SOURCE
        Bootstrapper = $env:ETAB_WEBVIEW2_BOOTSTRAPPER
        Output = $env:ETAB_INSTALLER_OUTPUT
    }
    try {
        $env:ETAB_INSTALLER_VERSION = $Version
        $env:ETAB_INSTALLER_SOURCE = $payloadRoot
        $env:ETAB_WEBVIEW2_BOOTSTRAPPER = $bootstrapper
        $env:ETAB_INSTALLER_OUTPUT = $stagingDirectory

        Write-Host "Compiling the Inno Setup installer with '$compiler'..."
        & $compiler '/Qp' $installerDefinition
        if ($LASTEXITCODE -ne 0) {
            throw "Inno Setup compiler failed with exit code $LASTEXITCODE."
        }
    } finally {
        $env:ETAB_INSTALLER_VERSION = $previousEnvironment.Version
        $env:ETAB_INSTALLER_SOURCE = $previousEnvironment.Source
        $env:ETAB_WEBVIEW2_BOOTSTRAPPER = $previousEnvironment.Bootstrapper
        $env:ETAB_INSTALLER_OUTPUT = $previousEnvironment.Output
    }

    if (-not (Test-Path -LiteralPath $stagingInstaller -PathType Leaf)) {
        throw "Inno Setup did not produce '$stagingInstaller'."
    }

    if (-not $SkipInstallerTest) {
        & (Join-Path $repositoryRoot 'test-installer-win-x64.ps1') `
            -InstallerPath $stagingInstaller
    }

    Remove-ValidatedItem -Path $finalInstaller -Parent $outputRoot
    Remove-ValidatedItem -Path $finalChecksum -Parent $outputRoot
    Move-Item -LiteralPath $stagingInstaller -Destination $finalInstaller

    $installerHash = (Get-FileHash -LiteralPath $finalInstaller -Algorithm SHA256).Hash.ToLowerInvariant()
    $checksumLine = "$installerHash  $installerName`n"
    [IO.File]::WriteAllText(
        $finalChecksum,
        $checksumLine,
        [Text.UTF8Encoding]::new($false))

    Write-Host 'Windows installer created successfully.'
    Write-Host "Installer: $finalInstaller"
    Write-Host "SHA256: $installerHash"
} finally {
    Remove-ValidatedItem -Path $stagingDirectory -Parent $stagingRoot -Recurse
}
