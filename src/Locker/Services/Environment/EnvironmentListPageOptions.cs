namespace Locker;

/// <summary>Options for one bounded page of environments.</summary>
public sealed class EnvironmentListPageOptions
{
    /// <summary>Number of items requested from the CLI, from 1 through 1000.</summary>
    public int? PageSize { get; init; }

    /// <summary>Opaque cursor returned by the previous page.</summary>
    public string? Cursor { get; init; }
}
