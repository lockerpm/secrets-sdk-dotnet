using System.Reflection;

namespace Locker;

internal static class LockerSdkMetadata
{
    internal const int ProtocolVersion = 1;

    internal static string Version { get; } = ResolveVersion();

    private static string ResolveVersion()
    {
        var assembly = typeof(LockerSdkMetadata).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            var metadataSeparator = informationalVersion.IndexOf(
                '+',
                StringComparison.Ordinal);
            return metadataSeparator < 0
                ? informationalVersion
                : informationalVersion[..metadataSeparator];
        }

        return assembly.GetName().Version?.ToString(3) ?? "unknown";
    }
}
