param(
    [switch] $SelfTest
)

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

function Require-Environment {
    param([Parameter(Mandatory = $true)][string] $Name)

    $value = [Environment]::GetEnvironmentVariable($Name)
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "$Name protected variable is missing"
    }
    return $value
}

Push-Location $repositoryRoot
try {
    Invoke-CiScript -Name "verify-ci-supply-chain.ps1"
    Assert-ExactSdk

    if ($SelfTest) {
        Invoke-CiScript -Name "prepare-release.ps1" -ArgumentList @("-SelfTest")
        Invoke-CiScript -Name "publish-nuget.ps1" -ArgumentList @("-SelfTest")
        Invoke-CiScript `
            -Name "create-gitlab-release.ps1" `
            -ArgumentList @("-SelfTest")
        Write-Output "release CI orchestration self-test passed"
        return
    }

    if ($env:CI_COMMIT_REF_PROTECTED -cne "true") {
        throw "release tags must be protected"
    }
    $null = Require-Environment -Name "NUGET_API_KEY"
    $publicKey = Require-Environment -Name "LOCKER_CLI_RELEASE_PUBLIC_KEY"
    $commit = Require-Environment -Name "CI_COMMIT_SHA"
    $tag = Require-Environment -Name "CI_COMMIT_TAG"
    $releasedAt = Require-Environment -Name "CI_COMMIT_TIMESTAMP"
    $releaseTitle = Require-Environment -Name "CI_COMMIT_TITLE"

    Invoke-Checked `
        -FilePath "git" `
        -ArgumentList @("fetch", "--force", "origin", "main") `
        -Label "release main fetch"
    Invoke-CiScript `
        -Name "prepare-release.ps1" `
        -ArgumentList @("-Tag", $tag, "-Commit", $commit, "-DotEnv", "release.env")

    $requiredDotEnvNames = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal
    )
    foreach ($name in @(
        "LOCKER_SDK_VERSION",
        "LOCKER_RELEASE_TAG",
        "SOURCE_DATE_EPOCH"
    )) {
        $null = $requiredDotEnvNames.Add($name)
    }
    $seenDotEnvNames = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal
    )
    foreach ($line in [IO.File]::ReadAllLines(
        (Join-Path $repositoryRoot "release.env"),
        [Text.UTF8Encoding]::new($false, $true)
    )) {
        $parts = $line -split "=", 2
        if (
            $parts.Count -ne 2 -or
            $parts[0] -notmatch "^[A-Z0-9_]+$" -or
            -not $requiredDotEnvNames.Contains($parts[0]) -or
            -not $seenDotEnvNames.Add($parts[0])
        ) {
            throw "invalid release dotenv"
        }
        [Environment]::SetEnvironmentVariable(
            $parts[0],
            $parts[1],
            [EnvironmentVariableTarget]::Process
        )
    }
    if ($seenDotEnvNames.Count -ne $requiredDotEnvNames.Count) {
        throw "release dotenv is incomplete"
    }

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

    $version = $env:LOCKER_SDK_VERSION
    $versionProperties = @(
        "-p:Version=$version",
        "-p:PackageVersion=$version"
    )
    Invoke-Checked `
        -FilePath "dotnet" `
        -ArgumentList (@(
            "build",
            "src/Locker.sln",
            "--configuration",
            "Release",
            "--no-restore",
            "-p:ContinuousIntegrationBuild=true"
        ) + $versionProperties) `
        -Label "release build"
    Invoke-Checked `
        -FilePath "dotnet" `
        -ArgumentList (@(
            "test",
            "src/LockerTests/LockerTests.csproj",
            "--configuration",
            "Release",
            "--no-build",
            "--no-restore"
        ) + $versionProperties) `
        -Label "release tests"

    $releaseRoot = Join-Path $repositoryRoot "release-dist"
    $null = New-Item -ItemType Directory -Path $releaseRoot -Force
    Invoke-Checked `
        -FilePath "dotnet" `
        -ArgumentList (@(
            "pack",
            "src/Locker/Locker.csproj",
            "--configuration",
            "Release",
            "--no-build",
            "--no-restore",
            "--output",
            $releaseRoot
        ) + $versionProperties) `
        -Label "release package"
    $verifierRoot = Join-Path $releaseRoot "release-verifier"
    Invoke-Checked `
        -FilePath "dotnet" `
        -ArgumentList @(
            "publish",
            "tools/Locker.ReleaseVerifier/Locker.ReleaseVerifier.csproj",
            "--configuration",
            "Release",
            "--no-restore",
            "--output",
            $verifierRoot
        ) `
        -Label "release verifier publish"

    $package = Join-Path $releaseRoot "lockersm.$version.nupkg"
    $verifier = Join-Path $verifierRoot "Locker.ReleaseVerifier.dll"
    Invoke-CiScript `
        -Name "verify-release.ps1" `
        -ArgumentList @(
            "-Version",
            $version,
            "-Tag",
            $env:LOCKER_RELEASE_TAG,
            "-PublicKey",
            $publicKey,
            "-PackagePath",
            $package,
            "-VerifierPath",
            $verifier
        )
    Invoke-CiScript `
        -Name "publish-nuget.ps1" `
        -ArgumentList @("-Version", $version, "-PackagePath", $package)
    Invoke-CiScript `
        -Name "create-gitlab-release.ps1" `
        -ArgumentList @(
            "-Version",
            $version,
            "-Tag",
            $env:LOCKER_RELEASE_TAG,
            "-Commit",
            $commit,
            "-ReleasedAt",
            $releasedAt,
            "-ReleaseTitle",
            $releaseTitle
        )
} finally {
    Pop-Location
}
