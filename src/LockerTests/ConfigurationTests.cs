using Locker;
using System.Reflection;
using System.Xml.Linq;
using Xunit;

namespace LockerTests;

public sealed class ConfigurationTests
{
    [Fact]
    public void LegacyBaseOptionsNeverThrowsDuringCompatibilityUse()
    {
#pragma warning disable CS0618
        Assert.Equal(string.Empty, new BaseOptions().BuildOptions());
#pragma warning restore CS0618
    }

    [Fact]
    public void ReleaseVersionMetadataRemainsAligned()
    {
        var repositoryRoot = FindRepositoryRoot();
        var releaseVersion = File.ReadAllText(
            Path.Combine(repositoryRoot.FullName, "VERSION")).Trim();
        var expectedRuntimeVersion =
            System.Environment.GetEnvironmentVariable("LOCKER_SDK_VERSION")
            ?? releaseVersion;
        var project = XDocument.Load(
            Path.Combine(
                repositoryRoot.FullName,
                "src",
                "Locker",
                "Locker.csproj"));
        var projectVersion = project
            .Descendants("Version")
            .Single()
            .Value
            .Trim();

        Assert.Equal(releaseVersion, projectVersion);
        Assert.Equal(expectedRuntimeVersion, LockerSdkMetadata.Version);
        Assert.Equal(
            expectedRuntimeVersion,
            LockerConfiguration.Instance.SdkVersion);
    }

    [Fact]
    public void ProtocolFixtureTracksTheSdkReleaseVersion()
    {
        var repositoryRoot = FindRepositoryRoot();
        var expectedVersion =
            System.Environment.GetEnvironmentVariable("LOCKER_SDK_VERSION")
            ?? File.ReadAllText(
                Path.Combine(repositoryRoot.FullName, "VERSION")).Trim();
        var fixtureVersion = AssemblyName
            .GetAssemblyName(FakeCliAssemblyPath)
            .Version?
            .ToString(3);

        // Release CI globally overrides Version (for example, 2.0.1).
        // The fake CLI must validate that exact build instead of a base-version
        // literal, or every protocol test silently becomes release-line-specific.
        Assert.Equal(expectedVersion, fixtureVersion);
    }

    [Fact]
    public void RejectsImplicitDotEnvLoading()
    {
        Assert.Throws<NotSupportedException>(
            () => LockerConfiguration.Instance.Init(envPath: ".env"));
    }

    [Fact]
    public void CanonicalCredentialsPrecedeLegacyAliases()
    {
        var names = new[]
        {
            "LOCKER_ACCESS_KEY_ID",
            "LOCKER_SECRET_ACCESS_KEY",
            "ACCESS_KEY_ID",
            "SECRET_ACCESS_KEY",
            "LOCKER_ACCESS_KEY_SECRET",
            "ACCESS_KEY_SECRET",
        };
        var originals = names.ToDictionary(
            name => name,
            System.Environment.GetEnvironmentVariable,
            StringComparer.Ordinal);
        try
        {
            System.Environment.SetEnvironmentVariable(
                "LOCKER_ACCESS_KEY_ID",
                TestCredentials.AccessKeyId);
            System.Environment.SetEnvironmentVariable(
                "LOCKER_SECRET_ACCESS_KEY",
                TestCredentials.SecretAccessKey);
            System.Environment.SetEnvironmentVariable(
                "ACCESS_KEY_ID",
                "10000000-0000-4000-8000-000000000001");
            System.Environment.SetEnvironmentVariable(
                "SECRET_ACCESS_KEY",
                "bGVnYWN5LXNlY3JldA==");
            System.Environment.SetEnvironmentVariable(
                "LOCKER_ACCESS_KEY_SECRET",
                "b2xkZXItc2VjcmV0");
            System.Environment.SetEnvironmentVariable(
                "ACCESS_KEY_SECRET",
                "b2xkZXN0LXNlY3JldA==");

            using var client = LockerClientFactory.FromEnvironment(cliPath: "locker");

            Assert.Equal(
                TestCredentials.AccessKeyId,
                client.Options.AccessKeyId);
            Assert.Equal(
                TestCredentials.SecretAccessKey,
                client.Options.SecretAccessKey);
        }
        finally
        {
            foreach (var pair in originals)
            {
                System.Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }
        }
    }

    [Fact]
    public void AcceptsHistoricalLockerSecretAliasForMigration()
    {
        var names = new[]
        {
            "LOCKER_ACCESS_KEY_ID",
            "LOCKER_SECRET_ACCESS_KEY",
            "ACCESS_KEY_ID",
            "SECRET_ACCESS_KEY",
            "LOCKER_ACCESS_KEY_SECRET",
            "ACCESS_KEY_SECRET",
        };
        var originals = names.ToDictionary(
            name => name,
            System.Environment.GetEnvironmentVariable,
            StringComparer.Ordinal);
        try
        {
            foreach (var name in names)
            {
                System.Environment.SetEnvironmentVariable(name, null);
            }
            System.Environment.SetEnvironmentVariable(
                "ACCESS_KEY_ID",
                "10000000-0000-4000-8000-000000000001");
            System.Environment.SetEnvironmentVariable(
                "LOCKER_ACCESS_KEY_SECRET",
                "bGVnYWN5LXNlY3JldA==");

            using var client = LockerClientFactory.FromEnvironment(cliPath: "locker");

            Assert.Equal(
                "10000000-0000-4000-8000-000000000001",
                client.Options.AccessKeyId);
            Assert.Equal(
                "bGVnYWN5LXNlY3JldA==",
                client.Options.SecretAccessKey);
        }
        finally
        {
            foreach (var pair in originals)
            {
                System.Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }
        }
    }

    [Fact]
    public async Task MissingEnvironmentCredentialsFailBeforeCliResolution()
    {
        var names = new[]
        {
            "LOCKER_ACCESS_KEY_ID",
            "LOCKER_SECRET_ACCESS_KEY",
            "ACCESS_KEY_ID",
            "SECRET_ACCESS_KEY",
            "LOCKER_ACCESS_KEY_SECRET",
            "ACCESS_KEY_SECRET",
        };
        var originals = names.ToDictionary(
            name => name,
            System.Environment.GetEnvironmentVariable,
            StringComparer.Ordinal);
        try
        {
            foreach (var name in names)
            {
                System.Environment.SetEnvironmentVariable(name, null);
            }

            using var client = LockerClientFactory.FromEnvironment(
                cliPath: Path.GetFullPath(
                    Path.Combine(
                        Path.GetTempPath(),
                        $"locker-does-not-exist-{Guid.NewGuid():N}")));

            var error = await Assert.ThrowsAsync<AuthenticationError>(
                () => client.EnsureCapabilitiesAsync());

            Assert.Equal(-32001, error.Code);
            Assert.Equal("missing_credentials", error.Kind);
            Assert.Equal(
                "access key ID and secret access key are required",
                error.Message);
            Assert.False(error.Retryable);
            Assert.Null(error.RequestId);
        }
        finally
        {
            foreach (var pair in originals)
            {
                System.Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }
        }
    }

    [Fact]
    public void ScannerFactoryHelperIsFailClosed()
    {
        var values = new Dictionary<string, string?>
        {
            ["LOCKER_ACCESS_KEY_ID"] = TestCredentials.AccessKeyId,
            ["LOCKER_SECRET_ACCESS_KEY"] =
                TestCredentials.SecretAccessKey,
            ["LOCKER_CLI_PATH"] = FakeCliPath,
        };
        var originals = values.Keys.ToDictionary(
            name => name,
            System.Environment.GetEnvironmentVariable,
            StringComparer.Ordinal);
        try
        {
            foreach (var pair in values)
            {
                System.Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }

            Assert.Equal(
                "secret-value",
                LockerClientFactory.GetRequiredFromEnvironment("key"));
            Assert.Throws<ResourceNotFoundError>(
                () => LockerClientFactory.GetRequiredFromEnvironment("missing"));
        }
        finally
        {
            foreach (var pair in originals)
            {
                System.Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }
        }
    }

    private static string FakeCliPath
    {
        get
        {
            var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name
                ?? throw new InvalidOperationException("Test build configuration is unavailable.");
            var extension = OperatingSystem.IsWindows() ? ".exe" : string.Empty;
            return Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "LockerTestCli",
                "bin",
                configuration,
                "net8.0",
                $"LockerTestCli{extension}"));
        }
    }

    private static string FakeCliAssemblyPath =>
        Path.Combine(
            Path.GetDirectoryName(FakeCliPath)
                ?? throw new InvalidOperationException(
                    "Test CLI output directory is unavailable."),
            "LockerTestCli.dll");

    private static DirectoryInfo FindRepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory);
             current is not null;
             current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "VERSION"))
                && File.Exists(
                    Path.Combine(
                        current.FullName,
                        "src",
                        "Locker",
                        "Locker.csproj")))
            {
                return current;
            }
        }

        throw new InvalidOperationException(
            "The SDK repository root could not be located.");
    }
}
