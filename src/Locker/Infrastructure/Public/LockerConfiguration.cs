using Locker.Infrastructure;
using Newtonsoft.Json;

namespace Locker;

public sealed class LockerConfiguration
{
    private static readonly Lazy<LockerConfiguration> LazyInstance =
        new(() => new LockerConfiguration(), LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly object sync = new();
    private Dictionary<string, string> headers = new(StringComparer.Ordinal);
    private string? apiBase;
    private string? accessKeyId;
    private string? secretAccessKey;
    private string? apiVersion;

    private LockerConfiguration()
    {
    }

    public static LockerConfiguration Instance => LazyInstance.Value;

    public void Init(
        string? apiBase = null,
        string? accessKeyId = null,
        string? secretAccessKey = null,
        string? apiVersion = null,
        Dictionary<string, string>? headers = null,
        string? envPath = null)
    {
        if (envPath is not null)
        {
            throw new NotSupportedException(
                "Implicit .env loading was removed. Load configuration in the application and use canonical LOCKER_* variables.");
        }

        lock (sync)
        {
            this.apiBase = apiBase;
            this.accessKeyId = accessKeyId;
            this.secretAccessKey = secretAccessKey;
            this.apiVersion = apiVersion;
            this.headers = headers is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(headers, StringComparer.Ordinal);
        }
    }

    public string? ApiBase
    {
        get { lock (sync) { return apiBase; } }
        set { lock (sync) { apiBase = value; } }
    }

    public string? AccessKeyId
    {
        get
        {
            lock (sync)
            {
                return accessKeyId
                    ?? System.Environment.GetEnvironmentVariable(
                        LockerClientFactory.AccessKeyIdEnvironmentVariable)
                    ?? System.Environment.GetEnvironmentVariable("ACCESS_KEY_ID");
            }
        }
        set { lock (sync) { accessKeyId = value; } }
    }

    public string? SecretAccessKey
    {
        get
        {
            lock (sync)
            {
                return secretAccessKey
                    ?? System.Environment.GetEnvironmentVariable(
                        LockerClientFactory.SecretAccessKeyEnvironmentVariable)
                    ?? System.Environment.GetEnvironmentVariable("SECRET_ACCESS_KEY")
                    ?? System.Environment.GetEnvironmentVariable("LOCKER_ACCESS_KEY_SECRET")
                    ?? System.Environment.GetEnvironmentVariable("ACCESS_KEY_SECRET");
            }
        }
        set { lock (sync) { secretAccessKey = value; } }
    }

    public string? ApiVersion
    {
        get { lock (sync) { return apiVersion; } }
        set { lock (sync) { apiVersion = value; } }
    }

    public Dictionary<string, string> Headers
    {
        get { lock (sync) { return new Dictionary<string, string>(headers, StringComparer.Ordinal); } }
        set
        {
            lock (sync)
            {
                headers = value is null
                    ? new Dictionary<string, string>(StringComparer.Ordinal)
                    : new Dictionary<string, string>(value, StringComparer.Ordinal);
            }
        }
    }

    public string SdkVersion => LockerSdkMetadata.Version;

    public string LockerDir => Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile),
        ".locker");

    public string? BinaryFilePath => null;

    public static JsonSerializerSettings SerializerSettings { get; set; } = new()
    {
        Converters = new List<JsonConverter> { new LockerObjectConverter() },
        DateParseHandling = DateParseHandling.None,
        MaxDepth = 128,
    };

    internal LockerClient CreateClient(RequestOptions? requestOptions = null)
    {
        var access = requestOptions?.AccessKeyId ?? AccessKeyId;
        var secret = requestOptions?.SecretAccessKey ?? SecretAccessKey;
        var optionHeaders = requestOptions?.Headers?.ToDictionary(
            pair => pair.Key,
            pair => Convert.ToString(
                pair.Value,
                System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            StringComparer.Ordinal);

        return new LockerClient(new LockerClientOptions(
            access ?? string.Empty,
            secret ?? string.Empty,
            requestOptions?.CliPath,
            requestOptions?.ApiBase ?? ApiBase,
            optionHeaders ?? Headers,
            timeout: TimeSpan.FromSeconds(requestOptions?.Timeout ?? 30)));
    }
}
