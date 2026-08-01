param(
    [Parameter(Mandatory = $true)]
    [string] $Tag,
    [Parameter(Mandatory = $true)]
    [string] $Commit,
    [string] $Remote = "origin"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$tagPattern = "^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$"
$commitPattern = "^(?:[0-9a-f]{40}|[0-9a-f]{64})$"
$remotePattern = "^[A-Za-z0-9._-]+$"

if (
    $Tag -notmatch $tagPattern -or
    $Commit -notmatch $commitPattern -or
    $Remote -notmatch $remotePattern
) {
    throw "remote release tag verification input is invalid"
}

$env:GIT_TERMINAL_PROMPT = "0"
$lines = @(
    & git ls-remote --tags $Remote "refs/tags/$Tag" "refs/tags/$Tag^{}" 2>$null
)
if ($LASTEXITCODE -ne 0) {
    throw "cannot query the remote release tag safely"
}

$values = @{}
foreach ($line in $lines) {
    $parts = @("$line" -split "\s+")
    if (
        $parts.Count -ne 2 -or
        $parts[0] -notmatch $commitPattern -or
        $parts[1] -notin @("refs/tags/$Tag", "refs/tags/$Tag^{}") -or
        $values.ContainsKey($parts[1])
    ) {
        throw "git returned invalid remote tag data"
    }
    $values[$parts[1]] = $parts[0]
}

if ($values.Count -gt 0) {
    if (-not $values.ContainsKey("refs/tags/$Tag")) {
        throw "git returned incomplete remote tag data"
    }
    $target = $values["refs/tags/$Tag"]
    if ($values.ContainsKey("refs/tags/$Tag^{}")) {
        $target = $values["refs/tags/$Tag^{}"]
    }
    if ($target -cne $Commit) {
        throw "remote tag $Tag already points to another commit"
    }
}

Write-Output "remote tag preflight passed for $Tag"
