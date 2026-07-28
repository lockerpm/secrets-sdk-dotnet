using System.Collections.ObjectModel;

namespace Locker;

public sealed class LockerClientOptions
{
    public const string DefaultApiBase = "https://api.locker.io/locker_secrets";
    public const int ProtocolRequestLimitBytes = 20 * 1024 * 1024;
    public const int ProtocolResponseLimitBytes = 20 * 1024 * 1024;
    public const int ProtocolJsonDepthLimit = 256;
    public const int ProtocolNameLengthLimit = 65_536;
    public const int ApiBaseLengthLimit = 4_096;
    public const int HeaderCountLimit = 64;

    public LockerClientOptions(
        string accessKeyId,
        string secretAccessKey,
        string? cliPath = null,
        string? apiBase = null,
        IReadOnlyDictionary<string, string>? headers = null,
        bool insecureSkipTlsVerify = false,
        TimeSpan? timeout = null,
        int maxStdoutBytes = ProtocolResponseLimitBytes,
        int maxStderrBytes = 64 * 1024,
        bool forceRefresh = false,
        int maxAgeSeconds = 120)
    {
        AccessKeyId = RequireValue(accessKeyId, nameof(accessKeyId));
        SecretAccessKey = RequireValue(secretAccessKey, nameof(secretAccessKey));
        CliPath = string.IsNullOrWhiteSpace(cliPath) ? null : cliPath;
        ApiBase = string.IsNullOrWhiteSpace(apiBase) ? DefaultApiBase : apiBase;
        Headers = new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(
                headers ?? new Dictionary<string, string>(),
                StringComparer.Ordinal));
        InsecureSkipTlsVerify = insecureSkipTlsVerify;
        Timeout = timeout ?? TimeSpan.FromSeconds(30);
        MaxStdoutBytes = maxStdoutBytes;
        MaxStderrBytes = maxStderrBytes;
        ForceRefresh = forceRefresh;
        MaxAgeSeconds = maxAgeSeconds;

        if (!Uri.TryCreate(ApiBase, UriKind.Absolute, out var apiUri)
            || (apiUri.Scheme != Uri.UriSchemeHttps && apiUri.Scheme != Uri.UriSchemeHttp))
        {
            throw new ArgumentException("API base must be an absolute HTTP or HTTPS URI.", nameof(apiBase));
        }

        if (ApiBase.Length > ApiBaseLengthLimit)
        {
            throw new ArgumentOutOfRangeException(
                nameof(apiBase),
                $"API base cannot exceed {ApiBaseLengthLimit} characters.");
        }

        if (Timeout <= TimeSpan.Zero || Timeout.TotalMilliseconds > uint.MaxValue - 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                "Timeout must be positive and supported by CancellationTokenSource.");
        }

        if (MaxStdoutBytes <= 0 || MaxStdoutBytes > ProtocolResponseLimitBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(maxStdoutBytes));
        }

        if (MaxStderrBytes <= 0 || MaxStderrBytes > 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(maxStderrBytes));
        }

        if (MaxAgeSeconds is < 0 or > 86400)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAgeSeconds));
        }

        if (Headers.Count > HeaderCountLimit)
        {
            throw new ArgumentException(
                $"Transport headers cannot contain more than {HeaderCountLimit} fields.",
                nameof(headers));
        }

        foreach (var pair in Headers)
        {
            if (string.IsNullOrWhiteSpace(pair.Key)
                || pair.Key.Any(c => char.IsControl(c) || c == ':')
                || pair.Value.Any(char.IsControl))
            {
                throw new ArgumentException("Transport headers contain an invalid name or value.", nameof(headers));
            }
        }
    }

    public string AccessKeyId { get; }
    public string SecretAccessKey { get; }
    public string? CliPath { get; }
    public string ApiBase { get; }
    public IReadOnlyDictionary<string, string> Headers { get; }
    public bool InsecureSkipTlsVerify { get; }
    public TimeSpan Timeout { get; }
    public int MaxStdoutBytes { get; }
    public int MaxStderrBytes { get; }
    public bool ForceRefresh { get; }
    public int MaxAgeSeconds { get; }

    private static string RequireValue(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > ProtocolNameLengthLimit)
        {
            throw new ArgumentException(
                $"A non-empty credential of at most {ProtocolNameLengthLimit} characters is required.",
                parameterName);
        }

        return value;
    }
}
