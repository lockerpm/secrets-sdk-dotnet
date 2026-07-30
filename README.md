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

The package embeds the production Ed25519 trust root, so managed resolution is
available without pinning a CLI version. A missing or malformed key fails
closed.

To force a signed latest check:

```csharp
using var installer = new LockerCliInstaller();
var path = await installer.InstallAsync();
```

Managed downloads are accepted only after the signed release metadata,
manifest, binary digest, detached signature, and executable platform header
all verify. Verified versions are stored privately and activated atomically.
The selected managed executable is revalidated before use, and rollback,
tampering, unsafe paths, and insecure cache ownership fail closed.

A temporary release-channel outage may reuse a previously verified compatible
binary. TLS, signature, schema, hash, platform, rollback, and local-integrity
failures never fall back to cache. Explicit absolute CLI paths remain
caller-owned and are not treated as managed, signed artifacts.

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

## Versioning

The SDK follows Semantic Versioning. NuGet packages use
`MAJOR.MINOR.PATCH`; matching source releases use
`vMAJOR.MINOR.PATCH`. See the release notes when upgrading across a major
version.

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
Report security issues privately to <contact@locker.io>.

## License

Apache License 2.0. See [LICENSE](LICENSE).
