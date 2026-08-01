param(
    [string] $Tag = $env:CI_COMMIT_TAG,
    [string] $Commit = $env:CI_COMMIT_SHA,
    [string] $DotEnv = "release.env",
    [switch] $SelfTest
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$tagPattern = "^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$"
$commitPattern = "^(?:[0-9a-f]{40}|[0-9a-f]{64})$"

function Invoke-ReleaseGit {
    param(
        [Parameter(Mandatory = $true)]
        [string[]] $Arguments,
        [int[]] $AllowedExitCodes = @(0)
    )

    $lines = & git -C $repositoryRoot @Arguments 2>&1
    $exitCode = $LASTEXITCODE
    if ($AllowedExitCodes -notcontains $exitCode) {
        throw "git failed while validating release history"
    }
    return (($lines | ForEach-Object { "$_" }) -join "`n").Trim()
}

function Assert-CleanReleaseStatus {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string] $StatusText
    )

    if (-not [string]::IsNullOrWhiteSpace($StatusText)) {
        throw "release checkout contains tracked or untracked changes"
    }
}

function Assert-SelfTestThrows {
    param(
        [Parameter(Mandatory = $true)]
        [scriptblock] $Action
    )

    $failure = $null
    try {
        & $Action
    } catch {
        $failure = $_
    }
    if ($null -eq $failure) {
        throw "release policy self-test expected rejection"
    }
}

if ($SelfTest) {
    Assert-CleanReleaseStatus -StatusText ""
    Assert-SelfTestThrows {
        Assert-CleanReleaseStatus -StatusText " M src/Locker/Locker.csproj"
    }
    Assert-SelfTestThrows {
        Assert-CleanReleaseStatus -StatusText "?? unexpected-package.nupkg"
    }
    Write-Output "release version self-test passed"
    exit 0
}

if ([string]::IsNullOrWhiteSpace($Tag) -or $Tag -notmatch $tagPattern) {
    throw "release tag must match vMAJOR.MINOR.PATCH"
}
if ([string]::IsNullOrWhiteSpace($Commit) -or $Commit -notmatch $commitPattern) {
    throw "release commit must be a full lowercase Git object ID"
}
if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw "git is required to derive a deterministic release version"
}

$head = Invoke-ReleaseGit -Arguments @("rev-parse", "HEAD^{commit}")
if ($head -ne $Commit) {
    throw "release checkout HEAD does not match CI_COMMIT_SHA"
}
$checkoutStatus = Invoke-ReleaseGit -Arguments @(
    "status",
    "--porcelain=v1",
    "--untracked-files=all"
)
Assert-CleanReleaseStatus -StatusText $checkoutStatus

$version = $Tag.Substring(1)

$null = Invoke-ReleaseGit -Arguments @(
    "cat-file",
    "-e",
    "refs/remotes/origin/main^{commit}"
)
$null = Invoke-ReleaseGit -Arguments @(
    "merge-base",
    "--is-ancestor",
    $Commit,
    "refs/remotes/origin/main"
)

$sourceDateEpochText = Invoke-ReleaseGit -Arguments @(
    "show",
    "-s",
    "--format=%ct",
    $Commit
)
$sourceDateEpoch = 0L
if (
    -not [long]::TryParse($sourceDateEpochText, [ref]$sourceDateEpoch) -or
    $sourceDateEpoch -lt 1
) {
    throw "git returned an invalid source commit timestamp"
}

$dotEnvPath = [IO.Path]::GetFullPath((Join-Path $repositoryRoot $DotEnv))
if (-not $dotEnvPath.StartsWith(
    $repositoryRoot + [IO.Path]::DirectorySeparatorChar,
    [StringComparison]::OrdinalIgnoreCase
)) {
    throw "release dotenv output must stay inside the repository"
}
$temporary = "$dotEnvPath.$([Guid]::NewGuid().ToString('N')).tmp"
$content = (
    "LOCKER_SDK_VERSION=$version`n" +
    "LOCKER_RELEASE_TAG=$Tag`n" +
    "SOURCE_DATE_EPOCH=$sourceDateEpoch`n"
)
try {
    [IO.File]::WriteAllText(
        $temporary,
        $content,
        [Text.UTF8Encoding]::new($false)
    )
    Move-Item -LiteralPath $temporary -Destination $dotEnvPath -Force
} finally {
    if (Test-Path -LiteralPath $temporary) {
        Remove-Item -LiteralPath $temporary -Force
    }
}

Write-Output "prepared lockersm $version ($Tag)"
