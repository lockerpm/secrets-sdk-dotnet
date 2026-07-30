using System.Security.Cryptography;
using System.Text;
using Locker;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace LockerTests;

public sealed class RpcErrorCatalogTests
{
    private const string CatalogSha256 =
        "bec020bea51d694371d738a9a44c17644ea66728706d7027f6bc86988ee93c7d";

    [Fact]
    public void VendoredCatalogMatchesRuntimeErrorMapping()
    {
        using var stream = typeof(LockerClient).Assembly
            .GetManifestResourceStream(
                "Locker.Protocol.locker-rpc-errors.v1.json");
        Assert.NotNull(stream);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var raw = buffer.ToArray();
        Assert.Equal(
            CatalogSha256,
            Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant());

        var catalog = JObject.Parse(
            Encoding.UTF8.GetString(raw));
        var types = new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            ["ProtocolError"] = typeof(ProtocolError),
            ["OperationError"] = typeof(APIError),
            ["OperationCancelledError"] = typeof(OperationCancelledError),
            ["RequestRejectedError"] = typeof(RequestRejectedError),
            ["ResponseTooLargeError"] = typeof(ResponseTooLargeError),
            ["ConflictError"] = typeof(ConflictError),
            ["AlreadyExistsError"] = typeof(AlreadyExistsError),
            ["ValidationError"] = typeof(ValidationError),
            ["AuthenticationError"] = typeof(AuthenticationError),
            ["PermissionDeniedError"] = typeof(PermissionDeniedError),
            ["NotFoundError"] = typeof(ResourceNotFoundError),
            ["RateLimitError"] = typeof(RateLimitError),
            ["NetworkError"] = typeof(APIConnectionError),
            ["ServerError"] = typeof(APIServerError),
            ["StorageError"] = typeof(LocalStorageError),
            ["IntegrityError"] = typeof(IntegrityError),
        };

        foreach (var catalogError in (JArray)catalog["errors"]!)
        {
            var code = catalogError["rpc_code"]!.Value<int>();
            var kind = catalogError["kind"]!.Value<string>()!;
            var expectedMessage =
                catalogError["message"]!.Value<string>()!;
            var expectedRetryable =
                catalogError["retryable"]!.Value<bool>();
            var expectedType =
                types[catalogError["sdk_error"]!.Value<string>()!];
            var mapped = ParseError(code, kind, expectedRetryable);

            Assert.Equal(expectedType, mapped.GetType());
            Assert.Equal(expectedMessage, mapped.Message);
            Assert.Equal(expectedRetryable, mapped.Retryable);
        }

        var policy =
            (JObject)catalog["unknown_server_code_policy"]!;
        var unknown = ParseError(
            policy["minimum"]!.Value<int>(),
            "future_error",
            policy["preserve_retryable"]!.Value<bool>());
        Assert.IsType<APIError>(unknown);
        Assert.Equal(policy["message"]!.Value<string>(), unknown.Message);
        Assert.Equal(
            policy["preserve_retryable"]!.Value<bool>(),
            unknown.Retryable);
    }

    private static LockerError ParseError(
        int code,
        string kind,
        bool retryable)
    {
        var envelope = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = "request-catalog",
            ["error"] = new JObject
            {
                ["code"] = code,
                ["message"] = "untrusted catalog fixture message",
                ["data"] = new JObject
                {
                    ["protocol_version"] = 1,
                    ["kind"] = kind,
                    ["retryable"] = retryable,
                },
            },
        };
        return Assert.IsAssignableFrom<LockerError>(
            Record.Exception(
                () => StrictProtocolResponse.Parse(
                    envelope.ToString(Formatting.None),
                    "request-catalog")));
    }
}
