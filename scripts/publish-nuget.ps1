param(
    [string] $Version = "",
    [string] $PackagePath = "",
    [string] $ApiKey = $env:NUGET_API_KEY,
    [switch] $SelfTest
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$semVerPattern = "^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$"
$maxPackageBytes = 128MB
$maxExpandedBytes = 256MB
$maxArchiveEntries = 32
$nugetSource = "https://api.nuget.org/v3/index.json"
$nugetServiceIndexPattern = (
    "https://api\.nuget\.org/v3/index\.json"
)
$relationshipsEntry = "_rels/.rels"
$relationshipsNamespace = (
    "http://schemas.openxmlformats.org/package/2006/relationships"
)
$manifestRelationshipType = (
    "http://schemas.microsoft.com/packaging/2010/07/manifest"
)
$corePropertiesRelationshipType = (
    "http://schemas.openxmlformats.org/package/2006/relationships/" +
    "metadata/core-properties"
)
$requiredPackageEntries = @(
    $relationshipsEntry,
    "[Content_Types].xml",
    "LICENSE",
    "README.md",
    "lib/net8.0/Locker.dll",
    "lib/net8.0/Locker.xml",
    "lockersm.nuspec"
)
$corePropertiesPattern = (
    "^package/services/metadata/core-properties/" +
    "[0-9a-f]{32}\.psmdcp$"
)
$signatureEntry = ".signature.p7s"

Add-Type -AssemblyName System.Net.Http
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Receive-NuGetPackage {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Destination
    )

    $handler = [Net.Http.HttpClientHandler]::new()
    $handler.AllowAutoRedirect = $false
    $client = [Net.Http.HttpClient]::new($handler)
    $client.Timeout = [TimeSpan]::FromSeconds(60)
    $response = $null
    $input = $null
    $output = $null
    try {
        $request = [Net.Http.HttpRequestMessage]::new(
            [Net.Http.HttpMethod]::Get,
            $packageUri
        )
        $response = $client.SendAsync(
            $request,
            [Net.Http.HttpCompletionOption]::ResponseHeadersRead
        ).GetAwaiter().GetResult()
        if ($response.StatusCode -eq [Net.HttpStatusCode]::NotFound) {
            return $false
        }
        if ($response.StatusCode -ne [Net.HttpStatusCode]::OK) {
            throw "NuGet package lookup returned HTTP $([int]$response.StatusCode)"
        }
        if (
            $response.Content.Headers.ContentLength -ne $null -and
            (
                $response.Content.Headers.ContentLength -lt 1 -or
                $response.Content.Headers.ContentLength -gt $maxPackageBytes
            )
        ) {
            throw "NuGet package response has an invalid size"
        }

        $input = $response.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
        $output = [IO.File]::Open(
            $Destination,
            [IO.FileMode]::CreateNew,
            [IO.FileAccess]::Write,
            [IO.FileShare]::None
        )
        $buffer = [byte[]]::new(65536)
        $total = 0L
        while (($read = $input.Read($buffer, 0, $buffer.Length)) -gt 0) {
            $total += $read
            if ($total -gt $maxPackageBytes) {
                throw "NuGet package response exceeds the size limit"
            }
            $output.Write($buffer, 0, $read)
        }
        if ($total -lt 1) {
            throw "NuGet returned an empty package"
        }
        $output.Flush($true)
        return $true
    } finally {
        if ($output -ne $null) {
            $output.Dispose()
        }
        if ($input -ne $null) {
            $input.Dispose()
        }
        if ($response -ne $null) {
            $response.Dispose()
        }
        $client.Dispose()
        $handler.Dispose()
    }
}

function Assert-SafeArchiveEntryName {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Name
    )

    if (
        [string]::IsNullOrWhiteSpace($Name) -or
        $Name.Length -gt 512 -or
        $Name.IndexOf([char] 0) -ge 0 -or
        $Name.Contains("\") -or
        $Name.Contains(":") -or
        $Name.StartsWith("/", [StringComparison]::Ordinal) -or
        $Name.EndsWith("/", [StringComparison]::Ordinal) -or
        $Name.Contains("//") -or
        $Name -match "(^|/)\.{1,2}(/|$)"
    ) {
        throw "NuGet package contains an unsafe archive entry name"
    }
}

function Get-ValidatedArchiveLayout {
    param(
        [Parameter(Mandatory = $true)]
        [IO.Compression.ZipArchive] $Archive,
        [Parameter(Mandatory = $true)]
        [bool] $RepositorySigned
    )

    if (
        $Archive.Entries.Count -lt 1 -or
        $Archive.Entries.Count -gt $maxArchiveEntries
    ) {
        throw "NuGet package archive entry count is invalid"
    }

    $names = @()
    [long] $expandedBytes = 0
    foreach ($entry in $Archive.Entries) {
        $name = $entry.FullName
        Assert-SafeArchiveEntryName -Name $name
        if ($names -ccontains $name) {
            throw "NuGet package contains a duplicate archive entry"
        }
        if ($names -icontains $name) {
            throw "NuGet package contains a case-colliding archive entry"
        }
        if ($entry.Length -lt 1 -or $entry.Length -gt $maxPackageBytes) {
            throw "NuGet package contains an empty or oversized archive entry"
        }
        if ($entry.Length -gt ($maxExpandedBytes - $expandedBytes)) {
            throw "NuGet package expanded payload exceeds the size limit"
        }
        $expandedBytes += $entry.Length
        $names += $name
    }

    $coreProperties = @(
        $names | Where-Object { $_ -cmatch $corePropertiesPattern }
    )
    if ($coreProperties.Count -ne 1) {
        throw "NuGet package must contain exactly one canonical core-properties entry"
    }

    $expectedNames = @($requiredPackageEntries) + $coreProperties[0]
    if ($RepositorySigned) {
        $expectedNames += $signatureEntry
    }
    if ($names.Count -ne $expectedNames.Count) {
        throw "NuGet package contains an unexpected archive entry"
    }
    foreach ($expectedName in $expectedNames) {
        if (-not ($names -ccontains $expectedName)) {
            throw "NuGet package is missing expected archive entry $expectedName"
        }
    }
    foreach ($name in $names) {
        if (-not ($expectedNames -ccontains $name)) {
            throw "NuGet package contains unexpected archive entry $name"
        }
    }

    return [PSCustomObject] @{
        Names = [string[]] $names
        CoreProperties = $coreProperties[0]
    }
}

function Get-ArchiveEntryDigest {
    param(
        [Parameter(Mandatory = $true)]
        [IO.Compression.ZipArchive] $Archive,
        [Parameter(Mandatory = $true)]
        [string] $Name
    )

    $matches = @($Archive.Entries | Where-Object { $_.FullName -ceq $Name })
    if ($matches.Count -ne 1) {
        throw "NuGet package payload entry $Name is missing or duplicate"
    }
    $sha256 = [Security.Cryptography.SHA256]::Create()
    $stream = $matches[0].Open()
    try {
        return [BitConverter]::ToString(
            $sha256.ComputeHash($stream)
        ).Replace("-", "").ToLowerInvariant()
    } finally {
        $stream.Dispose()
        $sha256.Dispose()
    }
}

function Get-ValidatedPackageRelationships {
    param(
        [Parameter(Mandatory = $true)]
        [IO.Compression.ZipArchive] $Archive,
        [Parameter(Mandatory = $true)]
        [string] $CoreProperties
    )

    $matches = @(
        $Archive.Entries |
            Where-Object { $_.FullName -ceq $relationshipsEntry }
    )
    if ($matches.Count -ne 1 -or $matches[0].Length -gt 65536) {
        throw "NuGet package relationships entry is missing, duplicate, or oversized"
    }

    $settings = [Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $settings.MaxCharactersInDocument = 65536
    $document = [Xml.XmlDocument]::new()
    $document.XmlResolver = $null
    $stream = $matches[0].Open()
    $reader = $null
    try {
        $reader = [Xml.XmlReader]::Create($stream, $settings)
        $document.Load($reader)
    } catch {
        throw "NuGet package relationships entry is not safe canonical XML"
    } finally {
        if ($reader -ne $null) {
            $reader.Dispose()
        }
        $stream.Dispose()
    }

    if (
        $document.ChildNodes.Count -ne 2 -or
        $document.ChildNodes[0].NodeType -ne [Xml.XmlNodeType]::XmlDeclaration -or
        $document.ChildNodes[1].NodeType -ne [Xml.XmlNodeType]::Element
    ) {
        throw "NuGet package relationships document is not canonical"
    }
    $declaration = [Xml.XmlDeclaration] $document.ChildNodes[0]
    if (
        $declaration.Version -cne "1.0" -or
        $declaration.Encoding -cne "utf-8" -or
        $declaration.Standalone.Length -ne 0
    ) {
        throw "NuGet package relationships declaration is not canonical"
    }

    $root = $document.DocumentElement
    if (
        $null -eq $root -or
        $root.LocalName -cne "Relationships" -or
        $root.NamespaceURI -cne $relationshipsNamespace -or
        $root.Attributes.Count -ne 1 -or
        $root.GetAttribute("xmlns") -cne $relationshipsNamespace
    ) {
        throw "NuGet package relationships root is not canonical"
    }

    $relationships = (
        [Collections.Generic.Dictionary[string, object]]::new(
            [StringComparer]::Ordinal
        )
    )
    foreach ($node in $root.ChildNodes) {
        if (
            $node.NodeType -ne [Xml.XmlNodeType]::Element -or
            $node.LocalName -cne "Relationship" -or
            $node.NamespaceURI -cne $relationshipsNamespace -or
            $node.Attributes.Count -ne 3 -or
            $node.HasChildNodes
        ) {
            throw "NuGet package relationships entry has unexpected content"
        }
        foreach ($attribute in $node.Attributes) {
            if (
                $attribute.NamespaceURI.Length -ne 0 -or
                @("Id", "Target", "Type") -cnotcontains $attribute.Name
            ) {
                throw "NuGet package relationship has unexpected attributes"
            }
        }

        $type = $node.GetAttribute("Type")
        $target = $node.GetAttribute("Target")
        $id = $node.GetAttribute("Id")
        if (
            [string]::IsNullOrEmpty($type) -or
            [string]::IsNullOrEmpty($target) -or
            $id -cnotmatch "^R[0-9A-F]{16}$" -or
            $relationships.ContainsKey($type)
        ) {
            throw "NuGet package relationship is malformed or duplicate"
        }
        $relationships.Add($type, [PSCustomObject] @{
            Id = $id
            Target = $target
        })
    }

    if (
        $relationships.Count -ne 2 -or
        -not $relationships.ContainsKey($manifestRelationshipType) -or
        -not $relationships.ContainsKey($corePropertiesRelationshipType)
    ) {
        throw "NuGet package relationships set is not canonical"
    }
    $manifest = $relationships[$manifestRelationshipType]
    $core = $relationships[$corePropertiesRelationshipType]
    if (
        $manifest.Target -cne "/lockersm.nuspec" -or
        $core.Target -cne "/$CoreProperties" -or
        $manifest.Id -ceq $core.Id
    ) {
        throw "NuGet package relationship target does not match its payload"
    }

    return [PSCustomObject] @{
        ManifestId = $manifest.Id
    }
}

function Assert-SamePackageRelationships {
    param(
        [Parameter(Mandatory = $true)]
        [IO.Compression.ZipArchive] $LocalArchive,
        [Parameter(Mandatory = $true)]
        [string] $LocalCoreProperties,
        [Parameter(Mandatory = $true)]
        [IO.Compression.ZipArchive] $RemoteArchive,
        [Parameter(Mandatory = $true)]
        [string] $RemoteCoreProperties
    )

    $local = Get-ValidatedPackageRelationships `
        -Archive $LocalArchive `
        -CoreProperties $LocalCoreProperties
    $remote = Get-ValidatedPackageRelationships `
        -Archive $RemoteArchive `
        -CoreProperties $RemoteCoreProperties
    if ($local.ManifestId -cne $remote.ManifestId) {
        throw "NuGet repository package changed the manifest relationship"
    }
}

function Assert-NuGetVerificationResult {
    param(
        [Parameter(Mandatory = $true)]
        [int] $ExitCode,
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string] $Details
    )

    if ($ExitCode -ne 0) {
        throw "dotnet nuget verify --all rejected the published package`n$Details"
    }
    if ($Details -notmatch "(?im)^\s*Signature type:\s*Repository\s*$") {
        throw "NuGet package does not contain an expected repository signature"
    }
    if (
        $Details -notmatch (
            "(?im)^\s*(?:Service index|nuget-v3-service-index-url):\s*" +
            "$nugetServiceIndexPattern\s*$"
        )
    ) {
        throw "NuGet repository signature does not identify nuget.org"
    }
}

function Assert-NuGetRepositorySignature {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    $savedLanguage = $env:DOTNET_CLI_UI_LANGUAGE
    $savedSignatureVerification = $env:DOTNET_NUGET_SIGNATURE_VERIFICATION
    $verificationOutput = @()
    $verifyExitCode = -1
    try {
        $env:DOTNET_CLI_UI_LANGUAGE = "en-US"
        $env:DOTNET_NUGET_SIGNATURE_VERIFICATION = "true"
        $verificationOutput = @(
            & dotnet nuget verify $Path --all --verbosity normal 2>&1 |
                ForEach-Object { $_.ToString() }
        )
        $verifyExitCode = $LASTEXITCODE
    } finally {
        [Environment]::SetEnvironmentVariable(
            "DOTNET_CLI_UI_LANGUAGE",
            $savedLanguage,
            [EnvironmentVariableTarget]::Process
        )
        [Environment]::SetEnvironmentVariable(
            "DOTNET_NUGET_SIGNATURE_VERIFICATION",
            $savedSignatureVerification,
            [EnvironmentVariableTarget]::Process
        )
    }

    $details = $verificationOutput -join "`n"
    Assert-NuGetVerificationResult `
        -ExitCode $verifyExitCode `
        -Details $details
}

function Assert-SamePublishedPayload {
    param(
        [Parameter(Mandatory = $true)]
        [string] $LocalPackage,
        [Parameter(Mandatory = $true)]
        [string] $RemotePackage
    )

    $local = [IO.Compression.ZipFile]::OpenRead($LocalPackage)
    $remote = [IO.Compression.ZipFile]::OpenRead($RemotePackage)
    try {
        $localLayout = Get-ValidatedArchiveLayout `
            -Archive $local `
            -RepositorySigned $false
        $remoteLayout = Get-ValidatedArchiveLayout `
            -Archive $remote `
            -RepositorySigned $true

        foreach (
            $name in (
                $requiredPackageEntries |
                    Where-Object { $_ -cne $relationshipsEntry }
            )
        ) {
            $localDigest = Get-ArchiveEntryDigest -Archive $local -Name $name
            $remoteDigest = Get-ArchiveEntryDigest -Archive $remote -Name $name
            if ($localDigest -cne $remoteDigest) {
                throw "NuGet already contains different release payload bytes for $name"
            }
        }

        $localCoreDigest = Get-ArchiveEntryDigest `
            -Archive $local `
            -Name $localLayout.CoreProperties
        $remoteCoreDigest = Get-ArchiveEntryDigest `
            -Archive $remote `
            -Name $remoteLayout.CoreProperties
        if ($localCoreDigest -cne $remoteCoreDigest) {
            throw "NuGet already contains different core-properties payload bytes"
        }

        Assert-SamePackageRelationships `
            -LocalArchive $local `
            -LocalCoreProperties $localLayout.CoreProperties `
            -RemoteArchive $remote `
            -RemoteCoreProperties $remoteLayout.CoreProperties
    } finally {
        $remote.Dispose()
        $local.Dispose()
    }
}

function Assert-PublishedPackage {
    param(
        [Parameter(Mandatory = $true)]
        [string] $LocalPackage,
        [Parameter(Mandatory = $true)]
        [string] $RemotePackage
    )

    Assert-NuGetRepositorySignature -Path $RemotePackage
    Assert-SamePublishedPayload `
        -LocalPackage $LocalPackage `
        -RemotePackage $RemotePackage
}

function Assert-UnsignedPackageLayout {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $null = Get-ValidatedArchiveLayout `
            -Archive $archive `
            -RepositorySigned $false
    } finally {
        $archive.Dispose()
    }
}

function New-SelfTestArchive {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path,
        [Parameter(Mandatory = $true)]
        [object[]] $Entries
    )

    $output = [IO.File]::Open(
        $Path,
        [IO.FileMode]::CreateNew,
        [IO.FileAccess]::Write,
        [IO.FileShare]::None
    )
    $archive = $null
    try {
        $archive = [IO.Compression.ZipArchive]::new(
            $output,
            [IO.Compression.ZipArchiveMode]::Create,
            $true
        )
        foreach ($item in $Entries) {
            $entry = $archive.CreateEntry(
                [string] $item.Name,
                [IO.Compression.CompressionLevel]::Optimal
            )
            $stream = $entry.Open()
            try {
                $bytes = [Text.Encoding]::UTF8.GetBytes([string] $item.Content)
                $stream.Write($bytes, 0, $bytes.Length)
            } finally {
                $stream.Dispose()
            }
        }
    } finally {
        if ($archive -ne $null) {
            $archive.Dispose()
        }
        $output.Dispose()
    }
}

function New-SelfTestRelationships {
    param(
        [Parameter(Mandatory = $true)]
        [string] $CoreProperties,
        [Parameter(Mandatory = $true)]
        [string] $CoreRelationshipId,
        [string] $ManifestRelationshipId = "R4F9C671C9793B3F4",
        [string] $ManifestTarget = "/lockersm.nuspec",
        [string] $ExtraRelationship = ""
    )

    return (
        "<?xml version=`"1.0`" encoding=`"utf-8`"?>`n" +
        "<Relationships xmlns=`"$relationshipsNamespace`">`n" +
        "  <Relationship Type=`"$manifestRelationshipType`" " +
        "Target=`"$ManifestTarget`" Id=`"$ManifestRelationshipId`" />`n" +
        "  <Relationship Type=`"$corePropertiesRelationshipType`" " +
        "Target=`"/$CoreProperties`" Id=`"$CoreRelationshipId`" />`n" +
        $ExtraRelationship +
        "</Relationships>"
    )
}

function Assert-SelfTestRejects {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Label,
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
        throw "self-test expected rejection for $Label"
    }
}

function Invoke-SelfTest {
    $testRoot = Join-Path (
        [IO.Path]::GetTempPath()
    ) "lockersm-nuget-selftest-$([Guid]::NewGuid().ToString('N'))"
    $null = [IO.Directory]::CreateDirectory($testRoot)
    try {
        $localCoreProperties = (
            "package/services/metadata/core-properties/" +
            "0123456789abcdef0123456789abcdef.psmdcp"
        )
        $remoteCoreProperties = (
            "package/services/metadata/core-properties/" +
            "fedcba9876543210fedcba9876543210.psmdcp"
        )
        $corePropertiesContent = "core properties"
        $localEntries = @(
            [PSCustomObject] @{
                Name = $relationshipsEntry
                Content = New-SelfTestRelationships `
                    -CoreProperties $localCoreProperties `
                    -CoreRelationshipId "RE7BC68C355741FAF"
            },
            [PSCustomObject] @{
                Name = "[Content_Types].xml"
                Content = "unsigned content types"
            },
            [PSCustomObject] @{ Name = "LICENSE"; Content = "license" },
            [PSCustomObject] @{ Name = "README.md"; Content = "readme" },
            [PSCustomObject] @{
                Name = "lib/net8.0/Locker.dll"
                Content = "assembly"
            },
            [PSCustomObject] @{
                Name = "lib/net8.0/Locker.xml"
                Content = "documentation"
            },
            [PSCustomObject] @{
                Name = "lockersm.nuspec"
                Content = "manifest"
            },
            [PSCustomObject] @{
                Name = $localCoreProperties
                Content = $corePropertiesContent
            }
        )
        $remoteEntries = @(
            $localEntries |
                Where-Object {
                    $_.Name -cne $relationshipsEntry -and
                    $_.Name -cne $localCoreProperties
                } |
                ForEach-Object {
                    [PSCustomObject] @{
                        Name = $_.Name
                        Content = $_.Content
                    }
                }
        ) + [PSCustomObject] @{
            Name = $relationshipsEntry
            Content = New-SelfTestRelationships `
                -CoreProperties $remoteCoreProperties `
                -CoreRelationshipId "R704D93F472B27BB0"
        } + [PSCustomObject] @{
            Name = $remoteCoreProperties
            Content = $corePropertiesContent
        } + [PSCustomObject] @{
            Name = $signatureEntry
            Content = "repository signature"
        }

        $localPackage = Join-Path $testRoot "local.nupkg"
        $remotePackage = Join-Path $testRoot "remote.nupkg"
        New-SelfTestArchive -Path $localPackage -Entries $localEntries
        New-SelfTestArchive -Path $remotePackage -Entries $remoteEntries
        Assert-UnsignedPackageLayout -Path $localPackage
        Assert-SamePublishedPayload `
            -LocalPackage $localPackage `
            -RemotePackage $remotePackage
        $validVerification = (
            "Signature type: Repository`n" +
            "Service index: https://api.nuget.org/v3/index.json"
        )
        Assert-NuGetVerificationResult `
            -ExitCode 0 `
            -Details $validVerification
        Assert-SelfTestRejects -Label "failed signature verification" -Action {
            Assert-NuGetVerificationResult `
                -ExitCode 1 `
                -Details $validVerification
        }
        Assert-SelfTestRejects -Label "author-only signature" -Action {
            Assert-NuGetVerificationResult `
                -ExitCode 0 `
                -Details (
                    "Signature type: Author`n" +
                    "Service index: https://api.nuget.org/v3/index.json"
                )
        }
        Assert-SelfTestRejects -Label "wrong signature repository" -Action {
            Assert-NuGetVerificationResult `
                -ExitCode 0 `
                -Details (
                    "Signature type: Repository`n" +
                    "Service index: https://packages.example.invalid/v3/index.json"
                )
        }

        $variants = @(
            [PSCustomObject] @{
                Label = "unknown build target"
                Entries = @($remoteEntries) + [PSCustomObject] @{
                    Name = "build/lockersm.targets"
                    Content = "malicious target"
                }
            },
            [PSCustomObject] @{
                Label = "unknown analyzer"
                Entries = @($remoteEntries) + [PSCustomObject] @{
                    Name = "analyzers/dotnet/cs/Locker.Analyzer.dll"
                    Content = "malicious analyzer"
                }
            },
            [PSCustomObject] @{
                Label = "path traversal"
                Entries = @($remoteEntries) + [PSCustomObject] @{
                    Name = "../outside"
                    Content = "traversal"
                }
            },
            [PSCustomObject] @{
                Label = "case collision"
                Entries = @($remoteEntries) + [PSCustomObject] @{
                    Name = "readme.md"
                    Content = "collision"
                }
            },
            [PSCustomObject] @{
                Label = "duplicate entry"
                Entries = @($remoteEntries) + [PSCustomObject] @{
                    Name = "README.md"
                    Content = "duplicate"
                }
            },
            [PSCustomObject] @{
                Label = "missing repository signature"
                Entries = @(
                    $remoteEntries |
                        Where-Object { $_.Name -cne $signatureEntry }
                )
            },
            [PSCustomObject] @{
                Label = "changed core properties"
                Entries = @(
                    $remoteEntries | ForEach-Object {
                        if ($_.Name -ceq $remoteCoreProperties) {
                            [PSCustomObject] @{
                                Name = $_.Name
                                Content = "different core properties"
                            }
                        } else {
                            $_
                        }
                    }
                )
            },
            [PSCustomObject] @{
                Label = "mismatched core-properties relationship"
                Entries = @(
                    $remoteEntries | ForEach-Object {
                        if ($_.Name -ceq $relationshipsEntry) {
                            [PSCustomObject] @{
                                Name = $_.Name
                                Content = New-SelfTestRelationships `
                                    -CoreProperties $localCoreProperties `
                                    -CoreRelationshipId "R704D93F472B27BB0"
                            }
                        } else {
                            $_
                        }
                    }
                )
            },
            [PSCustomObject] @{
                Label = "changed manifest relationship"
                Entries = @(
                    $remoteEntries | ForEach-Object {
                        if ($_.Name -ceq $relationshipsEntry) {
                            [PSCustomObject] @{
                                Name = $_.Name
                                Content = New-SelfTestRelationships `
                                    -CoreProperties $remoteCoreProperties `
                                    -CoreRelationshipId "R704D93F472B27BB0" `
                                    -ManifestTarget "/different.nuspec"
                            }
                        } else {
                            $_
                        }
                    }
                )
            },
            [PSCustomObject] @{
                Label = "extra package relationship"
                Entries = @(
                    $remoteEntries | ForEach-Object {
                        if ($_.Name -ceq $relationshipsEntry) {
                            [PSCustomObject] @{
                                Name = $_.Name
                                Content = New-SelfTestRelationships `
                                    -CoreProperties $remoteCoreProperties `
                                    -CoreRelationshipId "R704D93F472B27BB0" `
                                    -ExtraRelationship (
                                        "  <Relationship Type=`"" +
                                        "https://attacker.invalid/type`" " +
                                        "Target=`"/payload`" " +
                                        "Id=`"R1111111111111111`" />`n"
                                    )
                            }
                        } else {
                            $_
                        }
                    }
                )
            }
        )

        foreach (
            $immutableEntry in (
                $requiredPackageEntries |
                    Where-Object { $_ -cne $relationshipsEntry }
            )
        ) {
            $variants += [PSCustomObject] @{
                Label = "changed immutable payload $immutableEntry"
                Entries = @(
                    $remoteEntries | ForEach-Object {
                        if ($_.Name -ceq $immutableEntry) {
                            [PSCustomObject] @{
                                Name = $_.Name
                                Content = "different immutable payload"
                            }
                        } else {
                            $_
                        }
                    }
                )
            }
        }

        $variantNumber = 0
        foreach ($variant in $variants) {
            $variantNumber++
            $variantPackage = Join-Path (
                $testRoot
            ) "variant-$variantNumber.nupkg"
            New-SelfTestArchive `
                -Path $variantPackage `
                -Entries $variant.Entries
            Assert-SelfTestRejects -Label $variant.Label -Action {
                Assert-SamePublishedPayload `
                    -LocalPackage $localPackage `
                    -RemotePackage $variantPackage
            }
        }
    } finally {
        $fullTestRoot = [IO.Path]::GetFullPath($testRoot)
        $tempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        if (
            -not $tempPrefix.EndsWith(
                [IO.Path]::DirectorySeparatorChar.ToString(),
                [StringComparison]::Ordinal
            )
        ) {
            $tempPrefix += [IO.Path]::DirectorySeparatorChar
        }
        if (
            -not $fullTestRoot.StartsWith(
                $tempPrefix,
                [StringComparison]::OrdinalIgnoreCase
            ) -or
            [IO.Path]::GetFileName($fullTestRoot) -notlike (
                "lockersm-nuget-selftest-*"
            )
        ) {
            throw "refusing unsafe self-test cleanup"
        }
        if ([IO.Directory]::Exists($fullTestRoot)) {
            [IO.Directory]::Delete($fullTestRoot, $true)
        }
    }
}

if ($SelfTest) {
    Invoke-SelfTest
    Write-Output "publish-nuget self-test passed"
    exit 0
}

if ($Version -notmatch $semVerPattern) {
    throw "NuGet release version must be stable canonical SemVer"
}
if (
    [string]::IsNullOrWhiteSpace($ApiKey) -or
    $ApiKey -ne $ApiKey.Trim() -or
    $ApiKey.Length -gt 4096
) {
    throw "NUGET_API_KEY protected variable is missing or malformed"
}
if ([string]::IsNullOrWhiteSpace($PackagePath)) {
    throw "NuGet package path is missing"
}

$packageItem = Get-Item -LiteralPath $PackagePath -Force
if (
    $packageItem.PSIsContainer -or
    ($packageItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
    $packageItem.Length -lt 1 -or
    $packageItem.Length -gt $maxPackageBytes
) {
    throw "NuGet package is not a bounded regular file"
}
if ($packageItem.Name -cne "lockersm.$Version.nupkg") {
    throw "NuGet package filename does not match the release version"
}
$package = $packageItem.FullName
Assert-UnsignedPackageLayout -Path $package

$packageUri = (
    "https://api.nuget.org/v3-flatcontainer/lockersm/" +
    "$Version/lockersm.$Version.nupkg"
)
$temporaryRoot = Join-Path (
    Split-Path -Parent ([IO.Path]::GetFullPath($package))
) ".nuget-reconcile-$([Guid]::NewGuid().ToString('N'))"
$null = New-Item -ItemType Directory -Path $temporaryRoot
$download = Join-Path $temporaryRoot "lockersm.$Version.nupkg"
try {
    if (Receive-NuGetPackage -Destination $download) {
        Assert-PublishedPackage -LocalPackage $package -RemotePackage $download
        Write-Output "NuGet lockersm $Version already contains the verified payload"
        exit 0
    }

    & dotnet nuget push $package --source $nugetSource --api-key $ApiKey
    $pushExitCode = $LASTEXITCODE

    $published = $false
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        if (Test-Path -LiteralPath $download) {
            Remove-Item -LiteralPath $download -Force
        }
        if (Receive-NuGetPackage -Destination $download) {
            Assert-PublishedPackage -LocalPackage $package -RemotePackage $download
            $published = $true
            break
        }
        if ($attempt -lt 39) {
            Start-Sleep -Seconds 15
        }
    }
    if (-not $published) {
        if ($pushExitCode -ne 0) {
            throw "NuGet push failed and no reconcilable package became visible"
        }
        throw "NuGet push completed but the package did not become visible in time"
    }
    Write-Output "published and reconciled NuGet lockersm $Version"
} finally {
    if (Test-Path -LiteralPath $download) {
        Remove-Item -LiteralPath $download -Force
    }
    if (Test-Path -LiteralPath $temporaryRoot -PathType Container) {
        Remove-Item -LiteralPath $temporaryRoot -Force
    }
}
