namespace Locker;

/// <summary>Options for one bounded page of secrets.</summary>
public sealed class SecretListPageOptions
{
    /// <summary>Optional environment name used to filter the page.</summary>
    public string? EnvironmentName { get; init; }

    /// <summary>Number of items requested from the CLI, from 1 through 1000.</summary>
    public int? PageSize { get; init; }

    /// <summary>Opaque cursor returned by the previous page.</summary>
    public string? Cursor { get; init; }
}
