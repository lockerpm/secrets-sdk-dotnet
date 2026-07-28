using System.Net;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using Locker;
using Newtonsoft.Json.Linq;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Xunit;

namespace LockerTests;

public sealed class InstallerTests
{
    private static readonly Uri BaseUri =
        new(SignedUpdateContract.ReleaseBaseUrl, UriKind.Absolute);

    [Fact]
    public void BundledProductionTrustIsProvisionedAndCanonical()
    {
        var trust = LockerCliInstaller.LoadBundledReleaseTrust();

        Assert.Equal(BaseUri, trust.BaseUri);
        Assert.Equal(SignedUpdateContract.ReleaseKeyId, trust.KeyId);
        Assert.Equal(TimeSpan.FromHours(6), trust.CheckInterval);
        Assert.NotEmpty(trust.PublicKey);
        Assert.Equal(
            SignedUpdateContract.PublicKeySize,
            SignedUpdateContract.DecodePublicKey(trust.PublicKey).Length);
    }

    [Fact]
    public void BlankTrustFixtureAlwaysFailsClosed()
    {
        Assert.Throws<LockerCliDistributionUnavailableError>(
            () => SignedUpdateContract.DecodePublicKey(string.Empty));
    }

    [Theory]
    [InlineData("2.1.0", "2.0.999", 1)]
    [InlineData("2.0.1000", "2.1.0", -1)]
    [InlineData(
        "2.12345678901234567890.0",
        "2.9999999999999999999.99999999999999999999",
        1)]
    [InlineData("2.1.7", "2.1.7", 0)]
    public void ComparesEveryStableMajorTwoRelease(
        string left,
        string right,
        int expected)
    {
        Assert.Equal(expected, SignedUpdateContract.CompareVersions(left, right));
    }

    [Theory]
    [InlineData("2.01.0")]
    [InlineData("2.0.01")]
    [InlineData("2.0")]
    [InlineData("2.0.0-rc.1")]
    [InlineData("3.0.0")]
    public void RejectsVersionsOutsideStableMajorTwo(string version)
    {
        Assert.Throws<InvalidDataException>(
            () => SignedUpdateContract.CompareVersions(version, "2.0.0"));
    }

    [Fact]
    public void SharedFixtureCanonicalHashesMatchReference()
    {
        using var input = typeof(InstallerTests).Assembly.GetManifestResourceStream(
            "LockerTests.Resources.update-channel-v2.json");
        Assert.NotNull(input);
        using var output = new MemoryStream();
        input.CopyTo(output);
        var bytes = output.ToArray();
        Assert.Equal(
            "116ffe19be6d2f45555ec0f1e1a662ea860d08a48520d4bfb59c8c11cc2f0b47",
            SignedUpdateContract.Sha256Hex(bytes));
        var root = SignedUpdateContract.RequireObject(
            SignedUpdateContract.ParseStrictJson(
                bytes,
                SignedUpdateContract.MaxManifestBytes),
            "shared fixture");

        Assert.Equal(
            "dd32ad36e2ac2fac72220ad8ad8b72da3200799d7d33e7c93f08aa5221b2b22c",
            SignedUpdateContract.Sha256Hex(
                SignedUpdateContract.CanonicalJson(root["latest_payload"]!)));
        Assert.Equal(
            "dbac1da6c487aac212fb9cf18cc547983749d226f4958de849d00d15116e6212",
            SignedUpdateContract.Sha256Hex(
            SignedUpdateContract.CanonicalJson(root["manifest_payload"]!)));
    }

    [Fact]
    public void ManagedRootIsNamespacedForDotnetSdk()
    {
        var home = System.Environment.GetFolderPath(
            System.Environment.SpecialFolder.UserProfile);

        Assert.Equal(
            Path.Combine(home, ".locker", "sdk-cli", "dotnet"),
            LockerCliResolver.GetCanonicalManagedRoot());
    }

    [Theory]
    [MemberData(nameof(InvalidStrictJson))]
    public void StrictJsonRejectsNonContractDocuments(byte[] bytes)
    {
        Assert.Throws<InvalidDataException>(
            () => SignedUpdateContract.ParseStrictJson(bytes, 1024 * 1024));
    }

    [Fact]
    public void SignedEnvelopeRejectsNonCanonicalOuterAndPayloadEncoding()
    {
        var fixture = ReleaseFixture.Create("2.0.7");
        var publicKey = fixture.PublicKey;

        var outerWithoutLf = fixture.LatestBytes[..^1];
        Assert.Throws<InvalidDataException>(
            () => SignedUpdateContract.ParseLatest(outerWithoutLf, publicKey));

        var outerWithExtraLf = fixture.LatestBytes.Concat(new byte[] { (byte)'\n' }).ToArray();
        Assert.Throws<InvalidDataException>(
            () => SignedUpdateContract.ParseLatest(outerWithExtraLf, publicKey));

        var root = SignedUpdateContract.RequireObject(
            SignedUpdateContract.ParseStrictJson(
                fixture.LatestBytes,
                SignedUpdateContract.MaxLatestBytes),
            "latest");
        root["payload"] = ((string)root["payload"]!) + "=";
        Assert.Throws<InvalidDataException>(
            () => SignedUpdateContract.ParseLatest(
                CanonicalFile(root),
                publicKey));
    }

    [Fact]
    public async Task InstallsSignedLatestIntoImmutableVersionDirectory()
    {
        using var temporary = new TemporaryDirectory();
        var fixture = ReleaseFixture.Create("2.0.7");
        var handler = new ChannelHandler(fixture);
        using var installer = CreateInstaller(
            handler,
            temporary.Path,
            fixture,
            () => DateTimeOffset.FromUnixTimeSeconds(1_000));

        var path = await installer.InstallAsync();

        Assert.Equal(
            Path.Combine(
                temporary.Path,
                "releases",
                fixture.Version,
                fixture.Artifact.Filename),
            path);
        Assert.Equal(fixture.Binary, await File.ReadAllBytesAsync(path));
        Assert.Equal(
            fixture.ManifestBytes,
            await File.ReadAllBytesAsync(
                Path.Combine(
                    temporary.Path,
                    "releases",
                    fixture.Version,
                    "manifest.json")));
        Assert.Equal(
            fixture.ArtifactSignature,
            await File.ReadAllBytesAsync(path + ".sig"));
        Assert.True(File.Exists(Path.Combine(temporary.Path, "current.json")));
        Assert.True(File.Exists(Path.Combine(temporary.Path, "update-state.json")));
        Assert.Equal(1, handler.Count("latest.json"));
        Assert.Equal(1, handler.Count(fixture.ManifestPath));
        Assert.Equal(1, handler.Count(fixture.Artifact.Path));
        Assert.Equal(1, handler.Count(fixture.Artifact.SignaturePath));
    }

    [Fact]
    public async Task UsesPersistedExactSixHourCheckInterval()
    {
        using var temporary = new TemporaryDirectory();
        var fixture = ReleaseFixture.Create("2.0.7");
        var handler = new ChannelHandler(fixture);
        var now = DateTimeOffset.FromUnixTimeSeconds(10_000);
        using var installer = CreateInstaller(
            handler,
            temporary.Path,
            fixture,
            () => now);
        await installer.ResolveAsync(CancellationToken.None);
        handler.ResetCounts();

        now = now.AddSeconds(SignedUpdateContract.CheckIntervalSeconds - 1);
        var cached = await installer.ResolveAsync(CancellationToken.None);
        Assert.True(File.Exists(cached));
        Assert.Equal(0, handler.TotalCount);

        now = now.AddSeconds(1);
        var checkedPath = await installer.ResolveAsync(CancellationToken.None);
        Assert.Equal(cached, checkedPath);
        Assert.Equal(1, handler.Count("latest.json"));
        Assert.Equal(1, handler.Count(fixture.ManifestPath));
        Assert.Equal(0, handler.Count(fixture.Artifact.Path));
        Assert.Equal(0, handler.Count(fixture.Artifact.SignaturePath));
    }

    [Fact]
    public async Task NetworkFailureFallsBackOnlyToFullyVerifiedCache()
    {
        using var temporary = new TemporaryDirectory();
        var fixture = ReleaseFixture.Create("2.0.7");
        var handler = new ChannelHandler(fixture);
        var now = DateTimeOffset.FromUnixTimeSeconds(20_000);
        using var installer = CreateInstaller(
            handler,
            temporary.Path,
            fixture,
            () => now);
        var installed = await installer.ResolveAsync(CancellationToken.None);
        now = now.AddHours(6);
        handler.NetworkFailure = true;
        handler.ResetCounts();

        Assert.Equal(
            installed,
            await installer.ResolveAsync(CancellationToken.None));
        Assert.Equal(1, handler.Count("latest.json"));

        handler.ResetCounts();
        Assert.Equal(
            installed,
            await installer.ResolveAsync(CancellationToken.None));
        Assert.Equal(0, handler.TotalCount);

        now = now.AddSeconds(59);
        Assert.Equal(
            installed,
            await installer.ResolveAsync(CancellationToken.None));
        Assert.Equal(0, handler.TotalCount);

        now = now.AddSeconds(1);
        Assert.Equal(
            installed,
            await installer.ResolveAsync(CancellationToken.None));
        Assert.Equal(1, handler.Count("latest.json"));
    }

    [Fact]
    public async Task NetworkFailureWithoutVerifiedCacheFailsClosed()
    {
        using var temporary = new TemporaryDirectory();
        var fixture = ReleaseFixture.Create("2.0.7");
        var handler = new ChannelHandler(fixture) { NetworkFailure = true };
        using var installer = CreateInstaller(
            handler,
            temporary.Path,
            fixture,
            () => DateTimeOffset.FromUnixTimeSeconds(30_000));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => installer.ResolveAsync(CancellationToken.None));
        Assert.False(File.Exists(Path.Combine(temporary.Path, "current.json")));
    }

    [Fact]
    public async Task OnlyHttpFiveHundredsThroughFiveNinetyNineAreTransient()
    {
        using var temporary = new TemporaryDirectory();
        var fixture = ReleaseFixture.Create("2.0.7");
        var handler = new ChannelHandler(fixture);
        var now = DateTimeOffset.FromUnixTimeSeconds(35_000);
        using var installer = CreateInstaller(
            handler,
            temporary.Path,
            fixture,
            () => now);
        var cached = await installer.ResolveAsync(CancellationToken.None);

        now = now.AddHours(6);
        handler.ResponseStatus = 599;
        Assert.Equal(
            cached,
            await installer.ResolveAsync(CancellationToken.None));

        now = now.AddSeconds(60);
        handler.ResponseStatus = 600;
        await Assert.ThrowsAsync<InvalidDataException>(
            () => installer.ResolveAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SecureConnectionErrorIsIntegrityFailure(bool duringBodyRead)
    {
        using var temporary = new TemporaryDirectory();
        var fixture = ReleaseFixture.Create("2.0.7");
        var handler = new ChannelHandler(fixture);
        if (duringBodyRead)
        {
            handler.BodyRequestError = HttpRequestError.SecureConnectionError;
        }
        else
        {
            handler.NetworkRequestError = HttpRequestError.SecureConnectionError;
        }
        using var installer = CreateInstaller(
            handler,
            temporary.Path,
            fixture,
            () => DateTimeOffset.FromUnixTimeSeconds(36_000));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => installer.ResolveAsync(CancellationToken.None));
    }

    [Fact]
    public async Task TotalDownloadDeadlineCoversSlowResponseBody()
    {
        using var temporary = new TemporaryDirectory();
        var fixture = ReleaseFixture.Create("2.0.7");
        var handler = new ChannelHandler(fixture) { SlowBody = true };
        using var installer = CreateInstaller(
            handler,
            temporary.Path,
            fixture,
            () => DateTimeOffset.FromUnixTimeSeconds(37_000),
            TimeSpan.FromMilliseconds(100));
        var elapsed = Stopwatch.StartNew();

        await Assert.ThrowsAsync<HttpRequestException>(
            () => installer.ResolveAsync(CancellationToken.None));

        elapsed.Stop();
        Assert.True(
            elapsed.Elapsed < TimeSpan.FromSeconds(5),
            $"Slow response body exceeded the bounded deadline: {elapsed.Elapsed}.");
    }

    [Fact]
    public async Task IntegrityFailureNeverFallsBackAndBlocksOfflineReuse()
    {
        using var temporary = new TemporaryDirectory();
        var fixture = ReleaseFixture.Create("2.0.7");
        var handler = new ChannelHandler(fixture);
        var now = DateTimeOffset.FromUnixTimeSeconds(40_000);
        using var installer = CreateInstaller(
            handler,
            temporary.Path,
            fixture,
            () => now);
        await installer.ResolveAsync(CancellationToken.None);
        now = now.AddHours(6);
        handler.Set("latest.json", fixture.LatestBytes[..^1]);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => installer.ResolveAsync(CancellationToken.None));

        handler.NetworkFailure = true;
        await Assert.ThrowsAsync<HttpRequestException>(
            () => installer.ResolveAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData("digest")]
    [InlineData("signature")]
    [InlineData("header")]
    [InlineData("size")]
    public async Task RejectsEveryArtifactIntegrityMismatch(string corruption)
    {
        using var temporary = new TemporaryDirectory();
        var fixture = ReleaseFixture.Create("2.0.7");
        var handler = new ChannelHandler(fixture);
        switch (corruption)
        {
            case "digest":
                handler.Set(
                    fixture.Artifact.Path,
                    fixture.Binary.Select((value, index) =>
                        index == fixture.Binary.Length - 1
                            ? (byte)(value ^ 1)
                            : value).ToArray());
                break;
            case "signature":
                handler.Set(
                    fixture.Artifact.SignaturePath,
                    fixture.ArtifactSignature.Select((value, index) =>
                        index == 0 ? (byte)(value ^ 1) : value).ToArray());
                break;
            case "header":
                {
                    var malformed = fixture.Binary.ToArray();
                    malformed[0] ^= 1;
                    var replacement = fixture.WithCurrentArtifactBytes(malformed);
                    handler.Replace(replacement);
                    break;
                }
            case "size":
                handler.Set(
                    fixture.Artifact.Path,
                    fixture.Binary[..^1]);
                break;
        }

        using var installer = CreateInstaller(
            handler,
            temporary.Path,
            fixture,
            () => DateTimeOffset.FromUnixTimeSeconds(50_000));
        await Assert.ThrowsAsync<InvalidDataException>(
            () => installer.InstallAsync());
        Assert.False(File.Exists(Path.Combine(temporary.Path, "current.json")));
    }

    [Fact]
    public async Task RejectsRollbackAndSameVersionEquivocation()
    {
        using var temporary = new TemporaryDirectory();
        var initial = ReleaseFixture.Create("2.0.8");
        var handler = new ChannelHandler(initial);
        var now = DateTimeOffset.FromUnixTimeSeconds(60_000);
        using var installer = CreateInstaller(
            handler,
            temporary.Path,
            initial,
            () => now);
        await installer.ResolveAsync(CancellationToken.None);
        now = now.AddHours(6);

        handler.Replace(ReleaseFixture.Create("2.0.7", initial.Seed));
        await Assert.ThrowsAsync<InvalidDataException>(
            () => installer.ResolveAsync(CancellationToken.None));

        handler.Replace(ReleaseFixture.Create(
            "2.0.8",
            initial.Seed,
            sourceCommit: new string('b', 40)));
        await Assert.ThrowsAsync<InvalidDataException>(
            () => installer.ResolveAsync(CancellationToken.None));
    }

    [Fact]
    public async Task RejectsManifestSizeEquivocationWithinOneVersion()
    {
        using var temporary = new TemporaryDirectory();
        var initial = ReleaseFixture.Create("2.0.8");
        var handler = new ChannelHandler(initial);
        var now = DateTimeOffset.FromUnixTimeSeconds(65_000);
        using var installer = CreateInstaller(
            handler,
            temporary.Path,
            initial,
            () => now);
        await installer.ResolveAsync(CancellationToken.None);
        now = now.AddHours(6);
        handler.Replace(initial.WithLatestManifestSize(
            initial.ManifestBytes.Length + 1));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => installer.ResolveAsync(CancellationToken.None));
    }

    [Fact]
    public async Task PersistsAcceptedHighWaterBeforeManifestRequest()
    {
        using var temporary = new TemporaryDirectory();
        var initial = ReleaseFixture.Create("2.0.7");
        var upgraded = ReleaseFixture.Create(
            "2.0.8",
            initial.Seed,
            sourceCommit: new string('b', 40));
        var handler = new ChannelHandler(initial);
        var now = DateTimeOffset.FromUnixTimeSeconds(66_000);
        using var installer = CreateInstaller(
            handler,
            temporary.Path,
            initial,
            () => now);
        await installer.ResolveAsync(CancellationToken.None);

        now = now.AddHours(6);
        handler.Replace(upgraded);
        handler.CrashPath = upgraded.ManifestPath;
        await Assert.ThrowsAsync<SimulatedCrashException>(
            () => installer.ResolveAsync(CancellationToken.None));

        var state = await File.ReadAllTextAsync(
            Path.Combine(temporary.Path, "update-state.json"));
        Assert.Contains("\"highest_version\":\"2.0.8\"", state, StringComparison.Ordinal);
        Assert.Contains(
            $"\"highest_manifest_size\":{upgraded.ManifestBytes.Length}",
            state,
            StringComparison.Ordinal);

        handler.CrashPath = null;
        handler.Replace(initial);
        await Assert.ThrowsAsync<InvalidDataException>(
            () => installer.InstallAsync());
    }

    [Fact]
    public async Task UpgradeKeepsOldImmutableReleaseAndMovesAtomicReference()
    {
        using var temporary = new TemporaryDirectory();
        var initial = ReleaseFixture.Create("2.0.7");
        var handler = new ChannelHandler(initial);
        var now = DateTimeOffset.FromUnixTimeSeconds(70_000);
        using var installer = CreateInstaller(
            handler,
            temporary.Path,
            initial,
            () => now);
        var oldPath = await installer.ResolveAsync(CancellationToken.None);
        var upgraded = ReleaseFixture.Create(
            "2.0.8",
            initial.Seed,
            sourceCommit: new string('b', 40));
        handler.Replace(upgraded);
        now = now.AddHours(6);

        var newPath = await installer.ResolveAsync(CancellationToken.None);

        Assert.NotEqual(oldPath, newPath);
        Assert.True(File.Exists(oldPath));
        Assert.True(File.Exists(newPath));
        Assert.Equal(upgraded.Binary, await File.ReadAllBytesAsync(newPath));
        var current = await File.ReadAllTextAsync(
            Path.Combine(temporary.Path, "current.json"));
        Assert.Contains("\"version\":\"2.0.8\"", current, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TamperedManagedBinaryIsNeverExecutedOrRepairedSilently()
    {
        using var temporary = new TemporaryDirectory();
        var fixture = ReleaseFixture.Create("2.0.7");
        var handler = new ChannelHandler(fixture);
        var now = DateTimeOffset.FromUnixTimeSeconds(80_000);
        using var installer = CreateInstaller(
            handler,
            temporary.Path,
            fixture,
            () => now);
        var path = await installer.ResolveAsync(CancellationToken.None);
        var bytes = await File.ReadAllBytesAsync(path);
        bytes[^1] ^= 1;
        await File.WriteAllBytesAsync(path, bytes);
        handler.ResetCounts();

        await Assert.ThrowsAsync<InvalidDataException>(
            () => installer.ResolveAsync(CancellationToken.None));
        Assert.Equal(0, handler.TotalCount);
    }

    [Fact]
    public async Task WindowsCacheRejectsBroadWritableAcl()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temporary = new TemporaryDirectory();
        var fixture = ReleaseFixture.Create("2.0.7");
        var handler = new ChannelHandler(fixture);
        using var installer = CreateInstaller(
            handler,
            temporary.Path,
            fixture,
            () => DateTimeOffset.FromUnixTimeSeconds(85_000));
        var path = await installer.ResolveAsync(CancellationToken.None);
        var file = new FileInfo(path);
        var security = file.GetAccessControl();
        security.AddAccessRule(
            new FileSystemAccessRule(
                new SecurityIdentifier(
                    WellKnownSidType.WorldSid,
                    domainSid: null),
                FileSystemRights.Write,
                AccessControlType.Allow));
        file.SetAccessControl(security);
        handler.ResetCounts();

        await Assert.ThrowsAsync<InvalidDataException>(
            () => installer.ResolveAsync(CancellationToken.None));
        Assert.Equal(0, handler.TotalCount);
    }

    [Fact]
    public async Task ConcurrentResolversPublishOneCompleteImmutableRelease()
    {
        using var temporary = new TemporaryDirectory();
        var fixture = ReleaseFixture.Create("2.0.7");
        var handler = new ChannelHandler(fixture);
        var now = DateTimeOffset.FromUnixTimeSeconds(90_000);
        using var first = CreateInstaller(
            handler,
            temporary.Path,
            fixture,
            () => now);
        using var second = CreateInstaller(
            handler,
            temporary.Path,
            fixture,
            () => now);

        var paths = await Task.WhenAll(
            first.ResolveAsync(CancellationToken.None),
            second.ResolveAsync(CancellationToken.None));

        Assert.Equal(paths[0], paths[1]);
        Assert.Equal(fixture.Binary, await File.ReadAllBytesAsync(paths[0]));
        Assert.Equal(1, handler.Count(fixture.Artifact.Path));
        Assert.Equal(1, handler.Count(fixture.Artifact.SignaturePath));
    }

    [Fact]
    public async Task ManagedResolutionNeverFallsBackToAttackerControlledPath()
    {
        using var temporary = new TemporaryDirectory();
        var attackerCli = Path.Combine(
            temporary.Path,
            OperatingSystem.IsWindows() ? "locker.exe" : "locker");
        await File.WriteAllBytesAsync(attackerCli, new byte[] { 1 });
        var originalPath = System.Environment.GetEnvironmentVariable("PATH");
        var originalOverride = System.Environment.GetEnvironmentVariable(
            LockerClientFactory.CliPathEnvironmentVariable);
        try
        {
            System.Environment.SetEnvironmentVariable("PATH", temporary.Path);
            System.Environment.SetEnvironmentVariable(
                LockerClientFactory.CliPathEnvironmentVariable,
                null);
            var options = new LockerClientOptions("access", "secret");
            await Assert.ThrowsAsync<LockerCliDistributionUnavailableError>(
                () => LockerCliResolver.ResolveAsync(
                    options,
                    _ => throw new LockerCliDistributionUnavailableError(
                        "blank trust"),
                    CancellationToken.None));
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("PATH", originalPath);
            System.Environment.SetEnvironmentVariable(
                LockerClientFactory.CliPathEnvironmentVariable,
                originalOverride);
        }
    }

    [Fact]
    public void SharedCacheAncestorsAcceptSafeAclAndRejectBroadMutation()
    {
        using var temporary = new TemporaryDirectory();
        var ancestor = Path.Combine(temporary.Path, "sdk-cli");
        Directory.CreateDirectory(ancestor);
        if (OperatingSystem.IsWindows())
        {
            SetSafeWindowsDirectory(ancestor);
        }
        else
        {
            File.SetUnixFileMode(
                ancestor,
                UnixFileMode.UserRead
                | UnixFileMode.UserWrite
                | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead
                | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead
                | UnixFileMode.OtherExecute);
        }

        LockerCliInstaller.EnsureSafeManagedAncestorDirectory(ancestor);

        if (OperatingSystem.IsWindows())
        {
            var directory = new DirectoryInfo(ancestor);
            var security = directory.GetAccessControl();
            security.AddAccessRule(
                new FileSystemAccessRule(
                    new SecurityIdentifier(
                        WellKnownSidType.WorldSid,
                        domainSid: null),
                    FileSystemRights.CreateDirectories,
                    AccessControlType.Allow));
            directory.SetAccessControl(security);
        }
        else
        {
            File.SetUnixFileMode(
                ancestor,
                File.GetUnixFileMode(ancestor) | UnixFileMode.OtherWrite);
        }

        Assert.Throws<InvalidDataException>(
            () => LockerCliInstaller.EnsureSafeManagedAncestorDirectory(ancestor));
    }

    [Fact]
    public async Task UnixOwnershipChecksBindDirectoriesAndFilesToEffectiveUser()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var temporary = new TemporaryDirectory();
        File.SetUnixFileMode(
            temporary.Path,
            UnixFileMode.UserRead
            | UnixFileMode.UserWrite
            | UnixFileMode.UserExecute);
        var effectiveUser = LockerCliInstaller.GetEffectiveUnixUserId();
        Assert.Equal(
            effectiveUser,
            LockerCliInstaller.GetUnixPathOwnerId(
                temporary.Path,
                expectedDirectory: true));
        LockerCliInstaller.VerifyUnixPathOwner(
            temporary.Path,
            allowRootOwner: false,
            expectedDirectory: true);

        var file = Path.Combine(temporary.Path, "owned-file");
        await File.WriteAllBytesAsync(file, new byte[] { 1 });
        File.SetUnixFileMode(
            file,
            UnixFileMode.UserRead | UnixFileMode.UserWrite);
        Assert.Equal(
            effectiveUser,
            LockerCliInstaller.GetUnixPathOwnerId(
                file,
                expectedDirectory: false));
        LockerCliInstaller.VerifyUnixPathOwner(
            file,
            allowRootOwner: false,
            expectedDirectory: false);
        Assert.Throws<InvalidDataException>(
            () => LockerCliInstaller.GetUnixPathOwnerId(
                file,
                expectedDirectory: true));

        var fileSystemRoot = Path.GetPathRoot(temporary.Path);
        Assert.False(string.IsNullOrEmpty(fileSystemRoot));
        var rootOwner = LockerCliInstaller.GetUnixPathOwnerId(
            fileSystemRoot!,
            expectedDirectory: true);
        if (rootOwner != effectiveUser)
        {
            Assert.Throws<InvalidDataException>(
                () => LockerCliInstaller.VerifyUnixPathOwner(
                    fileSystemRoot!,
                    allowRootOwner: false,
                    expectedDirectory: true));
            if (rootOwner == 0)
            {
                LockerCliInstaller.VerifyUnixPathOwner(
                    fileSystemRoot!,
                    allowRootOwner: true,
                    expectedDirectory: true);
            }
        }
    }

    [Fact]
    public async Task ExplicitOptionAndEnvironmentPathsBypassManagedUpdater()
    {
        using var temporary = new TemporaryDirectory();
        var explicitPath = Path.Combine(temporary.Path, "locker.exe");
        await File.WriteAllBytesAsync(explicitPath, new byte[] { 1 });
        var original = System.Environment.GetEnvironmentVariable(
            LockerClientFactory.CliPathEnvironmentVariable);
        try
        {
            System.Environment.SetEnvironmentVariable(
                LockerClientFactory.CliPathEnvironmentVariable,
                Path.Combine(temporary.Path, "missing"));
            var options = new LockerClientOptions(
                "access",
                "secret",
                cliPath: explicitPath);
            Assert.Equal(
                explicitPath,
                await LockerCliResolver.ResolveAsync(
                    options,
                    CancellationToken.None));

            System.Environment.SetEnvironmentVariable(
                LockerClientFactory.CliPathEnvironmentVariable,
                explicitPath);
            var environmentOptions = new LockerClientOptions("access", "secret");
            Assert.Equal(
                explicitPath,
                await LockerCliResolver.ResolveAsync(
                    environmentOptions,
                    CancellationToken.None));
        }
        finally
        {
            System.Environment.SetEnvironmentVariable(
                LockerClientFactory.CliPathEnvironmentVariable,
                original);
        }
    }

    [Fact]
    public async Task ExplicitOverridesRejectBareAndRelativePathsWithoutConsultingPath()
    {
        using var temporary = new TemporaryDirectory();
        var attackerName = OperatingSystem.IsWindows() ? "locker.exe" : "locker";
        var attackerCli = Path.Combine(temporary.Path, attackerName);
        await File.WriteAllBytesAsync(attackerCli, new byte[] { 1 });
        var originalPath = System.Environment.GetEnvironmentVariable("PATH");
        var originalOverride = System.Environment.GetEnvironmentVariable(
            LockerClientFactory.CliPathEnvironmentVariable);
        try
        {
            System.Environment.SetEnvironmentVariable("PATH", temporary.Path);
            var explicitOptions = new LockerClientOptions(
                "access",
                "secret",
                cliPath: attackerName);
            await Assert.ThrowsAsync<CliRunError>(
                () => LockerCliResolver.ResolveAsync(
                    explicitOptions,
                    CancellationToken.None));

            System.Environment.SetEnvironmentVariable(
                LockerClientFactory.CliPathEnvironmentVariable,
                attackerName);
            await Assert.ThrowsAsync<CliRunError>(
                () => LockerCliResolver.ResolveAsync(
                    new LockerClientOptions("access", "secret"),
                    CancellationToken.None));

            System.Environment.SetEnvironmentVariable(
                LockerClientFactory.CliPathEnvironmentVariable,
                Path.Combine(".", attackerName));
            await Assert.ThrowsAsync<CliRunError>(
                () => LockerCliResolver.ResolveAsync(
                    new LockerClientOptions("access", "secret"),
                    CancellationToken.None));
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("PATH", originalPath);
            System.Environment.SetEnvironmentVariable(
                LockerClientFactory.CliPathEnvironmentVariable,
                originalOverride);
        }
    }

    public static TheoryData<byte[]> InvalidStrictJson => new()
    {
        Encoding.UTF8.GetBytes("{\"x\":1,\"x\":2}"),
        Encoding.UTF8.GetBytes("{\"x\":1.0}"),
        Encoding.UTF8.GetBytes("{\"x\":9223372036854775808}"),
        Encoding.UTF8.GetBytes("{\"x\":\"\u0080\"}"),
        new byte[] { 0xef, 0xbb, 0xbf, (byte)'{', (byte)'}' },
        Encoding.UTF8.GetBytes("{}{}"),
        Encoding.UTF8.GetBytes("{/*comment*/\"x\":1}"),
        Encoding.UTF8.GetBytes(
            new string('[', 65) + new string(']', 65)),
        new byte[] { (byte)'{', (byte)'"', 0xff, (byte)'"', (byte)':', (byte)'1', (byte)'}' },
    };

    private static LockerCliInstaller CreateInstaller(
        ChannelHandler handler,
        string root,
        ReleaseFixture fixture,
        Func<DateTimeOffset> now,
        TimeSpan? downloadTimeout = null)
    {
        var trust = new ReleaseTrust(
            BaseUri,
            SignedUpdateContract.ReleaseKeyId,
            Base64Url(fixture.PublicKey),
            TimeSpan.FromSeconds(SignedUpdateContract.CheckIntervalSeconds));
        return new LockerCliInstaller(
            new HttpClient(handler, disposeHandler: false)
            {
                Timeout = TimeSpan.FromSeconds(5),
            },
            root,
            trust,
            now,
            downloadTimeout);
    }

    [SupportedOSPlatform("windows")]
    private static void SetSafeWindowsDirectory(string path)
    {
        var owner = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException(
                "The test account has no Windows security identifier.");
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(
            isProtected: true,
            preserveInheritance: false);
        security.SetOwner(owner);
        security.AddAccessRule(
            new FileSystemAccessRule(
                owner,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
        new DirectoryInfo(path).SetAccessControl(security);
    }

    private static byte[] CanonicalFile(JToken value)
    {
        var bytes = SignedUpdateContract.CanonicalJson(value);
        Array.Resize(ref bytes, bytes.Length + 1);
        bytes[^1] = (byte)'\n';
        return bytes;
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private sealed class ChannelHandler : HttpMessageHandler
    {
        private readonly object sync = new();
        private readonly Dictionary<string, byte[]> responses =
            new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> counts =
            new(StringComparer.Ordinal);

        internal ChannelHandler(ReleaseFixture fixture)
        {
            Replace(fixture);
        }

        internal bool NetworkFailure { get; set; }

        internal HttpRequestError? NetworkRequestError { get; set; }

        internal HttpRequestError? BodyRequestError { get; set; }

        internal int? ResponseStatus { get; set; }

        internal bool SlowBody { get; set; }

        internal string? CrashPath { get; set; }

        internal int TotalCount
        {
            get
            {
                lock (sync)
                {
                    return counts.Values.Sum();
                }
            }
        }

        internal void Replace(ReleaseFixture fixture)
        {
            lock (sync)
            {
                responses.Clear();
                responses["latest.json"] = fixture.LatestBytes;
                responses[fixture.ManifestPath] = fixture.ManifestBytes;
                responses[fixture.Artifact.Path] = fixture.Binary;
                responses[fixture.Artifact.SignaturePath] =
                    fixture.ArtifactSignature;
            }
        }

        internal void Set(string path, byte[] bytes)
        {
            lock (sync)
            {
                responses[path] = bytes;
            }
        }

        internal int Count(string path)
        {
            lock (sync)
            {
                return counts.GetValueOrDefault(path);
            }
        }

        internal void ResetCounts()
        {
            lock (sync)
            {
                counts.Clear();
            }
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath[
                BaseUri.AbsolutePath.Length..]
                ?? string.Empty;
            lock (sync)
            {
                counts[path] = counts.GetValueOrDefault(path) + 1;
                if (NetworkFailure)
                {
                    throw new HttpRequestException("simulated network outage");
                }
                if (NetworkRequestError is { } requestError)
                {
                    throw new HttpRequestException(
                        requestError,
                        "simulated secure transport failure");
                }
                if (string.Equals(path, CrashPath, StringComparison.Ordinal))
                {
                    throw new SimulatedCrashException();
                }
                if (ResponseStatus is { } status)
                {
                    return Task.FromResult(
                        new HttpResponseMessage((HttpStatusCode)status));
                }

                if (!responses.TryGetValue(path, out var bytes))
                {
                    return Task.FromResult(
                        new HttpResponseMessage(HttpStatusCode.NotFound));
                }
                if (SlowBody || BodyRequestError is not null)
                {
                    return Task.FromResult(
                        new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StreamContent(
                                new CancellableNeverEndingStream(
                                    BodyRequestError)),
                            RequestMessage = request,
                        });
                }

                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(bytes),
                        RequestMessage = request,
                    });
            }
        }
    }

    private sealed class CancellableNeverEndingStream : Stream
    {
        private readonly HttpRequestError? requestError;

        internal CancellableNeverEndingStream(
            HttpRequestError? requestError = null)
        {
            this.requestError = requestError;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length =>
            throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override async Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            ThrowIfConfigured();
            await Task.Delay(
                System.Threading.Timeout.InfiniteTimeSpan,
                cancellationToken);
            return 0;
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            ThrowIfConfigured();
            await Task.Delay(
                System.Threading.Timeout.InfiniteTimeSpan,
                cancellationToken);
            return 0;
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        private void ThrowIfConfigured()
        {
            if (requestError is { } error)
            {
                throw new HttpRequestException(
                    error,
                    "simulated response body TLS failure");
            }
        }
    }

    private sealed class SimulatedCrashException : Exception
    {
    }

    private sealed record ReleaseFixture(
        string Version,
        byte[] Seed,
        byte[] PublicKey,
        string SourceCommit,
        ReleaseArtifact Artifact,
        byte[] Binary,
        byte[] ArtifactSignature,
        string ManifestPath,
        byte[] ManifestBytes,
        byte[] LatestBytes)
    {
        internal static ReleaseFixture Create(
            string version,
            byte[]? seed = null,
            string? sourceCommit = null)
        {
            seed ??= Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
            sourceCommit ??= new string('a', 40);
            var privateKey = new Ed25519PrivateKeyParameters(seed, 0);
            var publicKey = privateKey.GeneratePublicKey().GetEncoded();
            var binary = CurrentBinary(version);
            var signature = Sign(binary, privateKey);
            return Build(
                version,
                seed,
                publicKey,
                sourceCommit,
                binary,
                signature,
                privateKey);
        }

        internal ReleaseFixture WithCurrentArtifactBytes(byte[] bytes)
        {
            var privateKey = new Ed25519PrivateKeyParameters(Seed, 0);
            return Build(
                Version,
                Seed,
                PublicKey,
                SourceCommit,
                bytes,
                Sign(bytes, privateKey),
                privateKey);
        }

        internal ReleaseFixture WithLatestManifestSize(int size)
        {
            var privateKey = new Ed25519PrivateKeyParameters(Seed, 0);
            return Build(
                Version,
                Seed,
                PublicKey,
                SourceCommit,
                Binary,
                ArtifactSignature,
                privateKey,
                size);
        }

        private static ReleaseFixture Build(
            string version,
            byte[] seed,
            byte[] publicKey,
            string sourceCommit,
            byte[] binary,
            byte[] signature,
            Ed25519PrivateKeyParameters privateKey,
            int? latestManifestSize = null)
        {
            var target = SignedUpdateContract.CurrentTarget();
            var sha = SignedUpdateContract.Sha256Hex(binary);
            var artifacts = new JArray();
            ReleaseArtifact? selected = null;
            foreach (var candidate in SignedUpdateContract.CanonicalTargets)
            {
                var artifact = new ReleaseArtifact(
                    candidate.OS,
                    candidate.Arch,
                    candidate.Filename,
                    $"{version}/{candidate.Filename}",
                    $"{version}/{candidate.Filename}.sig",
                    sha,
                    binary.LongLength);
                if (candidate == target)
                {
                    selected = artifact;
                }

                artifacts.Add(new JObject
                {
                    ["arch"] = artifact.Arch,
                    ["filename"] = artifact.Filename,
                    ["os"] = artifact.OS,
                    ["path"] = artifact.Path,
                    ["sha256"] = artifact.Sha256,
                    ["signature_path"] = artifact.SignaturePath,
                    ["size"] = artifact.Size,
                });
            }

            var manifestPayload = new JObject
            {
                ["artifacts"] = artifacts,
                ["product"] = "locker-cli",
                ["protocol"] = new JObject
                {
                    ["max_version"] = 1,
                    ["min_version"] = 1,
                    ["name"] = "locker.sdk",
                    ["transport"] = "json-rpc-2.0-stdio",
                },
                ["schema"] = "io.locker.cli.update-manifest",
                ["schema_version"] = 2,
                ["source_commit"] = sourceCommit,
                ["version"] = version,
            };
            var manifestBytes = Envelope(manifestPayload, privateKey);
            var manifestPath = $"{version}/manifest.json";
            var latestPayload = new JObject
            {
                ["manifest"] = new JObject
                {
                    ["path"] = manifestPath,
                    ["sha256"] = SignedUpdateContract.Sha256Hex(manifestBytes),
                    ["size"] = latestManifestSize ?? manifestBytes.Length,
                },
                ["product"] = "locker-cli",
                ["schema"] = "io.locker.cli.update-latest",
                ["schema_version"] = 2,
                ["source_commit"] = sourceCommit,
                ["version"] = version,
            };
            return new ReleaseFixture(
                version,
                seed,
                publicKey,
                sourceCommit,
                selected!,
                binary,
                signature,
                manifestPath,
                manifestBytes,
                Envelope(latestPayload, privateKey));
        }

        private static byte[] Envelope(
            JObject payload,
            Ed25519PrivateKeyParameters privateKey)
        {
            var payloadBytes = SignedUpdateContract.CanonicalJson(payload);
            var envelope = new JObject
            {
                ["algorithm"] = "Ed25519",
                ["key_id"] = SignedUpdateContract.ReleaseKeyId,
                ["payload"] = Base64Url(payloadBytes),
                ["schema"] = "io.locker.cli.signed-envelope",
                ["schema_version"] = 2,
                ["signature"] = Base64Url(Sign(payloadBytes, privateKey)),
            };
            return CanonicalFile(envelope);
        }

        private static byte[] Sign(
            byte[] bytes,
            Ed25519PrivateKeyParameters privateKey)
        {
            var signer = new Ed25519Signer();
            signer.Init(true, privateKey);
            signer.BlockUpdate(bytes, 0, bytes.Length);
            return signer.GenerateSignature();
        }

        private static byte[] CurrentBinary(string version)
        {
            var data = new byte[512];
            RandomNumberGenerator.Fill(data);
            Encoding.ASCII.GetBytes(version).CopyTo(data, 256);
            var target = SignedUpdateContract.CurrentTarget();
            switch (target.OS)
            {
                case "windows":
                    data[0] = (byte)'M';
                    data[1] = (byte)'Z';
                    System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(
                        data.AsSpan(60, 4),
                        64);
                    data[64] = (byte)'P';
                    data[65] = (byte)'E';
                    data[66] = 0;
                    data[67] = 0;
                    System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(
                        data.AsSpan(68, 2),
                        0x8664);
                    break;
                case "linux":
                    data[0] = 0x7f;
                    data[1] = (byte)'E';
                    data[2] = (byte)'L';
                    data[3] = (byte)'F';
                    data[4] = 2;
                    data[5] = 1;
                    System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(
                        data.AsSpan(18, 2),
                        target.Arch == "amd64" ? (ushort)0x3e : (ushort)0xb7);
                    break;
                case "darwin":
                    data[0] = 0xcf;
                    data[1] = 0xfa;
                    data[2] = 0xed;
                    data[3] = 0xfe;
                    System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(
                        data.AsSpan(4, 4),
                        target.Arch == "amd64" ? 0x01000007u : 0x0100000cu);
                    break;
            }

            return data;
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        internal TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"locker-dotnet-updater-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
            if (OperatingSystem.IsWindows())
            {
                SetSafeWindowsDirectory(Path);
            }
        }

        internal string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
