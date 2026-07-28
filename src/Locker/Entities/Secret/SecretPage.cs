namespace Locker;

/// <summary>One bounded page returned by <c>secret.list_page</c>.</summary>
public sealed class SecretPage
{
    internal SecretPage(IEnumerable<Secret> items, string? nextCursor)
    {
        Items = Array.AsReadOnly(items.ToArray());
        NextCursor = nextCursor;
    }

    /// <summary>The stable protocol object discriminator.</summary>
    public string Object => "secret_page";

    /// <summary>The secrets in this page.</summary>
    public IReadOnlyList<Secret> Items { get; }

    /// <summary>
    /// Opaque continuation cursor, or <see langword="null"/> for the last page.
    /// </summary>
    public string? NextCursor { get; }
}
