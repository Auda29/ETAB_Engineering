[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+(?:\.\d+)?(?:-[0-9A-Za-z.-]+)?$')]
    [string]$Version = '0.1.0.2',

    [string]$OutputDirectory = '',

    [switch]$SkipTests
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
$bundleDirectory = Join-Path $stagingDirectory $packageName
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

New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
New-Item -ItemType Directory -Path $stagingDirectory -Force | Out-Null

try {
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

    $desktopExecutable = Join-Path $bundleDirectory 'ETAB Engineering.exe'
    $requiredFiles = @(
        $desktopExecutable,
        (Join-Path $bundleDirectory 'wwwroot\index.html'),
        (Join-Path $bundleDirectory 'schemas\etab-project.schema.json'),
        (Join-Path $bundleDirectory 'examples\BrushMachine.reference.etab.json'),
        (Join-Path $bundleDirectory 'examples\BrushMachine.integration.etab.json'),
        (Join-Path $bundleDirectory 'WebView2Loader.dll')
    )
    foreach ($requiredFile in $requiredFiles) {
        if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
            throw "Published bundle is incomplete: '$requiredFile' is missing."
        }
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
