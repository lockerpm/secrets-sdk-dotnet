# Locker Secret .NET SDK

The official .NET client for Locker Secret. Version 1 uses the stable
`locker.sdk` JSON-RPC protocol over `locker sdk`; it does not parse
human-facing CLI output.

## Requirements

- .NET 8
- A Locker CLI that advertises protocol v1
- `LOCKER_ACCESS_KEY_ID` and `LOCKER_SECRET_ACCESS_KEY`

Legacy `ACCESS_KEY_ID`, `SECRET_ACCESS_KEY`, `LOCKER_ACCESS_KEY_SECRET`, and
`ACCESS_KEY_SECRET` variables are accepted only to ease migration. New
deployments should use only the canonical `LOCKER_*` names.

## Basic use

```csharp
using Locker;

using var locker = LockerClientFactory.FromEnvironment();

// Recommended single expression for generated scanner fixes. It fails closed
// for missing secrets and every authentication, permission, transport,
// protocol, storage, network, or server error.
var databasePassword =
    LockerClientFactory.GetRequiredFromEnvironment("DATABASE_PASSWORD");

var productionSecrets = await locker.Secrets.ListAsync("production");
```

`GetOrDefault` returns a default only when Locker reports numeric JSON-RPC code
`-32004`. All other errors throw a typed `LockerError`.

```csharp
var optional = locker.Secrets.GetOrDefault("OPTIONAL_KEY", "safe-non-secret-default");
```

Applications that do not use environment configuration can construct an
immutable client explicitly:

```csharp
var locker = new LockerClient(new LockerClientOptions(
    accessKeyId: configuration.AccessKeyId,
    secretAccessKey: configuration.SecretAccessKey,
    cliPath: configuration.LockerCliPath,
    timeout: TimeSpan.FromSeconds(20)));
```

No constructor or import reads `.env`, downloads a binary, changes
permissions, or performs capability negotiation. The first vault operation
runs `system.capabilities` once per client and verifies protocol v1, the eight
base vault methods plus `system.capabilities`, and the advertised request and
response bounds. Paginated list methods are additive capabilities: an older
compatible CLI can still run base operations, while a page call fails locally
when its method was not advertised.

## Operations

The typed services expose synchronous scanner helpers and asynchronous vault
operations:

- `Secrets.GetAsync`, `ListAsync`, `CreateAsync`, `UpdateAsync`
- `Environments.GetAsync`, `ListAsync`, `CreateAsync`, `UpdateAsync`
- `Secrets.ListPageAsync` and `Environments.ListPageAsync`
- `Secrets.GetRequired` / `GetRequiredAsync`
- `Secrets.GetOrDefault` for not-found-only fallback

Use the page APIs for large vaults. A cursor is opaque and must be returned
unchanged on the next request:

```csharp
var page = await locker.Secrets.ListPageAsync(
    new SecretListPageOptions
    {
        EnvironmentName = "production",
        PageSize = 100,
    });

while (page.NextCursor is not null)
{
    page = await locker.Secrets.ListPageAsync(
        new SecretListPageOptions
        {
            EnvironmentName = "production",
            PageSize = 100,
            Cursor = page.NextCursor,
        });
}
```

The typed `SecretPage` and `EnvironmentPage` DTOs expose read-only item
collections and a nullable `NextCursor`.

If a legacy unpaginated list cannot fit the negotiated response bound, it
throws `APIError` with code `-32000`, kind `response_too_large`, and
`Retryable == false`; use `ListPageAsync` instead of retrying the same list.

To clear a secret's environment association:

```csharp
await locker.Secrets.UpdateAsync(
    "DATABASE_PASSWORD",
    new SecretUpdateOptions { ClearEnvironment = true },
    environmentName: "production");
```

## CLI resolution and installation

Resolution order is:

1. `LockerClientOptions.CliPath`
2. `LOCKER_CLI_PATH`
3. the latest fully verified managed release below
   `~/.locker/sdk-cli/dotnet/releases/2.x.y/`

An explicit option or environment path is caller-owned and bypasses all
managed update checks. It must identify an absolute regular non-link file;
bare names and relative paths are rejected, and no resolution branch consults
ambient `PATH`. Otherwise the SDK checks
`https://files.locker.io/cli/releases/latest.json` on first CLI use and then at most
once per persisted six-hour interval. Importing the assembly and constructing
a client do not perform network I/O.

The source tree and package embed the reviewed production Ed25519 trust root,
so default managed resolution is available without pinning a CLI version. A
missing or malformed key still fails closed. The tagged-package release gate
requires the embedded key to match the independent protected
`LOCKER_CLI_RELEASE_PUBLIC_KEY` variable exactly.

To force a signed latest check:

```csharp
using var installer = new LockerCliInstaller();
var path = await installer.InstallAsync();
```

The update chain is signed latest metadata, a size/SHA-bound signed manifest,
and an exact platform artifact with SHA-256 plus a raw detached Ed25519
signature. Parsing rejects duplicate keys, floats, non-ASCII strings,
non-canonical JSON/base64url, BOMs, trailing data, unknown fields, unsafe
paths, rollback, and same-version equivocation. The manifest must contain
exactly the five canonical `linux`, `darwin`, and `windows` amd64/arm64
artifacts and protocol `locker.sdk` v1 over JSON-RPC stdio.

Verified versions are immutable. A process lock protects concurrent updates;
the current reference and check state are flushed and atomically replaced.
The language-specific cache prevents cross-SDK state/lock collisions.
Existing shared `~/.locker` and `~/.locker/sdk-cli` ancestors are never
rewritten; links, unsafe ownership, or unauthorized mutation permissions are
rejected before the private .NET root is used.
Every cached binary is reverified from its signed manifest and detached
signature before use. Only a transient network/transport failure may fall
back to that fully verified cache; signature, schema, path, hash, size,
header, rollback, or local-cache failures fail closed. `system.capabilities`
is negotiated before the first vault operation.

## Process security

Credentials, headers, secret values, and mutations are sent only in the JSON
request body. Child argv is exactly `sdk`; inherited child environment is a
strict operating-system allowlist that excludes Locker credentials. Requests,
stdout, and stderr are bounded. Stdout uses the smaller of the local bound and
the CLI-advertised `max_response_bytes`. Timeout or cancellation terminates
the process tree and drains both output streams concurrently. Exceptions
contain safe metadata, never raw requests or responses.

## Development

Builds require the exact stable .NET SDK `8.0.423`; `global.json` disables
roll-forward and prerelease SDK selection. CI uses a job-local NuGet package
directory, restores every project in locked mode, validates every locked
package content hash, and fails when NuGet reports a vulnerable direct or
transitive package. The Windows runner must be provisioned ahead of time; jobs
never download an SDK or install tools dynamically.

Hermetic tests are the default and use a local fake protocol CLI:

```shell
pwsh -File scripts/verify-ci-supply-chain.ps1
dotnet restore src/Locker.sln
dotnet format src/Locker.sln --verify-no-changes
dotnet test src/Locker.sln --configuration Release
dotnet pack src/Locker/Locker.csproj --configuration Release --no-build
```

Live Locker credentials are never required by the default test suite. Any
future live suite must be separately gated by `LOCKER_RUN_LIVE_TESTS=1`.

Report security issues privately to <contact@locker.io>.

## License

Apache License 2.0. See [LICENSE](LICENSE).
