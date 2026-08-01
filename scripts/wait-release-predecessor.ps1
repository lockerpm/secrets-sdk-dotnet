param(
    [ValidateSet(0, 1)]
    [int] $Required = 1,
    [string] $Tag = "",
    [string] $Commit = "",
    [string] $Remote = "origin",
    [ValidateRange(1, 7200)]
    [int] $TimeoutSeconds = 1800,
    [ValidateRange(1, 60)]
    [int] $PollSeconds = 10,
    [switch] $SelfTest
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$tagPattern = "^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$"
$commitPattern = "^(?:[0-9a-f]{40}|[0-9a-f]{64})$"
$remotePattern = "^[A-Za-z0-9._-]+$"

function Resolve-RemoteTagState {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [string[]] $Lines,
        [Parameter(Mandatory = $true)]
        [string] $ExpectedTag,
        [Parameter(Mandatory = $true)]
        [string] $ExpectedCommit
    )

    if ($Lines.Count -eq 0) {
        return "missing"
    }

    $values = @{}
    foreach ($line in $Lines) {
        $parts = @("$line" -split "\s+")
        if (
            $parts.Count -ne 2 -or
            $parts[0] -notmatch $commitPattern -or
            -not (@(
                "refs/tags/$ExpectedTag",
                "refs/tags/$ExpectedTag^{}"
            ) -ccontains $parts[1]) -or
            $values.ContainsKey($parts[1])
        ) {
            throw "git returned invalid predecessor tag data"
        }
        $values[$parts[1]] = $parts[0]
    }
    if (-not $values.ContainsKey("refs/tags/$ExpectedTag")) {
        throw "git returned incomplete predecessor tag data"
    }

    $target = $values["refs/tags/$ExpectedTag"]
    if ($values.ContainsKey("refs/tags/$ExpectedTag^{}")) {
        $target = $values["refs/tags/$ExpectedTag^{}"]
    }
    if ($target -cne $ExpectedCommit) {
        throw "predecessor tag $ExpectedTag points to another commit"
    }
    return "matched"
}

function Assert-SelfTestThrows {
    param(
        [Parameter(Mandatory = $true)]
        [scriptblock] $Action
    )

    try {
        & $Action
    } catch {
        return
    }
    throw "predecessor gate self-test expected a failure"
}

if ($SelfTest) {
    $expected = -join ("a" * 40)
    $tagObject = -join ("b" * 40)
    if (
        (Resolve-RemoteTagState `
            -Lines @() `
            -ExpectedTag "v1.0.0" `
            -ExpectedCommit $expected) -cne "missing"
    ) {
        throw "missing predecessor self-test failed"
    }
    if (
        (Resolve-RemoteTagState `
            -Lines @("$expected`trefs/tags/v1.0.0") `
            -ExpectedTag "v1.0.0" `
            -ExpectedCommit $expected) -cne "matched"
    ) {
        throw "lightweight predecessor self-test failed"
    }
    if (
        (Resolve-RemoteTagState `
            -Lines @(
                "$tagObject`trefs/tags/v1.0.0",
                "$expected`trefs/tags/v1.0.0^{}"
            ) `
            -ExpectedTag "v1.0.0" `
            -ExpectedCommit $expected) -cne "matched"
    ) {
        throw "annotated predecessor self-test failed"
    }
    Assert-SelfTestThrows {
        $null = Resolve-RemoteTagState `
            -Lines @("$tagObject`trefs/tags/v1.0.0") `
            -ExpectedTag "v1.0.0" `
            -ExpectedCommit $expected
    }
    Assert-SelfTestThrows {
        $null = Resolve-RemoteTagState `
            -Lines @(
                "$expected`trefs/tags/v1.0.0",
                "$expected`trefs/tags/v1.0.0"
            ) `
            -ExpectedTag "v1.0.0" `
            -ExpectedCommit $expected
    }
    Assert-SelfTestThrows {
        $null = Resolve-RemoteTagState `
            -Lines @("$expected`trefs/tags/v1.0.0^{}") `
            -ExpectedTag "v1.0.0" `
            -ExpectedCommit $expected
    }
    Write-Output "release predecessor gate self-test passed"
    exit 0
}

if ($Required -eq 0) {
    if ($Tag -ne "" -or $Commit -ne "") {
        throw "first release must not declare a predecessor"
    }
    Write-Output "first release has no predecessor tag"
    exit 0
}
if (
    $Tag -notmatch $tagPattern -or
    $Commit -notmatch $commitPattern -or
    $Remote -notmatch $remotePattern
) {
    throw "release predecessor gate input is invalid"
}
if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw "git is required to verify the release predecessor"
}

$env:GIT_TERMINAL_PROMPT = "0"
$timer = [Diagnostics.Stopwatch]::StartNew()
$lastState = "missing"
while ($true) {
    $lines = @(
        & git ls-remote `
            --tags `
            $Remote `
            "refs/tags/$Tag" `
            "refs/tags/$Tag^{}" 2>$null
    )
    $exitCode = $LASTEXITCODE
    if ($exitCode -eq 0) {
        $lastState = Resolve-RemoteTagState `
            -Lines @($lines | ForEach-Object { "$_" }) `
            -ExpectedTag $Tag `
            -ExpectedCommit $Commit
        if ($lastState -ceq "matched") {
            Write-Output "predecessor tag $Tag points to $Commit"
            exit 0
        }
    } else {
        $lastState = "remote query unavailable"
    }

    if ($timer.Elapsed.TotalSeconds -ge $TimeoutSeconds) {
        throw (
            "predecessor tag $Tag did not become valid within " +
            "$TimeoutSeconds seconds ($lastState)"
        )
    }
    $remaining = [Math]::Ceiling(
        $TimeoutSeconds - $timer.Elapsed.TotalSeconds
    )
    $sleepSeconds = [Math]::Min($PollSeconds, [int]$remaining)
    if ($sleepSeconds -lt 1) {
        $sleepSeconds = 1
    }
    Start-Sleep -Seconds $sleepSeconds
}
