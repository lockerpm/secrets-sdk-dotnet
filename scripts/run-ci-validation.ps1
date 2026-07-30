$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path

function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)]
        [string] $FilePath,
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [string[]] $ArgumentList,
        [Parameter(Mandatory = $true)]
        [string] $Label
    )

    & $FilePath @ArgumentList
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "$Label failed with exit code $exitCode"
    }
}

function Invoke-CiScript {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Name,
        [string[]] $ArgumentList = @()
    )

    $script = Join-Path $PSScriptRoot $Name
    Invoke-Checked `
        -FilePath "pwsh" `
        -ArgumentList (@(
            "-NoProfile",
            "-NonInteractive",
            "-File",
            $script
        ) + $ArgumentList) `
        -Label $Name
}

function Assert-ExactSdk {
    $globalJson = Get-Content `
        -LiteralPath (Join-Path $repositoryRoot "global.json") `
        -Raw |
        ConvertFrom-Json
    $requiredSdk = [string] $globalJson.sdk.version
    $actualSdkLines = @(& dotnet --version)
    $exitCode = $LASTEXITCODE
    $actualSdk = (($actualSdkLines | ForEach-Object { "$_" }) -join "`n").Trim()
    if ($exitCode -ne 0 -or $actualSdk -cne $requiredSdk) {
        throw "required .NET SDK $requiredSdk, got $actualSdk"
    }
}

Push-Location $repositoryRoot
try {
    Invoke-CiScript -Name "verify-ci-supply-chain.ps1"
    Invoke-CiScript -Name "prepare-release.ps1" -ArgumentList @("-SelfTest")
    Invoke-CiScript `
        -Name "wait-release-predecessor.ps1" `
        -ArgumentList @("-SelfTest")
    Invoke-CiScript -Name "publish-nuget.ps1" -ArgumentList @("-SelfTest")
    Invoke-CiScript `
        -Name "create-gitlab-release.ps1" `
        -ArgumentList @("-SelfTest")

    Assert-ExactSdk
    Invoke-Checked -FilePath "dotnet" -ArgumentList @("--info") -Label "dotnet info"
    Invoke-Checked `
        -FilePath "dotnet" `
        -ArgumentList @("restore", "src/Locker.sln", "--locked-mode") `
        -Label "solution restore"
    Invoke-Checked `
        -FilePath "dotnet" `
        -ArgumentList @(
            "restore",
            "tools/Locker.ReleaseVerifier/Locker.ReleaseVerifier.csproj",
            "--locked-mode"
        ) `
        -Label "release verifier restore"
    Invoke-Checked `
        -FilePath "dotnet" `
        -ArgumentList @(
            "format",
            "src/Locker.sln",
            "--verify-no-changes",
            "--no-restore"
        ) `
        -Label "solution format"
    Invoke-Checked `
        -FilePath "dotnet" `
        -ArgumentList @(
            "format",
            "tools/Locker.ReleaseVerifier/Locker.ReleaseVerifier.csproj",
            "--verify-no-changes",
            "--no-restore"
        ) `
        -Label "release verifier format"
    Invoke-Checked `
        -FilePath "dotnet" `
        -ArgumentList @(
            "build",
            "src/Locker.sln",
            "--configuration",
            "Release",
            "--no-restore"
        ) `
        -Label "solution build"
    Invoke-Checked `
        -FilePath "dotnet" `
        -ArgumentList @(
            "test",
            "src/LockerTests/LockerTests.csproj",
            "--configuration",
            "Release",
            "--no-build",
            "--no-restore"
        ) `
        -Label "test suite"

    $artifacts = Join-Path $repositoryRoot "artifacts"
    $null = New-Item -ItemType Directory -Path $artifacts -Force
    $reportLines = @(
        & dotnet list src/Locker.sln package `
            --vulnerable `
            --include-transitive `
            --format json `
            --output-version 1
    )
    $reportExitCode = $LASTEXITCODE
    if ($reportExitCode -ne 0) {
        throw "NuGet vulnerability scan failed with exit code $reportExitCode"
    }
    $reportPath = Join-Path $artifacts "nuget-vulnerabilities.json"
    [IO.File]::WriteAllText(
        $reportPath,
        (($reportLines | ForEach-Object { "$_" }) -join "`n") + "`n",
        [Text.UTF8Encoding]::new($false)
    )
    Invoke-CiScript `
        -Name "assert-no-vulnerable-packages.ps1" `
        -ArgumentList @("-ReportPath", $reportPath)
    Invoke-Checked `
        -FilePath "dotnet" `
        -ArgumentList @(
            "pack",
            "src/Locker/Locker.csproj",
            "--configuration",
            "Release",
            "--no-build",
            "--no-restore",
            "--output",
            $artifacts
        ) `
        -Label "package build"
} finally {
    Pop-Location
}
