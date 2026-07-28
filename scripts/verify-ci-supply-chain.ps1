$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$expectedSdk = "8.0.423"
$maximumInputBytes = 2MB

function Read-BoundedText {
    param([Parameter(Mandatory = $true)][string]$Path)

    $item = Get-Item -LiteralPath $Path -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "$Path must not be a reparse point"
    }
    if ($item.PSIsContainer -or $item.Length -le 0 -or $item.Length -gt $maximumInputBytes) {
        throw "$Path is not a bounded regular file"
    }
    return [IO.File]::ReadAllText($item.FullName, [Text.UTF8Encoding]::new($false, $true))
}

function Assert-LockedPackages {
    param(
        [Parameter(Mandatory = $true)]$Node,
        [Parameter(Mandatory = $true)][string]$Path
    )

    if ($null -eq $Node) {
        return
    }
    if ($Node -is [System.Collections.IDictionary]) {
        foreach ($key in $Node.Keys) {
            Assert-LockedPackages -Node $Node[$key] -Path "$Path.$key"
        }
        return
    }
    if ($Node -is [System.Collections.IEnumerable] -and $Node -isnot [string]) {
        foreach ($entry in $Node) {
            Assert-LockedPackages -Node $entry -Path $Path
        }
        return
    }
    if ($Node -isnot [PSCustomObject]) {
        return
    }

    $typeProperty = $Node.PSObject.Properties["type"]
    if ($null -ne $typeProperty -and $typeProperty.Value -in @("Direct", "Transitive")) {
        $resolved = $Node.PSObject.Properties["resolved"]
        $contentHash = $Node.PSObject.Properties["contentHash"]
        if ($null -eq $resolved -or [string]::IsNullOrWhiteSpace([string]$resolved.Value)) {
            throw "$Path is missing its resolved NuGet version"
        }
        if ($null -eq $contentHash -or [string]::IsNullOrWhiteSpace([string]$contentHash.Value)) {
            throw "$Path is missing its NuGet content hash"
        }
    }

    foreach ($property in $Node.PSObject.Properties) {
        Assert-LockedPackages -Node $property.Value -Path "$Path.$($property.Name)"
    }
}

$globalJsonPath = Join-Path $repositoryRoot "global.json"
$globalJson = Read-BoundedText -Path $globalJsonPath | ConvertFrom-Json
if (
    $globalJson.sdk.version -ne $expectedSdk -or
    $globalJson.sdk.rollForward -ne "disable" -or
    $globalJson.sdk.allowPrerelease -ne $false
) {
    throw "global.json must select only stable .NET SDK $expectedSdk"
}

$pipeline = Read-BoundedText -Path (Join-Path $repositoryRoot ".gitlab-ci.yml")
if ($pipeline -match "(?i)\b(?:dotnet-install|choco\s+install|winget\s+install|Invoke-WebRequest|curl|wget)\b") {
    throw "CI must use the pre-provisioned exact SDK and must not download a mutable toolchain"
}
foreach ($required in @(
    "--locked-mode",
    "NUGET_PACKAGES",
    "verify-ci-supply-chain.ps1",
    "LOCKER_CLI_RELEASE_PUBLIC_KEY"
)) {
    if (-not $pipeline.Contains($required)) {
        throw "CI supply-chain contract is missing: $required"
    }
}

$projectFiles = Get-ChildItem -LiteralPath (Join-Path $repositoryRoot "src"), (Join-Path $repositoryRoot "tools") `
    -Recurse -Filter "*.csproj" -File
foreach ($project in $projectFiles) {
    $lockPath = Join-Path $project.DirectoryName "packages.lock.json"
    if (-not (Test-Path -LiteralPath $lockPath -PathType Leaf)) {
        throw "$($project.FullName) has no packages.lock.json"
    }
}

$lockFiles = Get-ChildItem -LiteralPath (Join-Path $repositoryRoot "src"), (Join-Path $repositoryRoot "tools") `
    -Recurse -Filter "packages.lock.json" -File
if ($lockFiles.Count -ne $projectFiles.Count) {
    throw "every .NET project must have exactly one adjacent NuGet lock"
}
foreach ($lockFile in $lockFiles) {
    $lock = Read-BoundedText -Path $lockFile.FullName | ConvertFrom-Json
    Assert-LockedPackages -Node $lock -Path $lockFile.FullName
}

Write-Output "CI supply-chain contract is valid"
