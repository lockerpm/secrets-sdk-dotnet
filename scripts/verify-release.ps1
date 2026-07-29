param(
    [Parameter(Mandatory = $true)]
    [string] $Version,
    [Parameter(Mandatory = $true)]
    [string] $Tag,
    [Parameter(Mandatory = $true)]
    [string] $PublicKey,
    [Parameter(Mandatory = $true)]
    [string] $PackagePath,
    [Parameter(Mandatory = $true)]
    [string] $VerifierPath
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($PublicKey) -or $PublicKey -ne $PublicKey.Trim()) {
    throw "protected Locker CLI release public key is missing or malformed"
}

foreach ($path in @($PackagePath, $VerifierPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "release verifier input is missing"
    }
}

$arguments = @(
    $VerifierPath,
    "--root", $repositoryRoot,
    "--version", $Version,
    "--tag", $Tag,
    "--public-key", $PublicKey,
    "--package", $PackagePath
)
& dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
