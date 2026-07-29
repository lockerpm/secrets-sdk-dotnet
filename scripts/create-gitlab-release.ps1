param(
    [string] $Version = "",
    [string] $Tag = "",
    [string] $Commit = "",
    [string] $ReleasedAt = "",
    [string] $ReleaseTitle = "",
    [switch] $SelfTest
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$versionPattern = "^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$"
$commitPattern = "^(?:[0-9a-f]{40}|[0-9a-f]{64})$"

function ConvertTo-TrustedGitLabUri {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Value,
        [Parameter(Mandatory = $true)]
        [string] $Label
    )

    $uri = $null
    if (
        [string]::IsNullOrWhiteSpace($Value) -or
        $Value -cne $Value.Trim() -or
        -not [Uri]::TryCreate($Value, [UriKind]::Absolute, [ref] $uri) -or
        -not $uri.Scheme.Equals("https", [StringComparison]::OrdinalIgnoreCase) -or
        [string]::IsNullOrWhiteSpace($uri.Host) -or
        -not [string]::IsNullOrEmpty($uri.UserInfo) -or
        -not [string]::IsNullOrEmpty($uri.Query) -or
        -not [string]::IsNullOrEmpty($uri.Fragment)
    ) {
        throw (
            "$Label must be absolute HTTPS without credentials, " +
            "query, or fragment"
        )
    }
    return $uri
}

function Get-GitLabReleaseCoordinates {
    param(
        [Parameter(Mandatory = $true)]
        [string] $ApiValue,
        [Parameter(Mandatory = $true)]
        [string] $ProjectValue,
        [Parameter(Mandatory = $true)]
        [string] $ProjectId
    )

    if ($ProjectId -notmatch "^[1-9][0-9]*$") {
        throw "GitLab project ID is invalid"
    }
    $api = ConvertTo-TrustedGitLabUri `
        -Value $ApiValue `
        -Label "GitLab API URL"
    $project = ConvertTo-TrustedGitLabUri `
        -Value $ProjectValue `
        -Label "GitLab project URL"
    if (
        -not $api.IdnHost.Equals(
            $project.IdnHost,
            [StringComparison]::OrdinalIgnoreCase
        ) -or
        $api.Port -ne $project.Port
    ) {
        throw "GitLab API and project URLs must have the same HTTPS origin"
    }

    $endpoint = [UriBuilder]::new($api)
    $endpoint.Path = (
        $api.AbsolutePath.TrimEnd("/") +
        "/projects/" +
        [Uri]::EscapeDataString($ProjectId) +
        "/releases"
    )
    $endpoint.Query = ""
    $endpoint.Fragment = ""
    return [PSCustomObject] @{
        Endpoint = $endpoint.Uri.AbsoluteUri
        ProjectBase = $project.AbsoluteUri.TrimEnd("/")
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
        throw "GitLab release origin self-test expected rejection"
    }
}

if ($SelfTest) {
    $coordinates = Get-GitLabReleaseCoordinates `
        -ApiValue "https://git.example.test:443/api/v4" `
        -ProjectValue "https://git.example.test/locker/project/" `
        -ProjectId "123"
    if (
        $coordinates.Endpoint -cne (
            "https://git.example.test/api/v4/projects/123/releases"
        ) -or
        $coordinates.ProjectBase -cne (
            "https://git.example.test/locker/project"
        )
    ) {
        throw "GitLab release origin self-test produced invalid coordinates"
    }

    foreach ($candidate in @(
        @{
            Api = "http://git.example.test/api/v4"
            Project = "https://git.example.test/project"
            Id = "123"
        },
        @{
            Api = "https://attacker.example/api/v4"
            Project = "https://git.example.test/project"
            Id = "123"
        },
        @{
            Api = "https://git.example.test:444/api/v4"
            Project = "https://git.example.test/project"
            Id = "123"
        },
        @{
            Api = "https://token@git.example.test/api/v4"
            Project = "https://git.example.test/project"
            Id = "123"
        },
        @{
            Api = "https://git.example.test/api/v4?target=attacker"
            Project = "https://git.example.test/project"
            Id = "123"
        },
        @{
            Api = "https://git.example.test/api/v4"
            Project = "https://git.example.test/project#fragment"
            Id = "123"
        },
        @{
            Api = "https://git.example.test/api/v4"
            Project = "https://git.example.test/project"
            Id = "../123"
        }
    )) {
        Assert-SelfTestThrows {
            Get-GitLabReleaseCoordinates `
                -ApiValue $candidate.Api `
                -ProjectValue $candidate.Project `
                -ProjectId $candidate.Id
        }
    }
    Write-Output "GitLab release origin self-test passed"
    exit 0
}

if ($Version -notmatch $versionPattern -or $Tag -cne "v$Version") {
    throw "GitLab release version or tag is not canonical"
}
if ($Commit -notmatch $commitPattern) {
    throw "GitLab release commit is invalid"
}
if (
    [string]::IsNullOrWhiteSpace($ReleaseTitle) -or
    $ReleaseTitle -ne $ReleaseTitle.Trim() -or
    $ReleaseTitle.Length -gt 200 -or
    $ReleaseTitle.IndexOfAny([char[]]"`r`n") -ge 0
) {
    throw "GitLab release title must be one bounded printable line"
}

$requiredEnvironment = @(
    "CI_API_V4_URL",
    "CI_PROJECT_ID",
    "CI_PROJECT_URL",
    "CI_JOB_TOKEN"
)
foreach ($name in $requiredEnvironment) {
    $value = [Environment]::GetEnvironmentVariable($name)
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "missing GitLab release environment: $name"
    }
}
$coordinates = Get-GitLabReleaseCoordinates `
    -ApiValue $env:CI_API_V4_URL `
    -ProjectValue $env:CI_PROJECT_URL `
    -ProjectId $env:CI_PROJECT_ID

$released = [DateTimeOffset]::MinValue
if (-not [DateTimeOffset]::TryParse(
    $ReleasedAt,
    [Globalization.CultureInfo]::InvariantCulture,
    [Globalization.DateTimeStyles]::RoundtripKind,
    [ref]$released
)) {
    throw "GitLab release timestamp is invalid"
}

$endpoint = $coordinates.Endpoint
$versionParts = $Version.Split(".")
$changeLink = ""
$patch = [int]::Parse(
    $versionParts[2],
    [Globalization.CultureInfo]::InvariantCulture
)
if ($patch -gt 0) {
    $previous = "v$($versionParts[0]).$($versionParts[1]).$($patch - 1)"
    $changeLink = (
        "- [Changes since $previous](" +
        "$($coordinates.ProjectBase)/-/compare/$previous...$Tag)`n"
    )
}
$description = (
    "Locker Secrets .NET SDK ``$Version``.`n`n" +
    "### Changes`n`n$ReleaseTitle`n`n" +
    "- [NuGet](https://www.nuget.org/packages/lockersm/$Version)`n" +
    $changeLink +
    "- [Source commit](" +
    "$($coordinates.ProjectBase)/-/commit/$Commit)"
)
$payload = @{
    name = "Locker Secrets .NET SDK $Tag"
    tag_name = $Tag
    tag_message = "Locker Secrets .NET SDK $Tag"
    ref = $Commit
    released_at = $ReleasedAt
    description = $description
} | ConvertTo-Json -Depth 4 -Compress
$headers = @{
    "JOB-TOKEN" = $env:CI_JOB_TOKEN
    "Accept" = "application/json"
}

$release = $null
try {
    $release = Invoke-RestMethod `
        -Uri $endpoint `
        -Method Post `
        -Headers $headers `
        -ContentType "application/json" `
        -Body $payload `
        -MaximumRedirection 0 `
        -TimeoutSec 20
} catch {
    $statusCode = 0
    if ($_.Exception.Response -ne $null) {
        $statusCode = [int]$_.Exception.Response.StatusCode
    }
    if ($statusCode -notin @(400, 409)) {
        throw "GitLab release request failed"
    }
    $release = Invoke-RestMethod `
        -Uri "$endpoint/$([Uri]::EscapeDataString($Tag))" `
        -Method Get `
        -Headers $headers `
        -MaximumRedirection 0 `
        -TimeoutSec 20
}

if (
    $release.tag_name -cne $Tag -or
    $release.commit -eq $null -or
    $release.commit.id -cne $Commit
) {
    throw "existing GitLab release does not match this source commit"
}

Write-Output "GitLab release $Tag points to $Commit"
