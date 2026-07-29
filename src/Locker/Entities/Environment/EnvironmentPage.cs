namespace Locker;

/// <summary>One bounded page returned by <c>environment.list_page</c>.</summary>
public sealed class EnvironmentPage
{
    internal EnvironmentPage(IEnumerable<Environment> items, string? nextCursor)
    {
        Items = Array.AsReadOnly(items.ToArray());
        NextCursor = nextCursor;
    }

    /// <summary>The stable protocol object discriminator.</summary>
    public string Object => "environment_page";

    /// <summary>The environments in this page.</summary>
    public IReadOnlyList<Environment> Items { get; }

    /// <summary>
    /// Opaque continuation cursor, or <see langword="null"/> for the last page.
    /// </summary>
    public string? NextCursor { get; }
}
