using Newtonsoft.Json.Linq;

namespace Locker;

public class EnvironmentService :
    ICreatable<Environment, EnvironmentCreateOptions>,
    IRetrievable<Environment, EnvironmentRetrieveOptions>,
    IUpdatable<Environment, EnvironmentUpdateOptions>,
    IListable<Environment, EnvironmentListOptions>
{
    private readonly LockerClient? client;

    public EnvironmentService()
    {
    }

    public EnvironmentService(LockerClient client)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<Environment> GetAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        RequireNonEmpty(name, nameof(name));
        var selectedClient = GetClient(null);
        var data = await selectedClient.CallOperationAsync(
                "environment.get",
                new JObject
                {
                    ["context"] = selectedClient.CreateContext(),
                    ["name"] = name,
                },
                cancellationToken)
            .ConfigureAwait(false);
        return ProtocolDataParser.ParseEnvironment(data);
    }

    public async Task<IReadOnlyList<Environment>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        var selectedClient = GetClient(null);
        var data = await selectedClient.CallOperationAsync(
                "environment.list",
                new JObject { ["context"] = selectedClient.CreateContext() },
                cancellationToken)
            .ConfigureAwait(false);
        return ProtocolDataParser.ParseEnvironments(data);
    }

    /// <summary>Returns one bounded page of environments.</summary>
    public async Task<EnvironmentPage> ListPageAsync(
        EnvironmentListPageOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ValidatePageOptions(options);
        var selectedClient = GetClient(null);
        var parameters = new JObject { ["context"] = selectedClient.CreateContext() };
        if (options?.PageSize is int pageSize)
        {
            parameters["page_size"] = pageSize;
        }

        AddOptional(parameters, "cursor", options?.Cursor);
        var data = await selectedClient.CallOperationAsync(
                "environment.list_page",
                parameters,
                cancellationToken)
            .ConfigureAwait(false);
        return ProtocolDataParser.ParseEnvironmentPage(data);
    }

    public async Task<Environment> CreateAsync(
        EnvironmentCreateOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        RequireNonEmpty(options.Name, nameof(options.Name));
        var selectedClient = GetClient(null);
        var parameters = new JObject
        {
            ["context"] = selectedClient.CreateContext(),
            ["name"] = options.Name,
        };
        AddOptional(parameters, "external_url", options.ExternalUrl, allowEmpty: true);
        AddOptional(parameters, "description", options.Description, allowEmpty: true);
        var data = await selectedClient.CallOperationAsync(
                "environment.create",
                parameters,
                cancellationToken)
            .ConfigureAwait(false);
        return ProtocolDataParser.ParseEnvironment(data);
    }

    public async Task<Environment> UpdateAsync(
        string name,
        EnvironmentUpdateOptions options,
        CancellationToken cancellationToken = default)
    {
        RequireNonEmpty(name, nameof(name));
        ArgumentNullException.ThrowIfNull(options);
        var changes = new JObject();
        AddOptional(changes, "name", options.Name);
        AddOptional(changes, "external_url", options.ExternalUrl, allowEmpty: true);
        AddOptional(changes, "description", options.Description, allowEmpty: true);
        if (!changes.HasValues)
        {
            throw new ArgumentException("At least one environment change is required.", nameof(options));
        }

        var selectedClient = GetClient(null);
        var data = await selectedClient.CallOperationAsync(
                "environment.update",
                new JObject
                {
                    ["context"] = selectedClient.CreateContext(),
                    ["name"] = name,
                    ["changes"] = changes,
                },
                cancellationToken)
            .ConfigureAwait(false);
        return ProtocolDataParser.ParseEnvironment(data);
    }

    public object Create(
        EnvironmentCreateOptions createOptions,
        RequestOptions? requestOptions = null) =>
        CreateForClientAsync(GetClient(requestOptions), createOptions).GetAwaiter().GetResult();

    public object List(
        EnvironmentListOptions? listOptions = null,
        RequestOptions? requestOptions = null) =>
        ListForClientAsync(GetClient(requestOptions)).GetAwaiter().GetResult();

    public object Get(
        string name,
        EnvironmentRetrieveOptions? retrieveOptions = null,
        RequestOptions? requestOptions = null) =>
        GetForClientAsync(GetClient(requestOptions), name).GetAwaiter().GetResult();

    public object Modify(
        string name,
        EnvironmentUpdateOptions updateOptions,
        RequestOptions? requestOptions = null) =>
        UpdateForClientAsync(GetClient(requestOptions), name, updateOptions)
            .GetAwaiter()
            .GetResult();

    private LockerClient GetClient(RequestOptions? requestOptions) =>
        requestOptions is null && client is not null
            ? client
            : LockerConfiguration.Instance.CreateClient(requestOptions);

    private static async Task<Environment> GetForClientAsync(
        LockerClient selectedClient,
        string name)
    {
        var data = await selectedClient.CallOperationAsync(
            "environment.get",
            new JObject
            {
                ["context"] = selectedClient.CreateContext(),
                ["name"] = name,
            },
            CancellationToken.None).ConfigureAwait(false);
        return ProtocolDataParser.ParseEnvironment(data);
    }

    private static async Task<IReadOnlyList<Environment>> ListForClientAsync(
        LockerClient selectedClient)
    {
        var data = await selectedClient.CallOperationAsync(
            "environment.list",
            new JObject { ["context"] = selectedClient.CreateContext() },
            CancellationToken.None).ConfigureAwait(false);
        return ProtocolDataParser.ParseEnvironments(data);
    }

    private static Task<Environment> CreateForClientAsync(
        LockerClient selectedClient,
        EnvironmentCreateOptions options) =>
        new EnvironmentService(selectedClient).CreateAsync(options);

    private static Task<Environment> UpdateForClientAsync(
        LockerClient selectedClient,
        string name,
        EnvironmentUpdateOptions options) =>
        new EnvironmentService(selectedClient).UpdateAsync(name, options);

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

    private static void ValidatePageOptions(EnvironmentListPageOptions? options)
    {
        if (options is null)
        {
            return;
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
