# Changelog

## 1.0.0 - 2026-07-26

- Replace the human CLI argv adapter with Locker SDK protocol v1.
- Add typed clients for all secret and environment operations.
- Add typed, bounded `SecretPage` and `EnvironmentPage` APIs for large
  collections.
- Add strict JSON-RPC response validation and numeric error mapping.
- Require the eight base vault methods, `system.capabilities`, and
  `max_response_bytes` during capability negotiation, then enforce the
  advertised response limit. Treat paginated list methods as additive and
  reject unsupported page calls locally.
- Add bounded, cancellable process transport with a credential-free child environment.
- Remove implicit `.env` loading and constructor-time binary downloads.
- Keep the obsolete human-CLI options compatibility hook non-throwing while
  directing all execution through protocol v1.
- Add signed latest-channel v2 CLI installation, immutable version caches,
  exact six-hour checks, rollback/equivocation protection, and hermetic
  security tests.
- Isolate managed state under `~/.locker/sdk-cli/dotnet`, validate shared
  ancestor ownership/permissions, require absolute regular non-link
  overrides, and remove ambient `PATH` resolution.
- Verify canonical signed latest/manifest envelopes, SHA-256, raw detached
  Ed25519 signatures, executable platform headers, and protocol compatibility
  before a managed CLI can run.
- Remove SDK-side CLI version pins and require an independently protected
  release public key to match the package-embedded trust root.
- Inspect candidate package resources as bounded PE metadata without loading
  package assemblies, bound ZIP entry inflation to declared sizes, and reject
  unreviewed NuGet dependency injection.
- Refresh Newtonsoft.Json and the .NET test toolchain to their current stable
  releases.
- Advertise the package SemVer in protocol context metadata and enforce
  consistency between `VERSION`, the project package version, and runtime
  metadata.

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.0.6] - 2024-05-09

### Changed

- Change flag `--versbose` to `--json`

### Removed

- Remove flag `--data`

### Added

- Add new flag  `--key`, `--value`, `--description`, `--environment` for create new secret
- Add new flag  `--new-key`, `--new-value`, `--new-description`, `--new-environment` for update a secret
- Add new flag  `--name`, `--url`, `--description` for create new environment
- Add new flag  `--new-name`, `--new-url`, `--new-description` for update an environment

[0.0.6]: https://git.cystack.org/locker/secrets-sdk-dotnet
