using Newtonsoft.Json.Linq;

namespace Locker;

public class SecretService :
    ICreatable<Secret, SecretCreateOptions>,
    IRetrievable<Secret, SecretRetrieveOptions>,
    IUpdatable<Secret, SecretUpdateOptions>,
    IListable<Secret, SecretListOptions>
{
    private readonly LockerClient? client;

    public SecretService()
    {
    }

    public SecretService(LockerClient client)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<Secret> GetAsync(
        string key,
        string? environmentName = null,
        CancellationToken cancellationToken = default)
    {
        RequireNonEmpty(key, nameof(key));
        var selectedClient = GetClient(null);
        var parameters = new JObject
        {
            ["context"] = selectedClient.CreateContext(),
            ["key"] = key,
        };
        AddOptional(parameters, "environment", environmentName);
        var data = await selectedClient.CallOperationAsync("secret.get", parameters, cancellationToken)
            .ConfigureAwait(false);
        return ProtocolDataParser.ParseSecret(data);
    }

    public async Task<IReadOnlyList<Secret>> ListAsync(
        string? environmentName = null,
        CancellationToken cancellationToken = default)
    {
        var selectedClient = GetClient(null);
        var parameters = new JObject { ["context"] = selectedClient.CreateContext() };
        AddOptional(parameters, "environment", environmentName);
        var data = await selectedClient.CallOperationAsync("secret.list", parameters, cancellationToken)
            .ConfigureAwait(false);
        return ProtocolDataParser.ParseSecrets(data);
    }

    /// <summary>Returns one bounded page of secrets.</summary>
    public async Task<SecretPage> ListPageAsync(
        SecretListPageOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ValidatePageOptions(options);
        var selectedClient = GetClient(null);
        var parameters = new JObject { ["context"] = selectedClient.CreateContext() };
        AddOptional(parameters, "environment", options?.EnvironmentName);
        if (options?.PageSize is int pageSize)
        {
            parameters["page_size"] = pageSize;
        }

        AddOptional(parameters, "cursor", options?.Cursor);
        var data = await selectedClient.CallOperationAsync(
                "secret.list_page",
                parameters,
                cancellationToken)
            .ConfigureAwait(false);
        return ProtocolDataParser.ParseSecretPage(data);
    }

    public async Task<Secret> CreateAsync(
        SecretCreateOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        RequireNonEmpty(options.Key, nameof(options.Key));
        ArgumentNullException.ThrowIfNull(options.Value);
        var selectedClient = GetClient(null);
        var parameters = new JObject
        {
            ["context"] = selectedClient.CreateContext(),
            ["key"] = options.Key,
            ["value"] = options.Value,
        };
        AddOptional(parameters, "environment", options.EnvironmentName);
        AddOptional(parameters, "description", options.Description, allowEmpty: true);
        var data = await selectedClient.CallOperationAsync("secret.create", parameters, cancellationToken)
            .ConfigureAwait(false);
        return ProtocolDataParser.ParseSecret(data);
    }

    public async Task<Secret> UpdateAsync(
        string key,
        SecretUpdateOptions options,
        string? environmentName = null,
        CancellationToken cancellationToken = default)
    {
        RequireNonEmpty(key, nameof(key));
        ArgumentNullException.ThrowIfNull(options);
        if (options.ClearEnvironment && options.EnvironmentName is not null)
        {
            throw new ArgumentException(
                "EnvironmentName and ClearEnvironment cannot both be set.",
                nameof(options));
        }

        var changes = new JObject();
        AddOptional(changes, "key", options.Key);
        AddOptional(changes, "value", options.Value, allowEmpty: true);
        AddOptional(changes, "description", options.Description, allowEmpty: true);
        if (options.ClearEnvironment)
        {
            changes["environment"] = JValue.CreateNull();
        }
        else
        {
            AddOptional(changes, "environment", options.EnvironmentName);
        }

        if (!changes.HasValues)
        {
            throw new ArgumentException("At least one secret change is required.", nameof(options));
        }

        var selectedClient = GetClient(null);
        var parameters = new JObject
        {
            ["context"] = selectedClient.CreateContext(),
            ["key"] = key,
            ["changes"] = changes,
        };
        AddOptional(parameters, "environment", environmentName);
        var data = await selectedClient.CallOperationAsync("secret.update", parameters, cancellationToken)
            .ConfigureAwait(false);
        return ProtocolDataParser.ParseSecret(data);
    }

    public string GetRequired(
        string key,
        string? environmentName = null,
        CancellationToken cancellationToken = default) =>
        GetAsync(key, environmentName, cancellationToken).GetAwaiter().GetResult().Value;

    public async Task<string> GetRequiredAsync(
        string key,
        string? environmentName = null,
        CancellationToken cancellationToken = default) =>
        (await GetAsync(key, environmentName, cancellationToken).ConfigureAwait(false)).Value;

    public string? GetOrDefault(
        string key,
        string? defaultValue = null,
        string? environmentName = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return GetRequired(key, environmentName, cancellationToken);
        }
        catch (ResourceNotFoundError)
        {
            return defaultValue;
        }
    }

    public object List(SecretListOptions? listOptions = null, RequestOptions? requestOptions = null)
    {
        var selectedClient = GetClient(requestOptions);
        return ListForClientAsync(selectedClient, listOptions?.EnvironmentName).GetAwaiter().GetResult();
    }

    public object Create(SecretCreateOptions createOptions, RequestOptions? requestOptions = null) =>
        CreateForClientAsync(GetClient(requestOptions), createOptions).GetAwaiter().GetResult();

    public object Get(
        string name,
        SecretRetrieveOptions? retrieveOptions = null,
        RequestOptions? requestOptions = null) =>
        GetForClientAsync(GetClient(requestOptions), name, retrieveOptions?.EnvironmentName)
            .GetAwaiter()
            .GetResult();

    public object Get(
        string name,
        string environmentName,
        SecretRetrieveOptions? retrieveOptions = null,
        RequestOptions? requestOptions = null) =>
        GetForClientAsync(GetClient(requestOptions), name, environmentName).GetAwaiter().GetResult();

    public string GetSecret(
        string name,
        string defaultValue = "",
        string environmentName = "",
        SecretRetrieveOptions? retrieveOptions = null,
        RequestOptions? requestOptions = null)
    {
        try
        {
            var secret = GetForClientAsync(
                    GetClient(requestOptions),
                    name,
                    string.IsNullOrEmpty(environmentName) ? null : environmentName)
                .GetAwaiter()
                .GetResult();
            return secret.Value;
        }
        catch (ResourceNotFoundError)
        {
            return defaultValue;
        }
    }

    public object Modify(
        string name,
        SecretUpdateOptions updateOptions,
        RequestOptions? requestOptions = null) =>
        UpdateForClientAsync(GetClient(requestOptions), name, updateOptions, null)
            .GetAwaiter()
            .GetResult();

    public object Modify(
        string name,
        string environmentName,
        SecretUpdateOptions updateOptions,
        RequestOptions? requestOptions = null) =>
        UpdateForClientAsync(GetClient(requestOptions), name, updateOptions, environmentName)
            .GetAwaiter()
            .GetResult();

    private LockerClient GetClient(RequestOptions? requestOptions) =>
        requestOptions is null && client is not null
            ? client
            : LockerConfiguration.Instance.CreateClient(requestOptions);

    private static async Task<Secret> GetForClientAsync(
        LockerClient selectedClient,
        string key,
        string? environmentName)
    {
        var parameters = new JObject
        {
            ["context"] = selectedClient.CreateContext(),
            ["key"] = key,
        };
        AddOptional(parameters, "environment", environmentName);
        var data = await selectedClient.CallOperationAsync(
            "secret.get",
            parameters,
            CancellationToken.None).ConfigureAwait(false);
        return ProtocolDataParser.ParseSecret(data);
    }

    private static async Task<IReadOnlyList<Secret>> ListForClientAsync(
        LockerClient selectedClient,
        string? environmentName)
    {
        var parameters = new JObject { ["context"] = selectedClient.CreateContext() };
        AddOptional(parameters, "environment", environmentName);
        var data = await selectedClient.CallOperationAsync(
            "secret.list",
            parameters,
            CancellationToken.None).ConfigureAwait(false);
        return ProtocolDataParser.ParseSecrets(data);
    }

    private static async Task<Secret> CreateForClientAsync(
        LockerClient selectedClient,
        SecretCreateOptions options)
    {
        var parameters = new JObject
        {
            ["context"] = selectedClient.CreateContext(),
            ["key"] = options.Key,
            ["value"] = options.Value,
        };
        AddOptional(parameters, "environment", options.EnvironmentName);
        AddOptional(parameters, "description", options.Description, allowEmpty: true);
        var data = await selectedClient.CallOperationAsync(
            "secret.create",
            parameters,
            CancellationToken.None).ConfigureAwait(false);
        return ProtocolDataParser.ParseSecret(data);
    }

    private static Task<Secret> UpdateForClientAsync(
        LockerClient selectedClient,
        string key,
        SecretUpdateOptions options,
        string? environmentName) =>
        new SecretService(selectedClient).UpdateAsync(key, options, environmentName);

    private static void AddOptional(
        JObject target,
        string name,
        string? value,
        bool allowEmpty = false)
    {
        if (value is not null && (allowEmpty || value.Length > 0))
        {
            target[name] = value;
        }
    }

    private static void RequireNonEmpty(string value, string parameterName)
    {
        if (string.IsNullOrEmpty(value))
        {
            throw new ArgumentException("Value cannot be null or empty.", parameterName);
        }
    }

    private static void ValidatePageOptions(SecretListPageOptions? options)
    {
        if (options is null)
        {
            return;
        }

        if (options.EnvironmentName is not null)
        {
            if (options.EnvironmentName.Length is < 1
                or > LockerClientOptions.ProtocolNameLengthLimit)
            {
                throw new ArgumentException(
                    "EnvironmentName must contain between 1 and 65536 characters.",
                    nameof(options));
            }
        }

        if (options.PageSize is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "PageSize must be between 1 and 1000.");
        }

        if (options.Cursor is not null
            && (options.Cursor.Length is < 1 or > 4096))
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Cursor must contain between 1 and 4096 characters.");
        }
    }
}
