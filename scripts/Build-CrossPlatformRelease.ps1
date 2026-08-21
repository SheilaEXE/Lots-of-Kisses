param(
    [string]$Version,
    [string]$OutputDirectory,
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'

function Set-ZipUnixPermissions {
    param([Parameter(Mandatory = $true)][string]$Path)

    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $eocdSignature = [byte[]](0x50, 0x4b, 0x05, 0x06)
    $eocdOffset = -1
    $minimumOffset = [Math]::Max(0, $bytes.Length - 65557)

    for ($index = $bytes.Length - 22; $index -ge $minimumOffset; $index--) {
        if ($bytes[$index] -eq $eocdSignature[0] -and
            $bytes[$index + 1] -eq $eocdSignature[1] -and
            $bytes[$index + 2] -eq $eocdSignature[2] -and
            $bytes[$index + 3] -eq $eocdSignature[3]) {
            $eocdOffset = $index
            break
        }
    }

    if ($eocdOffset -lt 0) {
        throw 'Could not find the ZIP end-of-central-directory record.'
    }

    $entryCount = [System.BitConverter]::ToUInt16($bytes, $eocdOffset + 10)
    $entryOffset = [int][System.BitConverter]::ToUInt32($bytes, $eocdOffset + 16)

    for ($entryIndex = 0; $entryIndex -lt $entryCount; $entryIndex++) {
        if ([System.BitConverter]::ToUInt32($bytes, $entryOffset) -ne 0x02014b50) {
            throw "Invalid ZIP central-directory entry at offset $entryOffset."
        }

        $nameLength = [System.BitConverter]::ToUInt16($bytes, $entryOffset + 28)
        $extraLength = [System.BitConverter]::ToUInt16($bytes, $entryOffset + 30)
        $commentLength = [System.BitConverter]::ToUInt16($bytes, $entryOffset + 32)
        $name = [System.Text.Encoding]::UTF8.GetString($bytes, $entryOffset + 46, $nameLength)

        # Upper byte of "version made by": 3 identifies Unix.
        $bytes[$entryOffset + 5] = 3
        [uint32]$attributes = if ($name.EndsWith('/')) { 1106051072 } else { 2175008768 }
        [System.Array]::Copy([System.BitConverter]::GetBytes($attributes), 0, $bytes, $entryOffset + 38, 4)

        $entryOffset += 46 + $nameLength + $extraLength + $commentLength
    }

    [System.IO.File]::WriteAllBytes($Path, $bytes)
}

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$manifest = Get-Content -LiteralPath (Join-Path $repoRoot 'manifest.json') -Raw | ConvertFrom-Json

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = [string]$manifest.Version
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot 'bin\cross-platform-release'
}

$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

if (-not $SkipBuild) {
    & dotnet build (Join-Path $repoRoot 'Lots_of_Kisses.slnx') -c Release --no-restore
    if ($LASTEXITCODE -ne 0) {
        throw "Release build failed with exit code $LASTEXITCODE."
    }
}

$tar = Get-Command tar -ErrorAction Stop
$stageRoot = Join-Path $OutputDirectory ('.staging-' + [guid]::NewGuid().ToString('N'))
$packageRoot = Join-Path $stageRoot 'LotsOfKisses'
$archivePath = Join-Path $OutputDirectory "LotsOfKisses.$Version.zip"

try {
    New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $packageRoot 'assets') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $packageRoot 'i18n') -Force | Out-Null

    Copy-Item -LiteralPath (Join-Path $repoRoot 'bin\Release\net6.0\LotsOfKisses.dll') -Destination $packageRoot
    Copy-Item -LiteralPath (Join-Path $repoRoot 'manifest.json') -Destination $packageRoot
    Copy-Item -Path (Join-Path $repoRoot 'assets\*') -Destination (Join-Path $packageRoot 'assets') -Recurse
    Copy-Item -Path (Join-Path $repoRoot 'i18n\*') -Destination (Join-Path $packageRoot 'i18n') -Recurse

    if (Test-Path -LiteralPath $archivePath) {
        Remove-Item -LiteralPath $archivePath -Force
    }

    Push-Location $stageRoot
    try {
        & $tar.Source -a -cf $archivePath 'LotsOfKisses'
        if ($LASTEXITCODE -ne 0) {
            throw "ZIP creation failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }

    Set-ZipUnixPermissions -Path $archivePath

    Write-Output $archivePath
}
finally {
    $resolvedStage = [System.IO.Path]::GetFullPath($stageRoot)
    $expectedPrefix = $OutputDirectory.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if ($resolvedStage.StartsWith($expectedPrefix, [System.StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $resolvedStage)) {
        Remove-Item -LiteralPath $resolvedStage -Recurse -Force
    }
}
