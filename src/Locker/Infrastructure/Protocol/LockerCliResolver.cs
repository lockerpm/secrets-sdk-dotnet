namespace Locker;

internal static class LockerCliResolver
{
    internal static async Task<string> ResolveAsync(
        LockerClientOptions options,
        CancellationToken cancellationToken)
    {
        return await ResolveAsync(
            options,
            async token =>
            {
                using var installer = new LockerCliInstaller();
                return await installer.ResolveAsync(token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<string> ResolveAsync(
        LockerClientOptions options,
        Func<CancellationToken, Task<string>> managedResolver,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(managedResolver);
        if (options.CliPath is not null)
        {
            return ValidateExplicit(options.CliPath, "configured CLI path");
        }

        var environmentPath = System.Environment.GetEnvironmentVariable(
            LockerClientFactory.CliPathEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(environmentPath))
        {
            return ValidateExplicit(
                environmentPath,
                LockerClientFactory.CliPathEnvironmentVariable);
        }

        return await managedResolver(cancellationToken).ConfigureAwait(false);
    }

    internal static string GetCanonicalManagedRoot()
    {
        var home = System.Environment.GetFolderPath(
            System.Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
        {
            throw new CliRunError("The user profile directory is unavailable.");
        }

        return Path.Combine(home, ".locker", "sdk-cli", "dotnet");
    }

    private static string ValidateExplicit(string path, string source)
    {
        if (path.Contains('\0') || !Path.IsPathFullyQualified(path))
        {
            throw new CliRunError(
                $"The {source} must be an absolute regular non-link file.");
        }

        var file = new FileInfo(Path.GetFullPath(path));
        file.Refresh();
        if (!file.Exists
            || file.LinkTarget is not null
            || (file.Attributes & FileAttributes.ReparsePoint) != 0
            || (file.Attributes
                & (FileAttributes.Directory | FileAttributes.Device)) != 0)
        {
            throw new CliRunError(
                $"The {source} must be an absolute regular non-link file.");
        }
        if (!OperatingSystem.IsWindows())
        {
            _ = LockerCliInstaller.GetUnixPathOwnerId(
                file.FullName,
                expectedDirectory: false);
        }

        return file.FullName;
    }
}
