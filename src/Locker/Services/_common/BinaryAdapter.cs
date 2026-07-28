namespace Locker;

/// <summary>
/// Compatibility placeholder for the removed human-CLI adapter. Use <see cref="LockerClient"/>.
/// </summary>
[Obsolete("Use LockerClient. Human CLI command strings are not a supported SDK boundary.")]
public sealed class BinaryAdapter
{
    public BinaryAdapter(
        string? accessKeyId = null,
        string? secretAccessKey = null,
        string? apiBase = null,
        string? apiVersion = null,
        bool isJson = false,
        Dictionary<string, string>? headers = null)
    {
    }

    public string Call(string cli, int timeout = 30, BaseOptions? options = null) =>
        throw new NotSupportedException(
            "Human CLI command strings were removed. Use LockerClient protocol-v1 methods.");
}
