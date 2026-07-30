using Locker;
using Xunit;

namespace LockerTests;

public sealed class RealCliConformanceTests
{
    [Fact]
    public async Task NegotiatesProtocolV1WithConfiguredRealCli()
    {
        var binary = System.Environment.GetEnvironmentVariable(
            "LOCKER_INTEGRATION_CLI");
        if (string.IsNullOrWhiteSpace(binary))
        {
            // This opt-in test runs in release CI after the exact CLI artifact
            // has been downloaded. Unit-only jobs do not carry that artifact.
            return;
        }

        Assert.True(
            File.Exists(binary),
            $"LOCKER_INTEGRATION_CLI does not exist: {binary}");

        using var client = new LockerClient(new LockerClientOptions(
            accessKeyId: TestCredentials.AccessKeyId,
            secretAccessKey: TestCredentials.SecretAccessKey,
            cliPath: binary,
            timeout: TimeSpan.FromSeconds(15)));

        await client.EnsureCapabilitiesAsync();
    }
}
