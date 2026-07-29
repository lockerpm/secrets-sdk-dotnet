param(
    [Parameter(Mandatory = $true)]
    [string]$ReportPath
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$item = Get-Item -LiteralPath $ReportPath -Force
if (
    ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
    $item.PSIsContainer -or
    $item.Length -le 0 -or
    $item.Length -gt 8MB
) {
    throw "NuGet vulnerability report is not a bounded regular file"
}

$report = [IO.File]::ReadAllText(
    $item.FullName,
    [Text.UTF8Encoding]::new($false, $true)
) | ConvertFrom-Json
$vulnerabilityCount = 0

function Count-Vulnerabilities {
    param([Parameter(Mandatory = $false)]$Node)

    if ($null -eq $Node) {
        return
    }
    if ($Node -is [System.Collections.IDictionary]) {
        foreach ($key in $Node.Keys) {
            Count-Vulnerabilities -Node $Node[$key]
        }
        return
    }
    if ($Node -is [System.Collections.IEnumerable] -and $Node -isnot [string]) {
        foreach ($entry in $Node) {
            Count-Vulnerabilities -Node $entry
        }
        return
    }
    if ($Node -isnot [PSCustomObject]) {
        return
    }

    foreach ($property in $Node.PSObject.Properties) {
        if ($property.Name -eq "vulnerabilities" -and $null -ne $property.Value) {
            $script:vulnerabilityCount += @($property.Value).Count
        } else {
            Count-Vulnerabilities -Node $property.Value
        }
    }
}

Count-Vulnerabilities -Node $report
if ($vulnerabilityCount -ne 0) {
    throw "NuGet reported $vulnerabilityCount vulnerable dependency entries"
}

Write-Output "NuGet reported no vulnerable dependency entries"
