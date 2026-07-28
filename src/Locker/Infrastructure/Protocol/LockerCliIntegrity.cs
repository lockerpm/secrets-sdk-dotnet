using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Locker;

internal static partial class LockerCliIntegrity
{
    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Regex();

    internal static bool IsSha256(string? value) =>
        value is not null && Sha256Regex().IsMatch(value);

    internal static async Task<bool> VerifyAsync(
        string path,
        long expectedSize,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.LinkTarget is not null || file.Length != expectedSize)
        {
            return false;
        }

        await using var input = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var sha = SHA256.Create();
        var digest = await sha.ComputeHashAsync(input, cancellationToken).ConfigureAwait(false);
        return CryptographicOperations.FixedTimeEquals(
            digest,
            Convert.FromHexString(expectedSha256));
    }
}
