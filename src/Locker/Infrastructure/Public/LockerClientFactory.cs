namespace Locker;

public static class LockerClientFactory
{
    public const string AccessKeyIdEnvironmentVariable = "LOCKER_ACCESS_KEY_ID";
    public const string SecretAccessKeyEnvironmentVariable = "LOCKER_SECRET_ACCESS_KEY";
    public const string CliPathEnvironmentVariable = "LOCKER_CLI_PATH";

    public static LockerClient FromEnvironment(
        string? cliPath = null,
        string? apiBase = null,
        IReadOnlyDictionary<string, string>? headers = null,
        TimeSpan? timeout = null)
    {
        var accessKeyId = FirstNonEmpty(
            System.Environment.GetEnvironmentVariable(AccessKeyIdEnvironmentVariable),
            System.Environment.GetEnvironmentVariable("ACCESS_KEY_ID"));
        var secretAccessKey = FirstNonEmpty(
            System.Environment.GetEnvironmentVariable(SecretAccessKeyEnvironmentVariable),
            System.Environment.GetEnvironmentVariable("SECRET_ACCESS_KEY"),
            System.Environment.GetEnvironmentVariable("LOCKER_ACCESS_KEY_SECRET"),
            System.Environment.GetEnvironmentVariable("ACCESS_KEY_SECRET"));

        if (accessKeyId is null || secretAccessKey is null)
        {
            throw new InvalidOperationException(
                $"Set {AccessKeyIdEnvironmentVariable} and {SecretAccessKeyEnvironmentVariable}.");
        }

        return new LockerClient(new LockerClientOptions(
            accessKeyId,
            secretAccessKey,
            cliPath,
            apiBase,
            headers,
            timeout: timeout));
    }

    public static string GetRequiredFromEnvironment(
        string key,
        string? environmentName = null,
        CancellationToken cancellationToken = default)
    {
        using var client = FromEnvironment();
        return client.Secrets.GetRequired(key, environmentName, cancellationToken);
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
