using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Locker;

internal sealed class LockerCredentials
{
    private const int AuthenticationErrorCode = -32001;
    private const int MaximumSecretAccessKeyLength =
        LockerClientOptions.ProtocolNameLengthLimit;
    private static readonly Regex AccessKeyIdPattern = new(
        "^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$",
        RegexOptions.CultureInvariant
        | RegexOptions.IgnoreCase
        | RegexOptions.NonBacktracking);

    private LockerCredentials(
        string accessKeyId,
        string secretAccessKey)
    {
        AccessKeyId = accessKeyId;
        SecretAccessKey = secretAccessKey;
    }

    internal string AccessKeyId { get; }

    internal string SecretAccessKey { get; }

    internal static LockerCredentials Resolve(
        string? accessKeyId,
        string? secretAccessKey)
    {
        var normalizedAccessKeyId = accessKeyId?.Trim() ?? string.Empty;
        var normalizedSecretAccessKey =
            secretAccessKey?.Trim() ?? string.Empty;

        if (normalizedAccessKeyId.Length == 0
            || normalizedSecretAccessKey.Length == 0)
        {
            throw Error(
                "missing_credentials",
                "access key ID and secret access key are required");
        }

        if (!AccessKeyIdPattern.IsMatch(normalizedAccessKeyId))
        {
            throw Error(
                "invalid_access_key_id",
                "access key ID must be a UUIDv4");
        }

        if (!IsCanonicalBase64(normalizedSecretAccessKey))
        {
            throw Error(
                "malformed_secret_access_key",
                "secret access key must be non-empty canonical base64");
        }

        return new LockerCredentials(
            normalizedAccessKeyId,
            normalizedSecretAccessKey);
    }

    private static bool IsCanonicalBase64(string value)
    {
        if (value.Length == 0
            || value.Length > MaximumSecretAccessKeyLength
            || value.Length % 4 != 0)
        {
            return false;
        }

        var decoded = new byte[(value.Length / 4) * 3];
        try
        {
            if (!Convert.TryFromBase64String(
                    value,
                    decoded,
                    out var decodedLength)
                || decodedLength == 0)
            {
                return false;
            }

            return string.Equals(
                Convert.ToBase64String(decoded, 0, decodedLength),
                value,
                StringComparison.Ordinal);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(decoded);
        }
    }

    private static AuthenticationError Error(
        string kind,
        string message) =>
        new(
            message,
            AuthenticationErrorCode,
            requestId: null,
            kind: kind,
            retryable: false);
}
