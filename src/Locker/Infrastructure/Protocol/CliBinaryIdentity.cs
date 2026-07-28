using Newtonsoft.Json.Linq;

namespace Locker;

internal sealed record CliBinaryIdentity(
    string CanonicalPath,
    long Length,
    DateTime CreationTimeUtc,
    DateTime LastWriteTimeUtc)
{
    internal static CliBinaryIdentity Capture(string configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath)
            || configuredPath.Contains('\0')
            || !Path.IsPathFullyQualified(configuredPath))
        {
            throw new CliRunError(
                "Locker CLI executable path must be absolute.");
        }

        var file = new FileInfo(Path.GetFullPath(configuredPath));
        file.Refresh();
        if (!file.Exists
            || file.LinkTarget is not null
            || (file.Attributes & FileAttributes.ReparsePoint) != 0
            || (file.Attributes
                & (FileAttributes.Directory | FileAttributes.Device)) != 0)
        {
            throw new CliRunError(
                "Locker CLI executable identity is unavailable or unsafe.");
        }
        if (!OperatingSystem.IsWindows())
        {
            _ = LockerCliInstaller.GetUnixPathOwnerId(
                file.FullName,
                expectedDirectory: false);
        }

        return new CliBinaryIdentity(
            file.FullName,
            file.Length,
            file.CreationTimeUtc,
            file.LastWriteTimeUtc);
    }
}

internal sealed class CliBinaryChangedError : ProtocolError
{
    internal CliBinaryChangedError(bool safeToRetry)
        : base("Locker CLI executable changed after capability negotiation.")
    {
        SafeToRetry = safeToRetry;
    }

    internal bool SafeToRetry { get; }
}

internal sealed record ProtocolCallResult(
    JToken Data,
    string CliVersion,
    CliBinaryIdentity BinaryIdentity);

internal sealed record DecodedProtocolResult(
    JToken Data,
    string CliVersion);
