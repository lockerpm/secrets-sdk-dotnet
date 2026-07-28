using Locker;
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
        Assert.Equal(releaseVersion, LockerSdkMetadata.Version);
        Assert.Equal(releaseVersion, LockerConfiguration.Instance.SdkVersion);
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
            System.Environment.SetEnvironmentVariable("LOCKER_ACCESS_KEY_ID", "canonical-access");
            System.Environment.SetEnvironmentVariable("LOCKER_SECRET_ACCESS_KEY", "canonical-secret");
            System.Environment.SetEnvironmentVariable("ACCESS_KEY_ID", "legacy-access");
            System.Environment.SetEnvironmentVariable("SECRET_ACCESS_KEY", "legacy-secret");
            System.Environment.SetEnvironmentVariable("LOCKER_ACCESS_KEY_SECRET", "older-secret");
            System.Environment.SetEnvironmentVariable("ACCESS_KEY_SECRET", "oldest-secret");

            using var client = LockerClientFactory.FromEnvironment(cliPath: "locker");

            Assert.Equal("canonical-access", client.Options.AccessKeyId);
            Assert.Equal("canonical-secret", client.Options.SecretAccessKey);
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
            System.Environment.SetEnvironmentVariable("ACCESS_KEY_ID", "legacy-access");
            System.Environment.SetEnvironmentVariable(
                "LOCKER_ACCESS_KEY_SECRET",
                "legacy-secret");

            using var client = LockerClientFactory.FromEnvironment(cliPath: "locker");

            Assert.Equal("legacy-access", client.Options.AccessKeyId);
            Assert.Equal("legacy-secret", client.Options.SecretAccessKey);
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
            ["LOCKER_ACCESS_KEY_ID"] = "test-access",
            ["LOCKER_SECRET_ACCESS_KEY"] = "test-secret",
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
