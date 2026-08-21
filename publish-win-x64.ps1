[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+(?:\.\d+)?(?:-[0-9A-Za-z.-]+)?$')]
    [string]$Version = '0.1.0.8',

    [string]$OutputDirectory = '',

    [string]$BundleDirectory = '',

    [switch]$SkipTests,

    [switch]$SkipBuild,

    [switch]$SkipArchive,

    [switch]$RequireSignature
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
$packageName = "ETAB-Engineering-v$Version-win-x64"
$stagingRoot = Join-Path $outputRoot '.staging'
$stagingDirectory = Join-Path $stagingRoot ([Guid]::NewGuid().ToString('N'))
$bundleDirectory = if ([string]::IsNullOrWhiteSpace($BundleDirectory)) {
    Join-Path $stagingDirectory $packageName
} elseif ([IO.Path]::IsPathRooted($BundleDirectory)) {
    [IO.Path]::GetFullPath($BundleDirectory)
} else {
    [IO.Path]::GetFullPath((Join-Path $repositoryRoot $BundleDirectory))
}
$temporaryZip = Join-Path $stagingDirectory "$packageName.zip"
$finalZip = Join-Path $outputRoot "$packageName.zip"
$finalChecksum = "$finalZip.sha256"
$smokeLog = Join-Path $stagingDirectory 'desktop-smoke.txt'

function Assert-SafeChildPath {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [string]$Parent
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

function Invoke-Checked {
    param(
        [Parameter(Mandatory)]
        [string]$Command,

        [Parameter(ValueFromRemainingArguments)]
        [string[]]$Arguments
    )

    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command '$Command' failed with exit code $LASTEXITCODE."
    }
}

function Remove-ValidatedItem {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [string]$Parent,

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
        Remove-Item -LiteralPath $item.FullName -Recurse -Force
    } else {
        Remove-Item -LiteralPath $item.FullName -Force
    }
}

function Assert-ValidReleaseSignature {
    param([Parameter(Mandatory)][string]$Path)

    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    $subject = if ($null -eq $signature.SignerCertificate) {
        '<no signer certificate>'
    } else {
        $signature.SignerCertificate.Subject
    }
    if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
        throw "Release signature is not valid: path='$Path', status=$($signature.Status), subject=$subject"
    }
    if ($null -eq $signature.TimeStamperCertificate) {
        throw "Release signature has no trusted timestamp: path='$Path', subject=$subject"
    }

    Write-Host "Verified release signature: $subject"
}

if ($SkipBuild -and [string]::IsNullOrWhiteSpace($BundleDirectory)) {
    throw '-SkipBuild requires -BundleDirectory so an existing prepared bundle can be selected explicitly.'
}
if ($SkipArchive -and [string]::IsNullOrWhiteSpace($BundleDirectory)) {
    throw '-SkipArchive requires -BundleDirectory so the prepared bundle remains available for signing.'
}
if ([IO.Path]::GetFileName($bundleDirectory) -ne $packageName) {
    throw "Bundle directory must end in '$packageName' so the portable archive has the expected root directory."
}

New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
New-Item -ItemType Directory -Path $stagingDirectory -Force | Out-Null

try {
    if (-not $SkipBuild) {
        if (Test-Path -LiteralPath $bundleDirectory) {
            $bundleItem = Get-Item -LiteralPath $bundleDirectory -Force
            if (($bundleItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Refusing to publish into reparse point '$bundleDirectory'."
            }
            if (@(Get-ChildItem -LiteralPath $bundleDirectory -Force).Count -ne 0) {
                throw "Bundle directory must be empty before publishing: '$bundleDirectory'."
            }
        } else {
            New-Item -ItemType Directory -Path $bundleDirectory -Force | Out-Null
        }

        Push-Location -LiteralPath $repositoryRoot
        try {
            Write-Host "Installing deterministic editor dependencies..."
            Invoke-Checked npm.cmd --prefix .\src\ETAB.Engineering.Editor ci --no-audit --no-fund

            Write-Host "Checking the TypeScript editor..."
            Invoke-Checked npm.cmd --prefix .\src\ETAB.Engineering.Editor run check

            Write-Host "Restoring the .NET solution..."
            Invoke-Checked dotnet restore .\ETAB.Engineering.sln

            if (-not $SkipTests) {
                Write-Host "Running the Release test suite..."
                Invoke-Checked dotnet test .\ETAB.Engineering.sln --configuration Release --no-restore
            }

            Write-Host "Publishing the self-contained Windows x64 desktop bundle..."
            Invoke-Checked dotnet publish `
                .\src\ETAB.Engineering.Desktop\ETAB.Engineering.Desktop.csproj `
                --configuration Release `
                --runtime win-x64 `
                --self-contained true `
                --no-restore `
                /p:Version=$Version `
                --output $bundleDirectory
        } finally {
            Pop-Location
        }

        $readmeTemplate = Join-Path $repositoryRoot 'packaging\README-win-x64.txt'
        $readmeContent = [IO.File]::ReadAllText($readmeTemplate).Replace('{VERSION}', $Version)
        [IO.File]::WriteAllText(
            (Join-Path $bundleDirectory 'README.txt'),
            $readmeContent,
            [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText(
            (Join-Path $bundleDirectory 'VERSION.txt'),
            "$Version`n",
            [Text.UTF8Encoding]::new($false))
    } elseif (-not (Test-Path -LiteralPath $bundleDirectory -PathType Container)) {
        throw "Prepared bundle directory was not found: '$bundleDirectory'."
    }

    $desktopExecutable = Join-Path $bundleDirectory 'ETAB Engineering.exe'
    $requiredFiles = @(
        $desktopExecutable,
        (Join-Path $bundleDirectory 'wwwroot\index.html'),
        (Join-Path $bundleDirectory 'schemas\etab-project.schema.json'),
        (Join-Path $bundleDirectory 'examples\BrushMachine.reference.etab.json'),
        (Join-Path $bundleDirectory 'examples\BrushMachine.integration.etab.json'),
        (Join-Path $bundleDirectory 'WebView2Loader.dll'),
        (Join-Path $bundleDirectory 'README.txt'),
        (Join-Path $bundleDirectory 'VERSION.txt')
    )
    foreach ($requiredFile in $requiredFiles) {
        if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
            throw "Published bundle is incomplete: '$requiredFile' is missing."
        }
    }

    $preparedVersion = [IO.File]::ReadAllText((Join-Path $bundleDirectory 'VERSION.txt')).Trim()
    if ($preparedVersion -ne $Version) {
        throw "Prepared bundle version '$preparedVersion' does not match requested version '$Version'."
    }
    if ($RequireSignature) {
        Assert-ValidReleaseSignature -Path $desktopExecutable
    }

    Write-Host "Running the published executable smoke test..."
    $smokeArguments = @(
        '--smoke-test',
        '--smoke-test-log',
        ('"' + $smokeLog + '"')
    )
    $smokeProcess = Start-Process `
        -FilePath $desktopExecutable `
        -ArgumentList $smokeArguments `
        -WindowStyle Hidden `
        -Wait `
        -PassThru
    if ($smokeProcess.ExitCode -ne 0) {
        $details = if (Test-Path -LiteralPath $smokeLog) {
            [IO.File]::ReadAllText($smokeLog)
        } else {
            'No smoke-test log was produced.'
        }
        throw "Published desktop smoke test failed with exit code $($smokeProcess.ExitCode).`n$details"
    }
    Get-Content -LiteralPath $smokeLog -Encoding UTF8 | ForEach-Object { Write-Host $_ }

    if ($SkipArchive) {
        Write-Host 'Prepared desktop bundle is ready for Authenticode signing.'
        Write-Host "Bundle: $bundleDirectory"
        return
    }

    Write-Host "Creating portable archive..."
    Compress-Archive -LiteralPath $bundleDirectory -DestinationPath $temporaryZip -CompressionLevel Optimal
    Remove-ValidatedItem -Path $finalZip -Parent $outputRoot
    Remove-ValidatedItem -Path $finalChecksum -Parent $outputRoot
    Move-Item -LiteralPath $temporaryZip -Destination $finalZip

    $hash = (Get-FileHash -LiteralPath $finalZip -Algorithm SHA256).Hash.ToLowerInvariant()
    $checksumLine = "$hash  $([IO.Path]::GetFileName($finalZip))`n"
    [IO.File]::WriteAllText($finalChecksum, $checksumLine, [Text.UTF8Encoding]::new($false))

    Write-Host "Desktop bundle created successfully."
    Write-Host "ZIP: $finalZip"
    Write-Host "SHA256: $hash"
} finally {
    Remove-ValidatedItem -Path $stagingDirectory -Parent $stagingRoot -Recurse
}
