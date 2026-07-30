# Locker Secrets .NET SDK

The official .NET client for Locker Passwords & Secrets. SDK 2.x uses the stable
`locker.sdk` JSON-RPC protocol v1 over `locker sdk`; it does not parse
human-facing CLI output and supports Locker Cloud and self-hosted deployments.

## Installation

```shell
dotnet add package lockersm
```

The package does not download or execute a binary during restore, assembly
load, or client construction. Managed mode resolves the latest compatible,
signed Locker CLI on first use.

## Requirements

- .NET 8
- A Locker CLI that advertises protocol v1
- `LOCKER_ACCESS_KEY_ID` and `LOCKER_SECRET_ACCESS_KEY`

Legacy `ACCESS_KEY_ID`, `SECRET_ACCESS_KEY`, `LOCKER_ACCESS_KEY_SECRET`, and
`ACCESS_KEY_SECRET` variables are accepted only to ease migration. New
deployments should use only the canonical `LOCKER_*` names.

| Environment variable | Purpose |
| --- | --- |
| `LOCKER_ACCESS_KEY_ID` | Project access key ID in UUIDv4 form |
| `LOCKER_SECRET_ACCESS_KEY` | Non-empty canonical standard-base64 project secret |
| `LOCKER_CLI_PATH` | Absolute caller-owned CLI path |

Pass a cloud or self-hosted API base explicitly to
`LockerClientOptions`/`LockerClientFactory.FromEnvironment`; .NET does not
implicitly read an API-base environment variable.

The SDK snapshots the selected pair once, removes outer whitespace, and
validates both values before resolving, downloading, negotiating with, or
starting the CLI. Invalid local input therefore cannot cause network or
process activity and is reported as a non-retryable `AuthenticationError`
with code `-32001`.

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
    apiBase: "https://api.locker.io/locker_secrets",
    timeout: TimeSpan.FromSeconds(20),
    forceRefresh: false,
    maxAgeSeconds: 120));
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

```csharp
var created = await locker.Secrets.CreateAsync(new SecretCreateOptions
{
    Key = "PAYMENT_API_KEY",
    Value = secretFromSecureInput,
    EnvironmentName = "staging",
});

var environment = await locker.Environments.CreateAsync(
    new EnvironmentCreateOptions
    {
        Name = "staging",
        ExternalUrl = "https://staging.example.com",
    });
```

Secret DTOs contain plaintext values by design. Pass them directly to the
component that needs them; never print, serialize, or log them. Secret and
environment deletion are not part of protocol v1.

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

The transport cryptographically rebinds a managed executable to that signed
manifest plus a streamed size/SHA-256/header check immediately before every
subprocess spawn. The detached signature is verified when a generation is
installed or first loaded into the process; subsequent same-generation
rebinds use a bounded pooled buffer instead of retaining the binary in memory.
File length, timestamps, and identity metadata only optimize capability cache
invalidation; they are never a managed-binary trust decision. Same-size
in-place tampering is rejected even when mtime is restored. Explicit absolute
paths remain caller-owned and receive only the documented non-link
regular-file/identity policy, not managed-channel signature validation.

## Process security

Credentials, headers, secret values, and mutations are sent only in the JSON
request body. Child argv is exactly `sdk`; inherited child environment is a
strict operating-system allowlist that excludes Locker credentials. Requests,
stdout, and stderr are bounded. Stdout uses the smaller of the local bound and
the CLI-advertised `max_response_bytes`. Timeout or cancellation terminates
the process tree and drains both output streams concurrently. One timeout
budget covers managed resolution, capability negotiation, lock waits, and the
vault operation; those phases do not each receive a fresh timeout. Managed
installers also share a hardened process-wide HTTP connection pool while still
re-verifying the signed cache before execution. Exceptions contain
safe metadata, never raw requests or responses.

### Timeout, cancellation, retry, and vault cache

Every asynchronous operation accepts a `CancellationToken`. Cancellation or
the total `LockerClientOptions.Timeout` stops managed resolution, update-lock
waiting, capability negotiation, and the vault operation, and terminates the
CLI process tree. Cancellation after a mutation reaches the server can leave
its commit outcome unknown.

The SDK does not retry API or JSON-RPC failures. Create and update are issued
once. One internal capability rebind is allowed only when the CLI generation
changes before an exchange and the transport classifies it safe to retry.
Applications may inspect `LockerError.Retryable` and add bounded retry only to
read-only operations.

The SDK never retains plaintext secret values. `ForceRefresh` and
`MaxAgeSeconds` are delegated to the CLI's encrypted, revision-aware cache.
`MaxAgeSeconds = 0` disables offline reuse; `ForceRefresh = true` requires a
successful server response. Authentication, authorization, TLS, integrity,
malformed-response, and local-storage failures fail closed.

## Error handling

All Locker-defined failures derive from `LockerError` and expose safe
structured metadata:

```csharp
try
{
    await locker.Secrets.CreateAsync(
        new SecretCreateOptions
        {
            Key = "PAYMENT_API_KEY",
            Value = paymentApiKey,
        },
        cancellationToken);
}
catch (AlreadyExistsError)
{
    // PAYMENT_API_KEY already exists.
    // AlreadyExistsError is also a ConflictError.
}
catch (RateLimitError error) when (error.Retryable == true)
{
    // RetryAfterSeconds is an optional validated 0..86400 hint.
    throw;
}
catch (LockerError error)
{
    logger.LogError(
        "Locker failed: code={Code} kind={Kind} request={RequestId}",
        error.Code,
        error.Kind,
        error.RequestId);
    throw;
}
```

| Protocol code | Exception | Canonical kind |
| ---: | --- | --- |
| `-32700` | `ProtocolError` | `parse_error` |
| `-32600` | `ProtocolError` | `invalid_request` |
| `-32601` | `ProtocolError` | `method_not_found` |
| `-32602` | `ProtocolError` | `invalid_params` |
| `-32603` | `ProtocolError` | `internal_protocol_error` |
| `-32000` | `APIError` and legacy subtypes | `operation_error`, `request_rejected`, `response_too_large`, `cancelled` |
| `-32001` | `AuthenticationError` | `missing_credentials`, `invalid_access_key_id`, `malformed_secret_access_key`, `invalid_secret_access_key`, `unauthorized` |
| `-32003` | `PermissionDeniedError` | `forbidden`; legacy `permission_denied` |
| `-32004` | `ResourceNotFoundError` | `secret_not_found`, `environment_not_found`; legacy not-found aliases |
| `-32009` | `ConflictError` / `AlreadyExistsError` | `conflict`, `secret_already_exists`, `environment_already_exists` |
| `-32022` | `ValidationError` | `validation_error` |
| `-32029` | `RateLimitError` | `rate_limited` |
| `-32050` | `APIConnectionError` | `network_error`, `network_timeout`; legacy `http_error` |
| `-32051` | `APIServerError` | `service_unavailable`, `internal_error`; legacy `server_error` |
| `-32060` | `LocalStorageError` | `database_error`, `file_error`, `path_error` |
| `-32070` | `IntegrityError` | integrity, transport-integrity, and data-integrity kinds |

Do not log exception context that includes application variables or secret
DTOs. Classification is numeric-first. Distinctive kinds from older CLI
releases (`duplicate_hash`, `*_already_exists`, `conflict`,
`validation_error`, and the integrity aliases) also retain their typed mapping
when the legacy code is `-32000`. `request_rejected`, `response_too_large`,
and `cancelled` have explicit `APIError` subtypes but are never guessed to be
conflicts. Every `-32000` error and known authentication, permission,
not-found, conflict, validation, storage, integrity, protocol, cancellation,
and internal-server error exposes `Retryable == false`. Only rate-limit,
network, service-unavailable, or an unknown server-range code can preserve a
true hint. The SDK does not replay vault RPCs automatically.

The SDK opts in with `context.error_contract = "typed-v1"` only after the
exact contract appears in capability `error_contracts`; absence and unknown
valid contracts remain compatible and are not sent. `ServerRequestId` is a
separately validated upstream correlation ID. It never replaces the local
JSON-RPC `RequestId` and is not included in default exception text.

## Development

Builds require the exact stable .NET SDK `8.0.423`; `global.json` disables
roll-forward and prerelease SDK selection. CI uses a job-local NuGet package
directory, restores every project in locked mode, validates every locked
package content hash, and fails when NuGet reports a vulnerable direct or
transitive package. Jobs run on the `cs_newgen_docker` runner in Microsoft's
official SDK 8.0.423 Bookworm image pinned by SHA-256 digest. They never
download an SDK or install tools dynamically.

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

## Automatic releases

Every accepted two-parent merge into protected `main` releases exactly one
patch version, beginning at `2.0.0`. The version is derived from the reviewed
first-parent history in `scripts/release-policy.json`; direct, squash, rebase,
fast-forward, rewritten-baseline, and mispointed-tag histories fail closed.
Concurrent pipelines wait for the exact immediate-predecessor tag, so patch
versions cannot be skipped. The `auto-release` job also uses the
`lockersm-nuget` resource group to avoid multiple release jobs occupying the
Docker runner while waiting. Set that resource group's process mode to
`oldest_first`; GitLab's default `unordered` mode serializes jobs without
preserving release order. The predecessor-tag check remains an independent
fail-closed ordering invariant.

Provide a protected GitLab Docker runner carrying the `cs_newgen_docker` tag;
the pipeline itself selects the reviewed digest-pinned SDK image.
Protect `main`, `v*`, and the `nuget` deployment environment, then configure
GitLab so `main` accepts only merge commits with a successful pipeline. Reject
`[ci skip]` and `[skip ci]` in protected-main commit messages with a push rule,
and use a pipeline execution policy to prevent the `ci.skip` and
`ci.no_pipeline` push options where the installed GitLab tier supports it.
Otherwise one skipped merge can leave the immediate-predecessor release chain
permanently incomplete. Configure these protected variables:

- `NUGET_API_KEY`: a short-lived NuGet.org key scoped only to the
  `lockersm` package with `Push new package versions` permission. Rotate it
  before expiry. The pinned .NET 8.0.423 CLI still receives this key as a
  child-process argument, so allow release jobs only on protected refs and
  keep the runner and its job containers isolated from untrusted workloads.
- `LOCKER_CLI_RELEASE_PUBLIC_KEY`: the canonical 43-character, unpadded
  base64url raw Ed25519 public key used by the CLI release channel.

After the first pipeline creates the resource group, a Maintainer must set its
ordering once:

```shell
curl --request PUT \
  --header "PRIVATE-TOKEN: <maintainer-token>" \
  --data "process_mode=oldest_first" \
  "https://git.cystack.org/api/v4/projects/<project-id>/resource_groups/lockersm-nuget"
```

The release job builds and tests the derived version, validates the exact
package payload and embedded trust root, publishes or reconciles NuGet.org,
verifies its repository signature, and only then creates or reconciles the
GitLab tag and Release. Bounded job retries are safe: immutable executable and
metadata payloads must match, while NuGet's regenerated OPC relationship and
core-properties identifiers are parsed and compared semantically.

Report security issues privately to <contact@locker.io>.

## Migration, troubleshooting, and support

SDK 2.x is the stable protocol-v1 boundary. Replace direct REST calls,
human-output parsing, relative CLI names, and legacy credential variables
with typed services, managed mode or an absolute caller-owned path, and the
canonical `LOCKER_*` pair.

- `missing_credentials`: set both canonical credential variables.
- `invalid_access_key_id`: use the UUIDv4 access key ID exactly as issued.
- `malformed_secret_access_key`: use the complete canonical standard-base64
  secret access key; do not use base64url, remove padding, or join wrapped
  lines.
- `invalid_secret_access_key`: the well-formed secret does not match the
  access key ID. Replace the pair together rather than mixing rotated values.
- `unauthorized`: the server rejected a well-formed pair; verify its active
  status and project/environment scope.
- Permission errors: verify the credential's project/environment scope.
- `LockerCliDistributionUnavailableError`: check system time, HTTPS access to
  `files.locker.io`, and private ownership below
  `~/.locker/sdk-cli/dotnet`.
- `CliRunError` / `LockerTimeoutError`: check the absolute path, timeout,
  cancellation source, and host process policy.
- `ProtocolError`: upgrade the SDK and CLI together or remove an incompatible
  explicit `LOCKER_CLI_PATH`.
- Unexpected stale reads: use `ForceRefresh`; never loosen cache permissions.

Product help is available at [support.locker.io](https://support.locker.io).

## License

Apache License 2.0. See [LICENSE](LICENSE).
