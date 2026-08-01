param(
    [string] $Commit = $env:CI_COMMIT_SHA,
    [string] $DotEnv = "release.env",
    [switch] $SelfTest
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$semVerPattern = "^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$"
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

function Get-DerivedVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string] $BaseVersion,
        [Parameter(Mandatory = $true)]
        [long] $Distance,
        [Parameter(Mandatory = $true)]
        [long] $FirstReleaseDistance
    )

    if ($BaseVersion -notmatch $semVerPattern) {
        throw "VERSION must contain stable canonical SemVer"
    }
    if ($Distance -lt $FirstReleaseDistance -or $FirstReleaseDistance -lt 1) {
        throw "release commit predates the first releasable commit"
    }

    $parts = $BaseVersion.Split(".")
    $patch = [long]::Parse($parts[2], [Globalization.CultureInfo]::InvariantCulture)
    $derivedPatch = $patch + ($Distance - $FirstReleaseDistance)
    if ($derivedPatch -lt $patch) {
        throw "derived release patch overflowed"
    }
    return "$($parts[0]).$($parts[1]).$derivedPatch"
}

function Get-ReleasePredecessor {
    param(
        [Parameter(Mandatory = $true)]
        [string] $BaseVersion,
        [Parameter(Mandatory = $true)]
        [long] $Distance,
        [Parameter(Mandatory = $true)]
        [long] $FirstReleaseDistance,
        [Parameter(Mandatory = $true)]
        [string[]] $OrderedHistory
    )

    if (
        $Distance -lt $FirstReleaseDistance -or
        $OrderedHistory.Count -ne $Distance
    ) {
        throw "release predecessor history is inconsistent"
    }
    if ($Distance -eq $FirstReleaseDistance) {
        return [PSCustomObject]@{
            Required = 0
            Tag = ""
            Commit = ""
        }
    }

    $previousDistance = $Distance - 1
    $previousVersion = Get-DerivedVersion `
        -BaseVersion $BaseVersion `
        -Distance $previousDistance `
        -FirstReleaseDistance $FirstReleaseDistance
    $previousIndex = [int]($previousDistance - 1)
    $previousCommit = $OrderedHistory[$previousIndex]
    if ($previousCommit -notmatch $commitPattern) {
        throw "release predecessor commit is invalid"
    }
    return [PSCustomObject]@{
        Required = 1
        Tag = "v$previousVersion"
        Commit = $previousCommit
    }
}

function Assert-ReleaseLineStartsAtBaseline {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Baseline,
        [Parameter(Mandatory = $true)]
        [string] $OldestHistoryLine
    )

    $parts = @(
        $OldestHistoryLine -split " " |
            Where-Object { $_ }
    )
    if (
        $Baseline -notmatch $commitPattern -or
        $parts.Count -lt 2 -or
        @($parts | Where-Object { $_ -notmatch $commitPattern }).Count -ne 0
    ) {
        throw "release-line root history is invalid"
    }
    if ($parts[1] -cne $Baseline) {
        throw (
            "the baseline commit must be the first parent of the first " +
            "release-line merge; a second-parent baseline is not accepted"
        )
    }
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
    if ((Get-DerivedVersion "1.0.0" 1 1) -ne "1.0.0") {
        throw "release version self-test failed"
    }
    if ((Get-DerivedVersion "1.0.0" 2 1) -ne "1.0.1") {
        throw "release version self-test failed"
    }
    if ((Get-DerivedVersion "9.8.7" 3 1) -ne "9.8.9") {
        throw "release version self-test failed"
    }
    $commitOne = -join ("a" * 40)
    $commitTwo = -join ("b" * 40)
    $commitThree = -join ("c" * 40)
    $first = Get-ReleasePredecessor `
        -BaseVersion "1.0.0" `
        -Distance 1 `
        -FirstReleaseDistance 1 `
        -OrderedHistory @($commitOne)
    if (
        $first.Required -ne 0 -or
        $first.Tag -ne "" -or
        $first.Commit -ne ""
    ) {
        throw "first release predecessor self-test failed"
    }
    $second = Get-ReleasePredecessor `
        -BaseVersion "1.0.0" `
        -Distance 2 `
        -FirstReleaseDistance 1 `
        -OrderedHistory @($commitOne, $commitTwo)
    if (
        $second.Required -ne 1 -or
        $second.Tag -cne "v1.0.0" -or
        $second.Commit -cne $commitOne
    ) {
        throw "second release predecessor self-test failed"
    }
    $third = Get-ReleasePredecessor `
        -BaseVersion "1.0.0" `
        -Distance 3 `
        -FirstReleaseDistance 1 `
        -OrderedHistory @($commitOne, $commitTwo, $commitThree)
    if (
        $third.Required -ne 1 -or
        $third.Tag -cne "v1.0.1" -or
        $third.Commit -cne $commitTwo
    ) {
        throw "immediate release predecessor self-test failed"
    }
    $delayedFirst = Get-ReleasePredecessor `
        -BaseVersion "2.4.0" `
        -Distance 3 `
        -FirstReleaseDistance 3 `
        -OrderedHistory @($commitOne, $commitTwo, $commitThree)
    if (
        $delayedFirst.Required -ne 0 -or
        $delayedFirst.Tag -ne "" -or
        $delayedFirst.Commit -ne ""
    ) {
        throw "baseline-based first release self-test failed"
    }
    $commitFour = -join ("d" * 40)
    $delayedSecond = Get-ReleasePredecessor `
        -BaseVersion "2.4.0" `
        -Distance 4 `
        -FirstReleaseDistance 3 `
        -OrderedHistory @(
            $commitOne,
            $commitTwo,
            $commitThree,
            $commitFour
        )
    if (
        $delayedSecond.Required -ne 1 -or
        $delayedSecond.Tag -cne "v2.4.0" -or
        $delayedSecond.Commit -cne $commitThree
    ) {
        throw "baseline-based predecessor self-test failed"
    }
    Assert-ReleaseLineStartsAtBaseline `
        -Baseline $commitOne `
        -OldestHistoryLine "$commitThree $commitOne $commitTwo"
    Assert-SelfTestThrows {
        Assert-ReleaseLineStartsAtBaseline `
            -Baseline $commitTwo `
            -OldestHistoryLine "$commitThree $commitOne $commitTwo"
    }
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

if ([string]::IsNullOrWhiteSpace($Commit) -or $Commit -notmatch $commitPattern) {
    throw "release commit must be a full lowercase Git object ID"
}
if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw "git is required to derive a deterministic release version"
}

$policyPath = Join-Path $PSScriptRoot "release-policy.json"
$policy = Get-Content -LiteralPath $policyPath -Raw -Encoding UTF8 |
    ConvertFrom-Json
$policyFields = @($policy.PSObject.Properties.Name | Sort-Object)
$expectedPolicyFields = @(
    "baseline_commit",
    "first_release_distance",
    "mainline_mode",
    "schema_version"
)
if (($policyFields -join "`n") -ne ($expectedPolicyFields -join "`n")) {
    throw "release policy fields do not match the reviewed schema"
}
if (
    $policy.schema_version -ne 1 -or
    $policy.mainline_mode -ne "merge_commit" -or
    "$($policy.baseline_commit)" -notmatch $commitPattern -or
    [long]$policy.first_release_distance -lt 1
) {
    throw "release policy is invalid"
}

$baseline = "$($policy.baseline_commit)"
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

$null = Invoke-ReleaseGit -Arguments @(
    "cat-file",
    "-e",
    "$baseline^{commit}"
)
$null = Invoke-ReleaseGit -Arguments @(
    "merge-base",
    "--is-ancestor",
    $baseline,
    $Commit
)

$distanceText = Invoke-ReleaseGit -Arguments @(
    "rev-list",
    "--first-parent",
    "--count",
    "$baseline..$Commit"
)
$distance = 0L
if (-not [long]::TryParse($distanceText, [ref]$distance)) {
    throw "git returned an invalid first-parent distance"
}

$historyText = Invoke-ReleaseGit -Arguments @(
    "rev-list",
    "--first-parent",
    "--parents",
    "$baseline..$Commit"
)
$history = @($historyText -split "`r?`n" | Where-Object { $_ })
if ($history.Count -ne $distance -or $history.Count -lt 1) {
    throw "first-parent release history is inconsistent"
}
Assert-ReleaseLineStartsAtBaseline `
    -Baseline $baseline `
    -OldestHistoryLine $history[$history.Count - 1]

$baseVersion = (
    Get-Content -LiteralPath (Join-Path $repositoryRoot "VERSION") -Raw -Encoding UTF8
).Trim()
$version = Get-DerivedVersion `
    -BaseVersion $baseVersion `
    -Distance $distance `
    -FirstReleaseDistance ([long]$policy.first_release_distance)
$tag = "v$version"

$orderedHistoryText = Invoke-ReleaseGit -Arguments @(
        "rev-list",
        "--first-parent",
        "--reverse",
        "$baseline..$Commit"
    )
$orderedHistory = @(
    $orderedHistoryText -split "`r?`n" | Where-Object { $_ }
)
if ($orderedHistory.Count -ne $distance -or $orderedHistory.Count -lt 1) {
    throw "release-line history is inconsistent"
}
if ($orderedHistory[$orderedHistory.Count - 1] -cne $Commit) {
    throw "release-line history does not end at CI_COMMIT_SHA"
}
$predecessor = Get-ReleasePredecessor `
    -BaseVersion $baseVersion `
    -Distance $distance `
    -FirstReleaseDistance ([long]$policy.first_release_distance) `
    -OrderedHistory $orderedHistory

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
    "LOCKER_RELEASE_TAG=$tag`n" +
    "LOCKER_PREDECESSOR_REQUIRED=$($predecessor.Required)`n" +
    "LOCKER_PREDECESSOR_TAG=$($predecessor.Tag)`n" +
    "LOCKER_PREDECESSOR_COMMIT=$($predecessor.Commit)`n" +
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

Write-Output (
    "prepared lockersm $version ($tag, first-parent distance $distance)"
)
