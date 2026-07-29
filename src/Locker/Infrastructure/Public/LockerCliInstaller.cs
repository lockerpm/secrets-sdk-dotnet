using System.Buffers;
using System.Net;
using System.Net.Security;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Runtime.Versioning;
using Newtonsoft.Json.Linq;

namespace Locker;

/// <summary>
/// Installs and refreshes the latest Locker CLI through the signed v2 release channel.
/// </summary>
public sealed class LockerCliInstaller : IDisposable
{
    private const string CurrentSchema = "io.locker.sdk.current-cli";
    private const string StateSchema = "io.locker.sdk.update-state";
    private const int LocalSchemaVersion = 2;
    private const int LockWaitSeconds = 30;
    private const int NetworkRetrySeconds = 60;
    private static readonly TimeSpan DefaultDownloadTimeout =
        TimeSpan.FromMinutes(2);
    private static readonly HttpClient SharedStrictHttpClient =
        CreateStrictHttpClient();

    private readonly HttpClient client;
    private readonly TimeSpan downloadTimeout;
    private readonly string managedRoot;
    private readonly ReleaseTrust trust;
    private readonly Func<DateTimeOffset> utcNow;
    private readonly bool ownsClient;
    private readonly object executionTrustSync = new();
    private CurrentReference? detachedSignatureReference;

    /// <summary>Creates an installer using the SDK-embedded release trust root.</summary>
    public LockerCliInstaller()
        : this(
            SharedStrictHttpClient,
            LockerCliResolver.GetCanonicalManagedRoot(),
            LoadBundledReleaseTrust(),
            () => DateTimeOffset.UtcNow,
            ownsClient: false,
            downloadTimeout: DefaultDownloadTimeout)
    {
    }

    internal LockerCliInstaller(
        HttpClient client,
        string managedRoot,
        ReleaseTrust trust,
        Func<DateTimeOffset>? utcNow = null,
        TimeSpan? downloadTimeout = null)
        : this(
            client,
            managedRoot,
            trust,
            utcNow ?? (() => DateTimeOffset.UtcNow),
            ownsClient: false,
            downloadTimeout ?? DefaultDownloadTimeout)
    {
    }

    private LockerCliInstaller(
        HttpClient client,
        string managedRoot,
        ReleaseTrust trust,
        Func<DateTimeOffset> utcNow,
        bool ownsClient,
        TimeSpan downloadTimeout)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(trust);
        ArgumentNullException.ThrowIfNull(utcNow);
        if (string.IsNullOrWhiteSpace(managedRoot)
            || !Path.IsPathFullyQualified(managedRoot))
        {
            throw new ArgumentException(
                "Managed Locker CLI root must be an absolute path.",
                nameof(managedRoot));
        }
        if (downloadTimeout <= TimeSpan.Zero
            || downloadTimeout > TimeSpan.FromMinutes(10))
        {
            throw new ArgumentOutOfRangeException(
                nameof(downloadTimeout),
                "The managed CLI download timeout must be between zero and ten minutes.");
        }

        this.client = client;
        this.downloadTimeout = downloadTimeout;
        this.managedRoot = Path.GetFullPath(managedRoot);
        this.trust = trust;
        this.utcNow = utcNow;
        this.ownsClient = ownsClient;
    }

    /// <summary>
    /// Forces a signed latest-channel check and returns the fully verified managed executable.
    /// </summary>
    public Task<string> InstallAsync(CancellationToken cancellationToken = default) =>
        EnsureManagedAsync(forceNetwork: true, cancellationToken);

    internal Task<string> ResolveAsync(CancellationToken cancellationToken) =>
        EnsureManagedAsync(forceNetwork: false, cancellationToken);

    internal Task<string> ResolveForExecutionAsync(
        CancellationToken cancellationToken) =>
        EnsureManagedAsync(
            forceNetwork: false,
            cancellationToken,
            reuseDetachedSignature: true);

    internal async Task<string> EnsureManagedAsync(
        bool forceNetwork,
        CancellationToken cancellationToken,
        bool reuseDetachedSignature = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var publicKey = SignedUpdateContract.DecodePublicKey(trust.PublicKey);
        ValidateTrust();
        _ = SignedUpdateContract.CurrentTarget();
        var now = utcNow().ToUniversalTime();
        if (now.ToUnixTimeSeconds() < 0)
        {
            throw new InvalidOperationException("Managed Locker CLI clock is invalid.");
        }

        EnsureManagedDirectories();
        await using var installLock = await AcquireLockAsync(cancellationToken)
            .ConfigureAwait(false);

        var state = await ReadStateAsync(cancellationToken).ConfigureAwait(false);
        VerifiedInstall? cached = null;
        CurrentReference? current = null;
        try
        {
            current = await ReadCurrentAsync(cancellationToken).ConfigureAwait(false);
            if (current is not null)
            {
                var verifyDetachedSignature =
                    !reuseDetachedSignature
                    || !HasDetachedSignatureBinding(current);
                cached = await VerifyReleaseAsync(
                    current,
                    publicKey,
                    verifyDetachedSignature,
                    cancellationToken).ConfigureAwait(false);
                if (verifyDetachedSignature)
                {
                    RememberDetachedSignatureBinding(current);
                }
            }
        }
        catch (Exception error) when (
            error is IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or CryptographicException)
        {
            var high = state is null
                ? null
                : new HighWater(
                    state.HighestVersion,
                    state.HighestSourceCommit,
                    state.HighestManifestSha256,
                    state.HighestManifestSize);
            if (high is not null)
            {
                await WriteStateAsync(
                    new UpdateState(
                        high.Version,
                        high.SourceCommit,
                        high.ManifestSha256,
                        high.ManifestSize,
                        IntegrityBlocked: true,
                        state!.LastCheckedUnix,
                        RetryAfterUnix: 0),
                    cancellationToken).ConfigureAwait(false);
            }

            throw new InvalidDataException(
                "Managed Locker CLI cache failed integrity verification.",
                error);
        }

        var highest = MergeHighWater(state, cached);
        if (!forceNetwork
            && cached is not null
            && state is not null
            && StateCoversCached(state, cached)
            && !IsCheckDue(state, now))
        {
            return cached.BinaryPath;
        }

        try
        {
            var latestBytes = await DownloadBytesAsync(
                "latest.json",
                SignedUpdateContract.MaxLatestBytes,
                expectedSize: null,
                cancellationToken).ConfigureAwait(false);
            LatestRelease latest;
            try
            {
                latest = SignedUpdateContract.ParseLatest(latestBytes, publicKey);
            }
            catch (Exception error) when (
                error is InvalidDataException or CryptographicException)
            {
                throw ManagedUpdateFailure.Integrity(
                    "Signed Locker CLI latest metadata is invalid.",
                    error);
            }

            highest = AcceptLatest(highest, latest);
            state = AcceptedLatestState(highest, state);
            await WriteStateAsync(state, cancellationToken).ConfigureAwait(false);

            var manifestBytes = await DownloadBytesAsync(
                latest.Manifest.Path,
                SignedUpdateContract.MaxManifestBytes,
                latest.Manifest.Size,
                cancellationToken).ConfigureAwait(false);
            ReleaseManifest manifest;
            try
            {
                if (!SignedUpdateContract.MatchesSha256(
                    manifestBytes,
                    latest.Manifest.Sha256))
                {
                    throw new InvalidDataException(
                        "Signed manifest digest does not match latest metadata.");
                }

                manifest = SignedUpdateContract.ParseManifest(manifestBytes, publicKey);
                SignedUpdateContract.VerifyLatestManifestBinding(
                    latest,
                    manifest,
                    manifestBytes);
            }
            catch (Exception error) when (
                error is InvalidDataException
                or CryptographicException
                or PlatformNotSupportedException)
            {
                throw ManagedUpdateFailure.Integrity(
                    "Signed Locker CLI manifest is invalid.",
                    error);
            }

            var artifact = SignedUpdateContract.SelectCurrentArtifact(manifest);
            if (cached is not null && cached.Reference.Version == manifest.Version)
            {
                if (cached.Reference.SourceCommit != manifest.SourceCommit
                    || cached.Reference.ManifestSha256 != latest.Manifest.Sha256
                    || cached.Reference.ManifestSize != latest.Manifest.Size
                    || !CryptographicOperations.FixedTimeEquals(
                        cached.ManifestBytes,
                        manifestBytes))
                {
                    throw ManagedUpdateFailure.Integrity(
                        "Signed Locker CLI manifest changed within one version.");
                }

                await WriteStateAsync(
                    SuccessfulState(highest!, now),
                    cancellationToken).ConfigureAwait(false);
                return cached.BinaryPath;
            }

            var signature = await DownloadBytesAsync(
                artifact.SignaturePath,
                SignedUpdateContract.SignatureSize,
                SignedUpdateContract.SignatureSize,
                cancellationToken).ConfigureAwait(false);
            var binary = await DownloadBytesAsync(
                artifact.Path,
                SignedUpdateContract.MaxArtifactBytes,
                artifact.Size,
                cancellationToken).ConfigureAwait(false);
            try
            {
                if (!SignedUpdateContract.MatchesSha256(binary, artifact.Sha256))
                {
                    throw new InvalidDataException(
                        "Locker CLI artifact digest does not match its manifest.");
                }

                SignedUpdateContract.VerifyArtifactHeader(binary, artifact);
                SignedUpdateContract.VerifyDetachedSignature(binary, signature, publicKey);
            }
            catch (Exception error) when (
                error is InvalidDataException or CryptographicException)
            {
                throw ManagedUpdateFailure.Integrity(
                    "Downloaded Locker CLI artifact failed verification.",
                    error);
            }

            var reference = new CurrentReference(
                manifest.Version,
                manifest.SourceCommit,
                latest.Manifest.Sha256,
                latest.Manifest.Size,
                artifact.Filename);
            var installed = await PublishReleaseAsync(
                reference,
                manifestBytes,
                signature,
                binary,
                publicKey,
                cancellationToken).ConfigureAwait(false);
            await WriteCurrentAsync(reference, cancellationToken).ConfigureAwait(false);
            await WriteStateAsync(
                SuccessfulState(highest!, now),
                cancellationToken).ConfigureAwait(false);
            return installed.BinaryPath;
        }
        catch (ManagedUpdateFailure error)
        {
            return await HandleUpdateFailureAsync(
                error,
                cached,
                state,
                highest,
                now,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(publicKey);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (ownsClient)
        {
            client.Dispose();
        }
    }

    internal static ReleaseTrust LoadBundledReleaseTrust()
    {
        using var stream = typeof(LockerCliInstaller).Assembly
            .GetManifestResourceStream("Locker.locker-cli-release.json")
            ?? throw new LockerCliDistributionUnavailableError(
                "This Locker SDK package has no embedded CLI release trust metadata.");
        if (!stream.CanRead
            || stream.Length is < 1 or > SignedUpdateContract.MaxStateBytes)
        {
            throw new LockerCliDistributionUnavailableError(
                "This Locker SDK package contains invalid CLI release trust metadata.");
        }

        var bytes = new byte[checked((int)stream.Length)];
        try
        {
            stream.ReadExactly(bytes);
            return SignedUpdateContract.ParseReleaseTrust(bytes);
        }
        catch (Exception error) when (
            error is IOException
            or InvalidDataException
            or NotSupportedException)
        {
            throw new LockerCliDistributionUnavailableError(
                "This Locker SDK package contains invalid CLI release trust metadata.");
        }
    }

    private async Task<string> HandleUpdateFailureAsync(
        ManagedUpdateFailure error,
        VerifiedInstall? cached,
        UpdateState? state,
        HighWater? highest,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!error.IsTransient)
        {
            if (highest is not null)
            {
                await WriteStateAsync(
                    new UpdateState(
                        highest.Version,
                        highest.SourceCommit,
                        highest.ManifestSha256,
                        highest.ManifestSize,
                        IntegrityBlocked: true,
                        state?.LastCheckedUnix ?? 0,
                        RetryAfterUnix: 0),
                    cancellationToken).ConfigureAwait(false);
            }

            throw new InvalidDataException(error.Message, error);
        }

        if (cached is null
            || state?.IntegrityBlocked == true)
        {
            throw new HttpRequestException(error.Message, error);
        }

        highest ??= new HighWater(
            cached.Reference.Version,
            cached.Reference.SourceCommit,
            cached.Reference.ManifestSha256,
            cached.Reference.ManifestSize);
        await WriteStateAsync(
            new UpdateState(
                highest.Version,
                highest.SourceCommit,
                highest.ManifestSha256,
                highest.ManifestSize,
                IntegrityBlocked: false,
                state?.LastCheckedUnix ?? 0,
                now.AddSeconds(NetworkRetrySeconds).ToUnixTimeSeconds()),
            cancellationToken).ConfigureAwait(false);
        return cached.BinaryPath;
    }

    private HighWater AcceptLatest(HighWater? highest, LatestRelease latest)
    {
        if (highest is null)
        {
            return new HighWater(
                latest.Version,
                latest.SourceCommit,
                latest.Manifest.Sha256,
                latest.Manifest.Size);
        }

        var comparison = SignedUpdateContract.CompareVersions(
            latest.Version,
            highest.Version);
        if (comparison < 0)
        {
            throw ManagedUpdateFailure.Integrity(
                "Signed Locker CLI channel attempted a rollback.");
        }

        if (comparison == 0)
        {
            if (latest.SourceCommit != highest.SourceCommit
                || latest.Manifest.Sha256 != highest.ManifestSha256
                || latest.Manifest.Size != highest.ManifestSize)
            {
                throw ManagedUpdateFailure.Integrity(
                    "Signed Locker CLI channel changed provenance within one version.");
            }

            return highest;
        }

        return new HighWater(
            latest.Version,
            latest.SourceCommit,
            latest.Manifest.Sha256,
            latest.Manifest.Size);
    }

    private async Task<VerifiedInstall> PublishReleaseAsync(
        CurrentReference reference,
        byte[] manifest,
        byte[] signature,
        byte[] binary,
        byte[] publicKey,
        CancellationToken cancellationToken)
    {
        var releasesRoot = ReleasesRoot();
        var versionRoot = VersionRoot(reference.Version);
        if (Directory.Exists(versionRoot))
        {
            var existing = await VerifyReleaseAsync(
                reference,
                publicKey,
                verifyDetachedSignature: true,
                cancellationToken).ConfigureAwait(false);
            if (!CryptographicOperations.FixedTimeEquals(
                existing.ManifestBytes,
                manifest))
            {
                throw ManagedUpdateFailure.Integrity(
                    "Existing immutable Locker CLI release differs from signed metadata.");
            }

            RememberDetachedSignatureBinding(reference);
            return existing;
        }

        var staging = Path.Combine(releasesRoot, $".staging-{Guid.NewGuid():N}");
        EnsurePathIsDirectChild(releasesRoot, staging);
        Directory.CreateDirectory(staging);
        SetPrivateDirectory(staging);
        var published = false;
        try
        {
            await WriteNewFileAsync(
                Path.Combine(staging, "manifest.json"),
                manifest,
                executable: false,
                cancellationToken).ConfigureAwait(false);
            await WriteNewFileAsync(
                Path.Combine(staging, reference.Filename + ".sig"),
                signature,
                executable: false,
                cancellationToken).ConfigureAwait(false);
            await WriteNewFileAsync(
                Path.Combine(staging, reference.Filename),
                binary,
                executable: true,
                cancellationToken).ConfigureAwait(false);

            Directory.Move(staging, versionRoot);
            published = true;
        }
        catch (IOException) when (Directory.Exists(versionRoot))
        {
            // A process that acquired an OS-level lock before us may have
            // completed the same immutable publication. Verification below
            // decides whether it is the identical signed release.
        }
        finally
        {
            if (!published && Directory.Exists(staging))
            {
                SafeDeleteStagingDirectory(releasesRoot, staging);
            }
        }

        var result = await VerifyReleaseAsync(
            reference,
            publicKey,
            verifyDetachedSignature: true,
            cancellationToken).ConfigureAwait(false);
        if (!CryptographicOperations.FixedTimeEquals(result.ManifestBytes, manifest))
        {
            throw ManagedUpdateFailure.Integrity(
                "Published immutable Locker CLI release differs from signed metadata.");
        }

        RememberDetachedSignatureBinding(reference);
        return result;
    }

    private async Task<VerifiedInstall> VerifyReleaseAsync(
        CurrentReference reference,
        byte[] publicKey,
        bool verifyDetachedSignature,
        CancellationToken cancellationToken)
    {
        ValidateCurrentReference(reference);
        var versionRoot = VersionRoot(reference.Version);
        EnsureSafeDirectory(versionRoot, requirePrivate: true);
        var manifestPath = Path.Combine(versionRoot, "manifest.json");
        var signaturePath = Path.Combine(versionRoot, reference.Filename + ".sig");
        var binaryPath = Path.Combine(versionRoot, reference.Filename);

        var manifestBytes = await ReadRegularFileAsync(
            manifestPath,
            SignedUpdateContract.MaxManifestBytes,
            reference.ManifestSize,
            requireExecutable: false,
            cancellationToken).ConfigureAwait(false);
        if (!SignedUpdateContract.MatchesSha256(
            manifestBytes,
            reference.ManifestSha256))
        {
            throw new InvalidDataException(
                "Cached Locker CLI manifest digest is invalid.");
        }

        var manifest = SignedUpdateContract.ParseManifest(manifestBytes, publicKey);
        if (manifest.Version != reference.Version
            || manifest.SourceCommit != reference.SourceCommit)
        {
            throw new InvalidDataException(
                "Cached Locker CLI manifest provenance is invalid.");
        }

        var artifact = SignedUpdateContract.SelectCurrentArtifact(manifest);
        if (artifact.Filename != reference.Filename)
        {
            throw new InvalidDataException(
                "Cached Locker CLI artifact target is invalid.");
        }

        if (verifyDetachedSignature)
        {
            var signature = await ReadRegularFileAsync(
                signaturePath,
                SignedUpdateContract.SignatureSize,
                SignedUpdateContract.SignatureSize,
                requireExecutable: false,
                cancellationToken).ConfigureAwait(false);
            var binary = await ReadRegularFileAsync(
                binaryPath,
                SignedUpdateContract.MaxArtifactBytes,
                artifact.Size,
                requireExecutable: true,
                cancellationToken).ConfigureAwait(false);
            try
            {
                if (!SignedUpdateContract.MatchesSha256(binary, artifact.Sha256))
                {
                    throw new InvalidDataException(
                        "Cached Locker CLI artifact digest is invalid.");
                }

                SignedUpdateContract.VerifyArtifactHeader(binary, artifact);
                SignedUpdateContract.VerifyDetachedSignature(
                    binary,
                    signature,
                    publicKey);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(binary);
            }
        }
        else
        {
            await VerifyExecutableForExecutionAsync(
                binaryPath,
                artifact,
                cancellationToken).ConfigureAwait(false);
        }

        return new VerifiedInstall(reference, binaryPath, manifestBytes);
    }

    private static async Task VerifyExecutableForExecutionAsync(
        string binaryPath,
        ReleaseArtifact artifact,
        CancellationToken cancellationToken)
    {
        var before = ValidateRegularFile(
            binaryPath,
            SignedUpdateContract.MaxArtifactBytes,
            artifact.Size,
            requireExecutable: true);
        await using var input = new FileStream(
            binaryPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        byte[]? actualHash = null;
        byte[]? expectedHash = null;
        try
        {
            long total = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = await input.ReadAsync(
                    buffer.AsMemory(0, buffer.Length),
                    cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                total = checked(total + read);
                if (total > artifact.Size)
                {
                    throw new InvalidDataException(
                        "Managed Locker CLI exceeds its signed size.");
                }

                digest.AppendData(buffer, 0, read);
            }

            actualHash = digest.GetHashAndReset();
            expectedHash = Convert.FromHexString(artifact.Sha256);
            if (total != artifact.Size
                || !CryptographicOperations.FixedTimeEquals(
                    actualHash,
                    expectedHash))
            {
                throw new InvalidDataException(
                    "Cached Locker CLI artifact digest is invalid.");
            }

            await VerifyExecutableHeaderAsync(
                input,
                artifact,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Array.Clear(buffer, 0, buffer.Length);
            ArrayPool<byte>.Shared.Return(buffer);
            if (actualHash is not null)
            {
                CryptographicOperations.ZeroMemory(actualHash);
            }
            if (expectedHash is not null)
            {
                CryptographicOperations.ZeroMemory(expectedHash);
            }
        }

        var after = ValidateRegularFile(
            binaryPath,
            SignedUpdateContract.MaxArtifactBytes,
            artifact.Size,
            requireExecutable: true);
        if (after != before)
        {
            throw new InvalidDataException(
                "Managed Locker CLI file changed while reading.");
        }
    }

    private static async Task VerifyExecutableHeaderAsync(
        FileStream input,
        ReleaseArtifact artifact,
        CancellationToken cancellationToken)
    {
        var target = SignedUpdateContract.CurrentTarget();
        if (target.Filename != artifact.Filename
            || target.OS != artifact.OS
            || target.Arch != artifact.Arch)
        {
            throw new InvalidDataException(
                "Locker CLI artifact target is invalid.");
        }

        var prefixLength = artifact.OS switch
        {
            "linux" => 20,
            "darwin" => 8,
            "windows" => 64,
            _ => throw new InvalidDataException(
                "Locker CLI artifact operating system is invalid."),
        };
        var prefix = new byte[prefixLength];
        input.Position = 0;
        await input.ReadExactlyAsync(prefix, cancellationToken).ConfigureAwait(false);

        switch (artifact.OS)
        {
            case "linux":
                {
                    if (prefix[0] != 0x7f
                        || prefix[1] != (byte)'E'
                        || prefix[2] != (byte)'L'
                        || prefix[3] != (byte)'F'
                        || prefix[4] != 2
                        || prefix[5] is not (1 or 2))
                    {
                        throw new InvalidDataException(
                            "Locker CLI artifact is not a 64-bit ELF executable.");
                    }

                    var machine = prefix[5] == 1
                        ? System.Buffers.Binary.BinaryPrimitives
                            .ReadUInt16LittleEndian(prefix.AsSpan(18, 2))
                        : System.Buffers.Binary.BinaryPrimitives
                            .ReadUInt16BigEndian(prefix.AsSpan(18, 2));
                    var expected = artifact.Arch == "amd64"
                        ? (ushort)0x3e
                        : (ushort)0xb7;
                    if (machine != expected)
                    {
                        throw new InvalidDataException(
                            "Locker CLI ELF architecture is invalid.");
                    }

                    break;
                }
            case "darwin":
                {
                    if (!prefix.AsSpan(0, 4).SequenceEqual(
                        new byte[] { 0xcf, 0xfa, 0xed, 0xfe }))
                    {
                        throw new InvalidDataException(
                            "Locker CLI artifact is not a 64-bit little-endian Mach-O executable.");
                    }

                    var cpu = System.Buffers.Binary.BinaryPrimitives
                        .ReadUInt32LittleEndian(prefix.AsSpan(4, 4));
                    var expected = artifact.Arch == "amd64"
                        ? 0x01000007u
                        : 0x0100000cu;
                    if (cpu != expected)
                    {
                        throw new InvalidDataException(
                            "Locker CLI Mach-O architecture is invalid.");
                    }

                    break;
                }
            case "windows":
                {
                    if (prefix[0] != (byte)'M' || prefix[1] != (byte)'Z')
                    {
                        throw new InvalidDataException(
                            "Locker CLI artifact is not a PE executable.");
                    }

                    var headerOffset = System.Buffers.Binary.BinaryPrimitives
                        .ReadUInt32LittleEndian(prefix.AsSpan(60, 4));
                    if (headerOffset < 64 || headerOffset > artifact.Size - 6)
                    {
                        throw new InvalidDataException(
                            "Locker CLI PE architecture is invalid.");
                    }

                    input.Position = headerOffset;
                    var peHeader = new byte[6];
                    await input.ReadExactlyAsync(
                        peHeader,
                        cancellationToken).ConfigureAwait(false);
                    if (!peHeader.AsSpan(0, 4).SequenceEqual(
                            new byte[] { (byte)'P', (byte)'E', 0, 0 })
                        || System.Buffers.Binary.BinaryPrimitives
                            .ReadUInt16LittleEndian(peHeader.AsSpan(4, 2))
                            != 0x8664)
                    {
                        throw new InvalidDataException(
                            "Locker CLI PE architecture is invalid.");
                    }

                    break;
                }
        }
    }

    private async Task<byte[]> DownloadBytesAsync(
        string relativePath,
        int maximumBytes,
        long? expectedSize,
        CancellationToken cancellationToken)
    {
        var uri = BuildReleaseUri(relativePath);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        deadline.CancelAfter(downloadTimeout);
        var downloadToken = deadline.Token;
        request.Headers.AcceptEncoding.Clear();
        if (relativePath == "latest.json")
        {
            request.Headers.CacheControl =
                new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
            request.Headers.Pragma.ParseAdd("no-cache");
        }

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                downloadToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException error) when (deadline.IsCancellationRequested)
        {
            throw ManagedUpdateFailure.Transient(
                "Locker CLI release download exceeded its total deadline.",
                error);
        }
        catch (Exception error) when (
            error is HttpRequestException
            or IOException
            or TaskCanceledException)
        {
            if (IsTlsIntegrityFailure(error))
            {
                throw ManagedUpdateFailure.Integrity(
                    "Locker CLI release TLS verification failed.",
                    error);
            }

            throw ManagedUpdateFailure.Transient(
                "Locker CLI release download failed.",
                error);
        }

        using (response)
        {
            if ((int)response.StatusCode is >= 300 and < 400)
            {
                throw ManagedUpdateFailure.Integrity(
                    "Locker CLI release redirects are not allowed.");
            }

            if (response.StatusCode != HttpStatusCode.OK)
            {
                if (response.StatusCode is HttpStatusCode.RequestTimeout
                    or HttpStatusCode.TooManyRequests
                    || (int)response.StatusCode == 425
                    || (int)response.StatusCode is >= 500 and <= 599)
                {
                    throw ManagedUpdateFailure.Transient(
                        "Locker CLI release endpoint is temporarily unavailable.");
                }

                throw ManagedUpdateFailure.Fatal(
                    "Locker CLI release endpoint returned a non-success status.");
            }

            if (response.Content.Headers.ContentEncoding.Count != 0)
            {
                throw ManagedUpdateFailure.Integrity(
                    "Compressed Locker CLI release responses are not allowed.");
            }

            var contentLength = response.Content.Headers.ContentLength;
            if (contentLength > maximumBytes
                || expectedSize is not null
                && contentLength is not null
                && contentLength != expectedSize)
            {
                throw ManagedUpdateFailure.Integrity(
                    "Locker CLI release response size is invalid.");
            }

            try
            {
                await using var input = await response.Content
                    .ReadAsStreamAsync(downloadToken).ConfigureAwait(false);
                using var output = new MemoryStream(
                    expectedSize is > 0 and <= int.MaxValue
                        ? checked((int)expectedSize.Value)
                        : Math.Min(maximumBytes, 81920));
                var buffer = new byte[81920];
                while (true)
                {
                    var read = await input.ReadAsync(
                        buffer,
                        downloadToken).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    if (output.Length + read > maximumBytes)
                    {
                        throw ManagedUpdateFailure.Integrity(
                            "Locker CLI release response exceeded its size limit.");
                    }

                    await output.WriteAsync(
                        buffer.AsMemory(0, read),
                        downloadToken).ConfigureAwait(false);
                }

                if (expectedSize is not null && output.Length != expectedSize)
                {
                    throw ManagedUpdateFailure.Integrity(
                        "Locker CLI release response size does not match signed metadata.");
                }

                return output.ToArray();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException error) when (deadline.IsCancellationRequested)
            {
                throw ManagedUpdateFailure.Transient(
                    "Locker CLI release download exceeded its total deadline.",
                    error);
            }
            catch (ManagedUpdateFailure)
            {
                throw;
            }
            catch (Exception error) when (
                error is IOException
                or HttpRequestException
                or TaskCanceledException)
            {
                if (IsTlsIntegrityFailure(error))
                {
                    throw ManagedUpdateFailure.Integrity(
                        "Locker CLI release TLS verification failed.",
                        error);
                }

                throw ManagedUpdateFailure.Transient(
                    "Locker CLI release response could not be read.",
                    error);
            }
        }
    }

    private Uri BuildReleaseUri(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath)
            || relativePath.Any(character => character > 0x7f)
            || relativePath.StartsWith('/')
            || relativePath.Contains('\\')
            || relativePath.Contains('?')
            || relativePath.Contains('#')
            || relativePath.Split('/').Any(
                segment => segment.Length == 0 || segment is "." or ".."))
        {
            throw ManagedUpdateFailure.Integrity(
                "Locker CLI release path is unsafe.");
        }

        var resolved = new Uri(trust.BaseUri, relativePath);
        if (resolved.Scheme != Uri.UriSchemeHttps
            || !string.Equals(resolved.Host, trust.BaseUri.Host, StringComparison.OrdinalIgnoreCase)
            || resolved.Port != trust.BaseUri.Port
            || !string.IsNullOrEmpty(resolved.UserInfo)
            || !string.IsNullOrEmpty(resolved.Query)
            || !string.IsNullOrEmpty(resolved.Fragment)
            || !resolved.AbsolutePath.StartsWith(
                trust.BaseUri.AbsolutePath,
                StringComparison.Ordinal))
        {
            throw ManagedUpdateFailure.Integrity(
                "Locker CLI release URL escaped its trusted origin.");
        }

        return resolved;
    }

    private async Task<CurrentReference?> ReadCurrentAsync(
        CancellationToken cancellationToken)
    {
        var path = CurrentPath();
        if (!File.Exists(path))
        {
            var absent = new FileInfo(path);
            if (Directory.Exists(path) || absent.LinkTarget is not null)
            {
                throw new InvalidDataException(
                    "Managed Locker CLI current reference path is unsafe.");
            }

            return null;
        }

        var bytes = await ReadRegularFileAsync(
            path,
            SignedUpdateContract.MaxStateBytes,
            expectedSize: null,
            requireExecutable: false,
            cancellationToken).ConfigureAwait(false);
        var root = SignedUpdateContract.RequireObject(
            SignedUpdateContract.ParseStrictJson(
                bytes,
                SignedUpdateContract.MaxStateBytes),
            "managed current reference");
        SignedUpdateContract.RequireExactFields(
            root,
            "filename",
            "manifest_sha256",
            "manifest_size",
            "schema",
            "schema_version",
            "source_commit",
            "version");
        SignedUpdateContract.RequireCanonicalFile(
            root,
            bytes,
            "managed current reference");
        if (SignedUpdateContract.RequireString(root, "schema") != CurrentSchema
            || SignedUpdateContract.RequireInteger(root, "schema_version")
                != LocalSchemaVersion)
        {
            throw new InvalidDataException(
                "Managed Locker CLI current reference schema is invalid.");
        }

        var result = new CurrentReference(
            SignedUpdateContract.RequireString(root, "version"),
            SignedUpdateContract.RequireString(root, "source_commit"),
            SignedUpdateContract.RequireString(root, "manifest_sha256"),
            SignedUpdateContract.RequireInteger(root, "manifest_size"),
            SignedUpdateContract.RequireString(root, "filename"));
        ValidateCurrentReference(result);
        return result;
    }

    private async Task<UpdateState?> ReadStateAsync(CancellationToken cancellationToken)
    {
        var path = StatePath();
        if (!File.Exists(path))
        {
            var absent = new FileInfo(path);
            if (Directory.Exists(path) || absent.LinkTarget is not null)
            {
                throw new InvalidDataException(
                    "Managed Locker CLI update state path is unsafe.");
            }

            return null;
        }

        var bytes = await ReadRegularFileAsync(
            path,
            SignedUpdateContract.MaxStateBytes,
            expectedSize: null,
            requireExecutable: false,
            cancellationToken).ConfigureAwait(false);
        var root = SignedUpdateContract.RequireObject(
            SignedUpdateContract.ParseStrictJson(
                bytes,
                SignedUpdateContract.MaxStateBytes),
            "managed update state");
        SignedUpdateContract.RequireExactFields(
            root,
            "highest_manifest_sha256",
            "highest_manifest_size",
            "highest_source_commit",
            "highest_version",
            "integrity_blocked",
            "last_checked_unix",
            "retry_after_unix",
            "schema",
            "schema_version");
        SignedUpdateContract.RequireCanonicalFile(root, bytes, "managed update state");
        if (SignedUpdateContract.RequireString(root, "schema") != StateSchema
            || SignedUpdateContract.RequireInteger(root, "schema_version")
                != LocalSchemaVersion
            || root["integrity_blocked"] is not JValue
            {
                Type: JTokenType.Boolean,
                Value: bool integrityBlocked,
            })
        {
            throw new InvalidDataException(
                "Managed Locker CLI update state schema is invalid.");
        }

        var state = new UpdateState(
            SignedUpdateContract.RequireString(root, "highest_version"),
            SignedUpdateContract.RequireString(root, "highest_source_commit"),
            SignedUpdateContract.RequireString(root, "highest_manifest_sha256"),
            SignedUpdateContract.RequireInteger(root, "highest_manifest_size"),
            integrityBlocked,
            SignedUpdateContract.RequireInteger(root, "last_checked_unix"),
            SignedUpdateContract.RequireInteger(root, "retry_after_unix"));
        ValidateHighWater(new HighWater(
            state.HighestVersion,
            state.HighestSourceCommit,
            state.HighestManifestSha256,
            state.HighestManifestSize));
        if (state.LastCheckedUnix < 0 || state.RetryAfterUnix < 0)
        {
            throw new InvalidDataException(
                "Managed Locker CLI update timestamp is invalid.");
        }

        return state;
    }

    private Task WriteCurrentAsync(
        CurrentReference reference,
        CancellationToken cancellationToken)
    {
        ValidateCurrentReference(reference);
        var value = new JObject
        {
            ["filename"] = reference.Filename,
            ["manifest_sha256"] = reference.ManifestSha256,
            ["manifest_size"] = reference.ManifestSize,
            ["schema"] = CurrentSchema,
            ["schema_version"] = LocalSchemaVersion,
            ["source_commit"] = reference.SourceCommit,
            ["version"] = reference.Version,
        };
        return WriteAtomicAsync(
            CurrentPath(),
            AppendLf(SignedUpdateContract.CanonicalJson(value)),
            executable: false,
            cancellationToken);
    }

    private Task WriteStateAsync(
        UpdateState state,
        CancellationToken cancellationToken)
    {
        ValidateHighWater(new HighWater(
            state.HighestVersion,
            state.HighestSourceCommit,
            state.HighestManifestSha256,
            state.HighestManifestSize));
        if (state.LastCheckedUnix < 0 || state.RetryAfterUnix < 0)
        {
            throw new InvalidDataException(
                "Managed Locker CLI update timestamp is invalid.");
        }

        var value = new JObject
        {
            ["highest_manifest_sha256"] = state.HighestManifestSha256,
            ["highest_manifest_size"] = state.HighestManifestSize,
            ["highest_source_commit"] = state.HighestSourceCommit,
            ["highest_version"] = state.HighestVersion,
            ["integrity_blocked"] = state.IntegrityBlocked,
            ["last_checked_unix"] = state.LastCheckedUnix,
            ["retry_after_unix"] = state.RetryAfterUnix,
            ["schema"] = StateSchema,
            ["schema_version"] = LocalSchemaVersion,
        };
        return WriteAtomicAsync(
            StatePath(),
            AppendLf(SignedUpdateContract.CanonicalJson(value)),
            executable: false,
            cancellationToken);
    }

    private async Task WriteAtomicAsync(
        string destination,
        byte[] bytes,
        bool executable,
        CancellationToken cancellationToken)
    {
        EnsureSafeExistingDestination(destination);
        var directory = Path.GetDirectoryName(destination)
            ?? throw new InvalidDataException("Managed metadata directory is unavailable.");
        var temporary = Path.Combine(directory, $".metadata-{Guid.NewGuid():N}.tmp");
        EnsurePathIsDirectChild(directory, temporary);
        try
        {
            await WriteNewFileAsync(
                temporary,
                bytes,
                executable,
                cancellationToken).ConfigureAwait(false);
            File.Move(temporary, destination, overwrite: true);
            var published = await ReadRegularFileAsync(
                destination,
                Math.Max(bytes.Length, 1),
                bytes.Length,
                executable,
                cancellationToken).ConfigureAwait(false);
            if (!CryptographicOperations.FixedTimeEquals(bytes, published))
            {
                throw new InvalidDataException(
                    "Atomically published Locker CLI metadata changed unexpectedly.");
            }
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static async Task WriteNewFileAsync(
        string path,
        byte[] bytes,
        bool executable,
        CancellationToken cancellationToken)
    {
        await using var output = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            81920,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                path,
                executable
                    ? UnixFileMode.UserRead
                        | UnixFileMode.UserWrite
                        | UnixFileMode.UserExecute
                    : UnixFileMode.UserRead | UnixFileMode.UserWrite);
            VerifyUnixPathOwner(
                path,
                allowRootOwner: false,
                expectedDirectory: false);
        }
        else
        {
            SetPrivateWindowsFile(path, executable);
        }

        await output.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        output.Flush(flushToDisk: true);
    }

    private static async Task<byte[]> ReadRegularFileAsync(
        string path,
        int maximumBytes,
        long? expectedSize,
        bool requireExecutable,
        CancellationToken cancellationToken)
    {
        var before = ValidateRegularFile(
            path,
            maximumBytes,
            expectedSize,
            requireExecutable);
        var bytes = new byte[checked((int)before.Length)];
        await using var input = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await input.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        if (input.ReadByte() != -1)
        {
            throw new InvalidDataException(
                "Managed Locker CLI file changed while reading.");
        }

        var after = ValidateRegularFile(
            path,
            maximumBytes,
            expectedSize,
            requireExecutable);
        if (after != before)
        {
            throw new InvalidDataException(
                "Managed Locker CLI file changed while reading.");
        }

        return bytes;
    }

    private static RegularFileSnapshot ValidateRegularFile(
        string path,
        int maximumBytes,
        long? expectedSize,
        bool requireExecutable)
    {
        var file = new FileInfo(path);
        file.Refresh();
        if (!file.Exists
            || file.LinkTarget is not null
            || (file.Attributes & FileAttributes.ReparsePoint) != 0
            || file.Length is < 1
            || file.Length > maximumBytes
            || expectedSize is not null && file.Length != expectedSize)
        {
            throw new InvalidDataException(
                "Managed Locker CLI file is absent or unsafe.");
        }

        if (!OperatingSystem.IsWindows())
        {
            VerifyUnixPathOwner(
                path,
                allowRootOwner: false,
                expectedDirectory: false);
            var mode = File.GetUnixFileMode(path);
            if ((mode & (UnixFileMode.GroupRead
                | UnixFileMode.GroupWrite
                | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead
                | UnixFileMode.OtherWrite
                | UnixFileMode.OtherExecute)) != 0
                || requireExecutable && (mode & UnixFileMode.UserExecute) == 0)
            {
                throw new InvalidDataException(
                    "Managed Locker CLI file permissions are unsafe.");
            }
        }
        else
        {
            VerifyPrivateWindowsFile(path);
        }

        return new RegularFileSnapshot(
            file.Length,
            file.CreationTimeUtc,
            file.LastWriteTimeUtc);
    }

    private async Task<FileStream> AcquireLockAsync(CancellationToken cancellationToken)
    {
        var lockPath = Path.Combine(managedRoot, ".update.lock");
        EnsureSafeExistingDestination(lockPath);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(LockWaitSeconds);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var stream = new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    1,
                    FileOptions.WriteThrough);
                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(
                        lockPath,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite);
                    VerifyUnixPathOwner(
                        lockPath,
                        allowRootOwner: false,
                        expectedDirectory: false);
                }
                else
                {
                    SetPrivateWindowsFile(lockPath, executable: false);
                    VerifyPrivateWindowsFile(lockPath);
                }

                var info = new FileInfo(lockPath);
                info.Refresh();
                if (info.LinkTarget is not null
                    || (info.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    await stream.DisposeAsync().ConfigureAwait(false);
                    throw new InvalidDataException(
                        "Managed Locker CLI update lock is unsafe.");
                }

                return stream;
            }
            catch (IOException) when (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private void ValidateTrust()
    {
        if (trust.BaseUri.AbsoluteUri != SignedUpdateContract.ReleaseBaseUrl
            || trust.KeyId != SignedUpdateContract.ReleaseKeyId
            || trust.CheckInterval
                != TimeSpan.FromSeconds(SignedUpdateContract.CheckIntervalSeconds))
        {
            throw new InvalidDataException(
                "Locker CLI release trust coordinates are invalid.");
        }
    }

    private void EnsureManagedDirectories()
    {
        var canonicalRoot = Path.GetFullPath(
            LockerCliResolver.GetCanonicalManagedRoot());
        if (string.Equals(
            managedRoot,
            canonicalRoot,
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal))
        {
            var sdkCliRoot = Path.GetDirectoryName(canonicalRoot)
                ?? throw new InvalidDataException(
                    "Managed Locker SDK CLI root directory is unavailable.");
            var lockerRoot = Path.GetDirectoryName(sdkCliRoot)
                ?? throw new InvalidDataException(
                    "Managed Locker root directory is unavailable.");
            Directory.CreateDirectory(lockerRoot);
            EnsureSafeManagedAncestorDirectory(lockerRoot, allowRootOwner: true);
            Directory.CreateDirectory(sdkCliRoot);
            EnsureSafeManagedAncestorDirectory(sdkCliRoot, allowRootOwner: true);
        }

        Directory.CreateDirectory(managedRoot);
        EnsureSafeManagedAncestorDirectory(managedRoot, allowRootOwner: false);
        SetPrivateDirectory(managedRoot);
        EnsureSafeDirectory(managedRoot, requirePrivate: true);
        var releases = ReleasesRoot();
        Directory.CreateDirectory(releases);
        EnsureSafeDirectory(releases, requirePrivate: false);
        SetPrivateDirectory(releases);
        EnsureSafeDirectory(releases, requirePrivate: true);
    }

    internal static void EnsureSafeManagedAncestorDirectory(
        string path,
        bool allowRootOwner = true)
    {
        EnsureSafeDirectory(path, requirePrivate: false);
        if (!OperatingSystem.IsWindows())
        {
            var mode = File.GetUnixFileMode(path);
            if ((mode & (UnixFileMode.UserRead
                | UnixFileMode.UserWrite
                | UnixFileMode.UserExecute))
                    != (UnixFileMode.UserRead
                        | UnixFileMode.UserWrite
                        | UnixFileMode.UserExecute)
                || (mode & (UnixFileMode.GroupWrite
                    | UnixFileMode.OtherWrite)) != 0)
            {
                throw new InvalidDataException(
                    "Managed Locker CLI ancestor permissions are unsafe.");
            }

            VerifyUnixPathOwner(
                path,
                allowRootOwner,
                expectedDirectory: true);
            return;
        }

        VerifySafeWindowsAncestorDirectory(path);
    }

    private static void EnsureSafeDirectory(string path, bool requirePrivate)
    {
        var info = new DirectoryInfo(path);
        info.Refresh();
        if (!info.Exists
            || info.LinkTarget is not null
            || (info.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                "Managed Locker CLI directory is absent or unsafe.");
        }

        if (!OperatingSystem.IsWindows() && requirePrivate)
        {
            VerifyUnixPathOwner(
                path,
                allowRootOwner: false,
                expectedDirectory: true);
            var mode = File.GetUnixFileMode(path);
            if ((mode & (UnixFileMode.GroupRead
                | UnixFileMode.GroupWrite
                | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead
                | UnixFileMode.OtherWrite
                | UnixFileMode.OtherExecute)) != 0)
            {
                throw new InvalidDataException(
                    "Managed Locker CLI directory permissions are unsafe.");
            }
        }
        else if (OperatingSystem.IsWindows() && requirePrivate)
        {
            VerifyPrivateWindowsDirectory(path);
        }
    }

    private static void SetPrivateDirectory(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead
                | UnixFileMode.UserWrite
                | UnixFileMode.UserExecute);
        }
        else
        {
            SetPrivateWindowsDirectory(path);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void SetPrivateWindowsDirectory(string path)
    {
        var owner = CurrentWindowsOwner();
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(owner);
        security.AddAccessRule(
            new FileSystemAccessRule(
                owner,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
        AddWindowsServiceRules(security, isDirectory: true);
        new DirectoryInfo(path).SetAccessControl(security);
    }

    [SupportedOSPlatform("windows")]
    private static void SetPrivateWindowsFile(string path, bool executable)
    {
        var owner = CurrentWindowsOwner();
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(owner);
        security.AddAccessRule(
            new FileSystemAccessRule(
                owner,
                executable
                    ? FileSystemRights.ReadAndExecute
                        | FileSystemRights.Write
                        | FileSystemRights.Delete
                        | FileSystemRights.ChangePermissions
                        | FileSystemRights.TakeOwnership
                    : FileSystemRights.FullControl,
                AccessControlType.Allow));
        AddWindowsServiceRules(security, isDirectory: false);
        new FileInfo(path).SetAccessControl(security);
    }

    [SupportedOSPlatform("windows")]
    private static void AddWindowsServiceRules(
        FileSystemSecurity security,
        bool isDirectory)
    {
        var inheritance = isDirectory
            ? InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit
            : InheritanceFlags.None;
        foreach (var sid in TrustedWindowsServiceSids())
        {
            security.AddAccessRule(
                new FileSystemAccessRule(
                    sid,
                    FileSystemRights.FullControl,
                    inheritance,
                    PropagationFlags.None,
                    AccessControlType.Allow));
        }
    }

    [SupportedOSPlatform("windows")]
    private static void VerifyPrivateWindowsDirectory(string path)
    {
        var security = new DirectoryInfo(path).GetAccessControl(
            AccessControlSections.Owner | AccessControlSections.Access);
        VerifyPrivateWindowsAcl(security, "directory");
    }

    [SupportedOSPlatform("windows")]
    private static void VerifySafeWindowsAncestorDirectory(string path)
    {
        var security = new DirectoryInfo(path).GetAccessControl(
            AccessControlSections.Owner | AccessControlSections.Access);
        var owner = CurrentWindowsOwner();
        if (security.GetOwner(typeof(SecurityIdentifier))
                is not SecurityIdentifier actualOwner
            || !actualOwner.Equals(owner))
        {
            throw new InvalidDataException(
                "Managed Locker CLI Windows ancestor ownership is unsafe.");
        }

        var trusted = TrustedWindowsServiceSids()
            .Append(owner)
            .Select(sid => sid.Value)
            .ToHashSet(StringComparer.Ordinal);
        const FileSystemRights mutationRights =
            FileSystemRights.WriteData
            | FileSystemRights.AppendData
            | FileSystemRights.WriteExtendedAttributes
            | FileSystemRights.WriteAttributes
            | FileSystemRights.DeleteSubdirectoriesAndFiles
            | FileSystemRights.Delete
            | FileSystemRights.ChangePermissions
            | FileSystemRights.TakeOwnership;
        var ownerCanCreate = false;
        foreach (FileSystemAccessRule rule in security.GetAccessRules(
            includeExplicit: true,
            includeInherited: true,
            typeof(SecurityIdentifier)))
        {
            if (rule.IdentityReference is not SecurityIdentifier identity
                || rule.AccessControlType != AccessControlType.Allow)
            {
                continue;
            }

            var canMutate = (rule.FileSystemRights & mutationRights) != 0;
            if (canMutate && !trusted.Contains(identity.Value))
            {
                throw new InvalidDataException(
                    "Managed Locker CLI Windows ancestor grants mutation rights to an untrusted principal.");
            }

            if (canMutate && identity.Equals(owner))
            {
                ownerCanCreate = true;
            }
        }

        if (!ownerCanCreate)
        {
            throw new InvalidDataException(
                "Managed Locker CLI Windows ancestor is not writable by its owner.");
        }
    }

    [SupportedOSPlatform("windows")]
    private static void VerifyPrivateWindowsFile(string path)
    {
        var security = new FileInfo(path).GetAccessControl(
            AccessControlSections.Owner | AccessControlSections.Access);
        VerifyPrivateWindowsAcl(security, "file");
    }

    [SupportedOSPlatform("windows")]
    private static void VerifyPrivateWindowsAcl(
        FileSystemSecurity security,
        string label)
    {
        var owner = CurrentWindowsOwner();
        if (!security.AreAccessRulesProtected
            || security.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier actualOwner
            || !actualOwner.Equals(owner))
        {
            throw new InvalidDataException(
                $"Managed Locker CLI Windows {label} ownership or inheritance is unsafe.");
        }

        var trusted = TrustedWindowsServiceSids()
            .Append(owner)
            .Select(sid => sid.Value)
            .ToHashSet(StringComparer.Ordinal);
        var ownerCanWrite = false;
        foreach (FileSystemAccessRule rule in security.GetAccessRules(
            includeExplicit: true,
            includeInherited: true,
            typeof(SecurityIdentifier)))
        {
            if (rule.IdentityReference is not SecurityIdentifier identity
                || rule.AccessControlType != AccessControlType.Allow)
            {
                continue;
            }

            if (!trusted.Contains(identity.Value))
            {
                throw new InvalidDataException(
                    $"Managed Locker CLI Windows {label} grants access to an untrusted principal.");
            }

            if (identity.Equals(owner)
                && (rule.FileSystemRights
                    & (FileSystemRights.Write
                        | FileSystemRights.Modify
                        | FileSystemRights.FullControl)) != 0)
            {
                ownerCanWrite = true;
            }
        }

        if (!ownerCanWrite)
        {
            throw new InvalidDataException(
                $"Managed Locker CLI Windows {label} is not writable by its owner.");
        }
    }

    [SupportedOSPlatform("windows")]
    private static SecurityIdentifier CurrentWindowsOwner() =>
        WindowsIdentity.GetCurrent().User
        ?? throw new InvalidDataException(
            "The current Windows account has no security identifier.");

    [SupportedOSPlatform("windows")]
    private static SecurityIdentifier[] TrustedWindowsServiceSids() =>
    [
        new SecurityIdentifier(WellKnownSidType.LocalSystemSid, domainSid: null),
        new SecurityIdentifier(
            WellKnownSidType.BuiltinAdministratorsSid,
            domainSid: null),
    ];

    internal static void VerifyUnixPathOwner(
        string path,
        bool allowRootOwner,
        bool expectedDirectory)
    {
        var owner = GetUnixPathOwnerId(path, expectedDirectory);
        var effectiveUser = GetEffectiveUnixUserId();
        if (owner != effectiveUser
            && (!allowRootOwner || owner != 0))
        {
            throw new InvalidDataException(
                "Managed Locker CLI Unix path ownership is unsafe.");
        }
    }

    internal static uint GetEffectiveUnixUserId()
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Unix user identity is unavailable on Windows.");
        }

        return GetEffectiveUserIdNative();
    }

    internal static uint GetUnixPathOwnerId(
        string path,
        bool expectedDirectory)
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Unix file ownership is unavailable on Windows.");
        }
        if (string.IsNullOrEmpty(path) || path.Contains('\0'))
        {
            throw new InvalidDataException(
                "Managed Locker CLI Unix ownership path is invalid.");
        }

        var buffer = Marshal.AllocHGlobal(256);
        try
        {
            uint owner;
            ushort mode;
            if (OperatingSystem.IsLinux())
            {
                var systemCall = RuntimeInformation.ProcessArchitecture switch
                {
                    Architecture.X64 => 332L,
                    Architecture.Arm64 => 291L,
                    _ => throw new PlatformNotSupportedException(
                        "Managed Locker CLI ownership checks require Linux amd64 or arm64."),
                };
                var pathBytes = Marshal.StringToCoTaskMemUTF8(path);
                try
                {
                    const long currentWorkingDirectory = -100;
                    const long noFollow = 0x100;
                    const long basicStats = 0x7ff;
                    var result = UnixSystemCall(
                        systemCall,
                        currentWorkingDirectory,
                        pathBytes,
                        noFollow,
                        basicStats,
                        buffer);
                    if (result != 0)
                    {
                        var error = Marshal.GetLastPInvokeError();
                        throw new InvalidDataException(
                            "Managed Locker CLI Unix ownership metadata is unavailable.",
                            new Win32Exception(error));
                    }
                }
                finally
                {
                    Marshal.FreeCoTaskMem(pathBytes);
                }

                const uint requiredMask = 0x0b;
                var returnedMask = unchecked((uint)Marshal.ReadInt32(buffer, 0));
                if ((returnedMask & requiredMask) != requiredMask)
                {
                    throw new InvalidDataException(
                        "Managed Locker CLI Linux ownership metadata is incomplete.");
                }

                owner = unchecked((uint)Marshal.ReadInt32(buffer, 20));
                mode = unchecked((ushort)Marshal.ReadInt16(buffer, 28));
            }
            else if (OperatingSystem.IsMacOS())
            {
                var pathBytes = Marshal.StringToCoTaskMemUTF8(path);
                try
                {
                    var result = RuntimeInformation.ProcessArchitecture switch
                    {
                        Architecture.X64 => MacLStat64(pathBytes, buffer),
                        Architecture.Arm64 => MacLStat(pathBytes, buffer),
                        _ => throw new PlatformNotSupportedException(
                            "Managed Locker CLI ownership checks require macOS amd64 or arm64."),
                    };
                    if (result != 0)
                    {
                        var error = Marshal.GetLastPInvokeError();
                        throw new InvalidDataException(
                            "Managed Locker CLI Unix ownership metadata is unavailable.",
                            new Win32Exception(error));
                    }
                }
                finally
                {
                    Marshal.FreeCoTaskMem(pathBytes);
                }

                owner = unchecked((uint)Marshal.ReadInt32(buffer, 16));
                mode = unchecked((ushort)Marshal.ReadInt16(buffer, 4));
            }
            else
            {
                throw new PlatformNotSupportedException(
                    "Managed Locker CLI ownership checks require Linux or macOS.");
            }

            const ushort fileTypeMask = 0xf000;
            const ushort directoryType = 0x4000;
            const ushort regularFileType = 0x8000;
            var expectedType = expectedDirectory
                ? directoryType
                : regularFileType;
            if ((mode & fileTypeMask) != expectedType)
            {
                throw new InvalidDataException(
                    "Managed Locker CLI Unix path type is unsafe.");
            }

            return owner;
        }
        catch (Exception error) when (
            error is DllNotFoundException
            or EntryPointNotFoundException)
        {
            throw new InvalidDataException(
                "Managed Locker CLI Unix ownership verification is unavailable.",
                error);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [DllImport("libc", EntryPoint = "geteuid", ExactSpelling = true)]
    private static extern uint GetEffectiveUserIdNative();

    [DllImport("libc", EntryPoint = "syscall", ExactSpelling = true, SetLastError = true)]
    private static extern long UnixSystemCall(
        long number,
        long firstArgument,
        IntPtr secondArgument,
        long thirdArgument,
        long fourthArgument,
        IntPtr fifthArgument);

    [DllImport("libc", EntryPoint = "lstat", ExactSpelling = true, SetLastError = true)]
    private static extern int MacLStat(
        IntPtr path,
        IntPtr buffer);

    [DllImport("libc", EntryPoint = "lstat64", ExactSpelling = true, SetLastError = true)]
    private static extern int MacLStat64(
        IntPtr path,
        IntPtr buffer);

    private static void EnsureSafeExistingDestination(string path)
    {
        if (Directory.Exists(path))
        {
            throw new InvalidDataException(
                "Managed Locker CLI destination is a directory.");
        }

        var info = new FileInfo(path);
        info.Refresh();
        if (info.Exists
            && (info.LinkTarget is not null
                || (info.Attributes & FileAttributes.ReparsePoint) != 0
                || (info.Attributes & FileAttributes.Directory) != 0))
        {
            throw new InvalidDataException(
                "Managed Locker CLI destination is unsafe.");
        }
    }

    private static void EnsurePathIsDirectChild(string parent, string child)
    {
        var resolvedParent = Path.GetFullPath(parent)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var resolvedChild = Path.GetFullPath(child);
        if (!string.Equals(
            Path.GetDirectoryName(resolvedChild),
            resolvedParent,
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Managed Locker CLI temporary path escaped its directory.");
        }
    }

    private static void SafeDeleteStagingDirectory(string releasesRoot, string staging)
    {
        EnsurePathIsDirectChild(releasesRoot, staging);
        if (!Path.GetFileName(staging).StartsWith(".staging-", StringComparison.Ordinal)
            || Path.GetFileName(staging).Length != ".staging-".Length + 32)
        {
            throw new InvalidDataException(
                "Managed Locker CLI staging path is unsafe.");
        }

        Directory.Delete(staging, recursive: true);
    }

    private static bool IsTlsIntegrityFailure(Exception error)
    {
        for (Exception? current = error; current is not null; current = current.InnerException)
        {
            if (current is AuthenticationException
                || current is HttpRequestException
                {
                    HttpRequestError: HttpRequestError.SecureConnectionError,
                })
            {
                return true;
            }
        }

        return false;
    }

    private static HttpClient CreateStrictHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            MaxConnectionsPerServer = 16,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ResponseDrainTimeout = TimeSpan.FromSeconds(2),
            SslOptions = new SslClientAuthenticationOptions
            {
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            },
        };
        return new HttpClient(handler)
        {
            Timeout = System.Threading.Timeout.InfiniteTimeSpan,
        };
    }

    private static HighWater? MergeHighWater(
        UpdateState? state,
        VerifiedInstall? cached)
    {
        HighWater? result = state is null
            ? null
            : new HighWater(
                state.HighestVersion,
                state.HighestSourceCommit,
                state.HighestManifestSha256,
                state.HighestManifestSize);
        if (cached is null)
        {
            return result;
        }

        var current = new HighWater(
            cached.Reference.Version,
            cached.Reference.SourceCommit,
            cached.Reference.ManifestSha256,
            cached.Reference.ManifestSize);
        if (result is null)
        {
            return current;
        }

        var comparison = SignedUpdateContract.CompareVersions(
            result.Version,
            current.Version);
        if (comparison == 0
            && (result.SourceCommit != current.SourceCommit
                || result.ManifestSha256 != current.ManifestSha256
                || result.ManifestSize != current.ManifestSize))
        {
            throw new InvalidDataException(
                "Managed Locker CLI state conflicts with the verified cache.");
        }

        return comparison >= 0 ? result : current;
    }

    private static bool StateCoversCached(
        UpdateState state,
        VerifiedInstall cached)
    {
        var comparison = SignedUpdateContract.CompareVersions(
            state.HighestVersion,
            cached.Reference.Version);
        return comparison > 0
            || comparison == 0
            && state.HighestSourceCommit == cached.Reference.SourceCommit
            && state.HighestManifestSha256 == cached.Reference.ManifestSha256
            && state.HighestManifestSize == cached.Reference.ManifestSize;
    }

    private bool IsCheckDue(UpdateState state, DateTimeOffset now)
    {
        if (state.IntegrityBlocked)
        {
            return true;
        }

        if (state.RetryAfterUnix > 0)
        {
            try
            {
                if (now < DateTimeOffset.FromUnixTimeSeconds(state.RetryAfterUnix))
                {
                    return false;
                }
            }
            catch (ArgumentOutOfRangeException)
            {
                return true;
            }
        }

        DateTimeOffset last;
        try
        {
            last = DateTimeOffset.FromUnixTimeSeconds(state.LastCheckedUnix);
        }
        catch (ArgumentOutOfRangeException)
        {
            return true;
        }

        return now < last || now - last >= trust.CheckInterval;
    }

    private static UpdateState AcceptedLatestState(
        HighWater high,
        UpdateState? previous) =>
        new(
            high.Version,
            high.SourceCommit,
            high.ManifestSha256,
            high.ManifestSize,
            previous?.IntegrityBlocked ?? false,
            previous?.LastCheckedUnix ?? 0,
            previous?.RetryAfterUnix ?? 0);

    private static UpdateState SuccessfulState(
        HighWater high,
        DateTimeOffset now) =>
        new(
            high.Version,
            high.SourceCommit,
            high.ManifestSha256,
            high.ManifestSize,
            IntegrityBlocked: false,
            now.ToUnixTimeSeconds(),
            RetryAfterUnix: 0);

    private static void ValidateCurrentReference(CurrentReference reference)
    {
        ValidateHighWater(new HighWater(
            reference.Version,
            reference.SourceCommit,
            reference.ManifestSha256,
            reference.ManifestSize));
        var target = SignedUpdateContract.CurrentTarget();
        if (reference.Filename != target.Filename)
        {
            throw new InvalidDataException(
                "Managed Locker CLI current target is invalid.");
        }
    }

    private static void ValidateHighWater(HighWater high)
    {
        _ = SignedUpdateContract.CompareVersions(high.Version, high.Version);
        if (high.SourceCommit.Length is not (40 or 64)
            || high.SourceCommit.Any(character =>
                character is not (>= '0' and <= '9')
                && character is not (>= 'a' and <= 'f'))
            || high.ManifestSha256.Length != 64
            || high.ManifestSha256.Any(character =>
                character is not (>= '0' and <= '9')
                && character is not (>= 'a' and <= 'f'))
            || high.ManifestSize is < 1 or > SignedUpdateContract.MaxManifestBytes)
        {
            throw new InvalidDataException(
                "Managed Locker CLI high-water mark is invalid.");
        }
    }

    private static byte[] AppendLf(byte[] bytes)
    {
        Array.Resize(ref bytes, bytes.Length + 1);
        bytes[^1] = (byte)'\n';
        return bytes;
    }

    private string ReleasesRoot() => Path.Combine(managedRoot, "releases");

    private string VersionRoot(string version)
    {
        _ = SignedUpdateContract.CompareVersions(version, version);
        var result = Path.Combine(ReleasesRoot(), version);
        EnsurePathIsDirectChild(ReleasesRoot(), result);
        return result;
    }

    private string CurrentPath() => Path.Combine(managedRoot, "current.json");

    private string StatePath() => Path.Combine(managedRoot, "update-state.json");

    private bool HasDetachedSignatureBinding(CurrentReference reference)
    {
        lock (executionTrustSync)
        {
            return detachedSignatureReference == reference;
        }
    }

    private void RememberDetachedSignatureBinding(CurrentReference reference)
    {
        lock (executionTrustSync)
        {
            detachedSignatureReference = reference;
        }
    }

    private sealed record RegularFileSnapshot(
        long Length,
        DateTime CreationTimeUtc,
        DateTime LastWriteTimeUtc);

    private sealed record CurrentReference(
        string Version,
        string SourceCommit,
        string ManifestSha256,
        long ManifestSize,
        string Filename);

    private sealed record UpdateState(
        string HighestVersion,
        string HighestSourceCommit,
        string HighestManifestSha256,
        long HighestManifestSize,
        bool IntegrityBlocked,
        long LastCheckedUnix,
        long RetryAfterUnix);

    private sealed record HighWater(
        string Version,
        string SourceCommit,
        string ManifestSha256,
        long ManifestSize);

    private sealed record VerifiedInstall(
        CurrentReference Reference,
        string BinaryPath,
        byte[] ManifestBytes);

    private sealed class ManagedUpdateFailure : Exception
    {
        private ManagedUpdateFailure(
            string message,
            bool isTransient,
            Exception? innerException = null)
            : base(message, innerException)
        {
            IsTransient = isTransient;
        }

        internal bool IsTransient { get; }

        internal static ManagedUpdateFailure Integrity(
            string message,
            Exception? error = null) =>
            new(message, isTransient: false, error);

        internal static ManagedUpdateFailure Transient(
            string message,
            Exception? error = null) =>
            new(message, isTransient: true, error);

        internal static ManagedUpdateFailure Fatal(
            string message,
            Exception? error = null) =>
            new(message, isTransient: false, error);
    }
}
