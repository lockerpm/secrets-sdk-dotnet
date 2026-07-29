using System.Globalization;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Locker;

internal static class StrictProtocolResponse
{
    internal static DecodedProtocolResult Parse(
        string json,
        string expectedRequestId,
        int maxJsonDepth = LockerClientOptions.ProtocolJsonDepthLimit)
    {
        JObject envelope;
        try
        {
            envelope = ParseSingleObject(json, maxJsonDepth);
        }
        catch (JsonException)
        {
            throw new ProtocolError("Locker CLI returned malformed JSON.");
        }

        RequireString(envelope, "jsonrpc", "2.0");
        var id = Require(envelope, "id");
        if (id.Type != JTokenType.String || !string.Equals((string?)id, expectedRequestId, StringComparison.Ordinal))
        {
            throw new ProtocolError("Locker CLI response ID does not match the request.", requestId: expectedRequestId);
        }

        var hasResult = envelope.TryGetValue("result", out var result);
        var hasError = envelope.TryGetValue("error", out var error);
        if (hasResult == hasError)
        {
            throw new ProtocolError(
                "Locker CLI response must contain exactly one of result or error.",
                requestId: expectedRequestId);
        }

        if (hasError)
        {
            throw CreateError(RequireObject(error!, "error"), expectedRequestId);
        }

        var wrapper = RequireObject(result!, "result");
        RequireInteger(
            wrapper,
            "protocol_version",
            LockerSdkMetadata.ProtocolVersion);
        var data = Require(wrapper, "data");
        var meta = RequireObject(Require(wrapper, "meta"), "result.meta");
        var cliVersion = RequireString(meta, "cli_version");
        if (string.IsNullOrWhiteSpace(cliVersion))
        {
            throw new ProtocolError("Locker CLI result version is empty.");
        }

        return new DecodedProtocolResult(data, cliVersion);
    }

    internal static JObject ParseSingleObject(
        string json,
        int maxJsonDepth = LockerClientOptions.ProtocolJsonDepthLimit)
    {
        if (maxJsonDepth <= 0
            || maxJsonDepth > LockerClientOptions.ProtocolJsonDepthLimit)
        {
            throw new ArgumentOutOfRangeException(nameof(maxJsonDepth));
        }

        ValidateEscapedUnicode(json);

        using var text = new StringReader(json);
        using var reader = new JsonTextReader(text)
        {
            DateParseHandling = DateParseHandling.None,
            FloatParseHandling = FloatParseHandling.Decimal,
            MaxDepth = maxJsonDepth,
            SupportMultipleContent = true,
        };

        ValidateNoDuplicateProperties(reader);

        using var parseText = new StringReader(json);
        using var parseReader = new JsonTextReader(parseText)
        {
            DateParseHandling = DateParseHandling.None,
            FloatParseHandling = FloatParseHandling.Decimal,
            MaxDepth = maxJsonDepth,
            SupportMultipleContent = true,
        };
        if (!parseReader.Read())
        {
            throw new JsonReaderException("Empty JSON.");
        }

        if (JToken.ReadFrom(parseReader) is not JObject result)
        {
            throw new JsonReaderException("Root JSON value must be an object.");
        }

        if (parseReader.Read())
        {
            throw new JsonReaderException("Trailing JSON value.");
        }

        return result;
    }

    private static void ValidateEscapedUnicode(string json)
    {
        var inString = false;
        for (var index = 0; index < json.Length; index++)
        {
            var current = json[index];
            if (!inString)
            {
                if (current == '"')
                {
                    inString = true;
                }

                continue;
            }

            if (current == '"')
            {
                inString = false;
                continue;
            }

            if (current != '\\')
            {
                continue;
            }

            if (++index >= json.Length)
            {
                return;
            }

            if (json[index] != 'u')
            {
                continue;
            }

            var codeUnit = ReadEscapedCodeUnit(json, index);
            index += 4;
            if (char.IsLowSurrogate((char)codeUnit))
            {
                throw new JsonReaderException("Unpaired Unicode surrogate.");
            }

            if (!char.IsHighSurrogate((char)codeUnit))
            {
                continue;
            }

            if (index + 6 >= json.Length
                || json[index + 1] != '\\'
                || json[index + 2] != 'u')
            {
                throw new JsonReaderException("Unpaired Unicode surrogate.");
            }

            var low = ReadEscapedCodeUnit(json, index + 2);
            if (!char.IsLowSurrogate((char)low))
            {
                throw new JsonReaderException("Unpaired Unicode surrogate.");
            }

            index += 6;
        }
    }

    private static int ReadEscapedCodeUnit(string json, int uIndex)
    {
        if (uIndex + 4 >= json.Length)
        {
            throw new JsonReaderException("Incomplete Unicode escape.");
        }

        var value = 0;
        for (var offset = 1; offset <= 4; offset++)
        {
            var digit = json[uIndex + offset];
            value <<= 4;
            value += digit switch
            {
                >= '0' and <= '9' => digit - '0',
                >= 'a' and <= 'f' => digit - 'a' + 10,
                >= 'A' and <= 'F' => digit - 'A' + 10,
                _ => throw new JsonReaderException("Invalid Unicode escape."),
            };
        }

        return value;
    }

    private static void ValidateNoDuplicateProperties(JsonTextReader reader)
    {
        var objectProperties = new Stack<HashSet<string>>();
        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonToken.StartObject:
                    objectProperties.Push(new HashSet<string>(StringComparer.Ordinal));
                    break;
                case JsonToken.PropertyName:
                    ValidateUnicode((string)reader.Value!);
                    if (objectProperties.Count == 0
                        || !objectProperties.Peek().Add((string)reader.Value!))
                    {
                        throw new JsonReaderException("Duplicate JSON object property.");
                    }

                    break;
                case JsonToken.String:
                    ValidateUnicode((string)reader.Value!);
                    break;
                case JsonToken.EndObject:
                    objectProperties.Pop();
                    break;
                case JsonToken.Comment:
                case JsonToken.Undefined:
                case JsonToken.Date:
                case JsonToken.Bytes:
                    throw new JsonReaderException("Unsupported JSON token.");
            }
        }
    }

    private static void ValidateUnicode(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (char.IsHighSurrogate(value[index]))
            {
                if (index + 1 >= value.Length
                    || !char.IsLowSurrogate(value[index + 1]))
                {
                    throw new JsonReaderException("Unpaired Unicode surrogate.");
                }

                index++;
            }
            else if (char.IsLowSurrogate(value[index]))
            {
                throw new JsonReaderException("Unpaired Unicode surrogate.");
            }
        }
    }

    private static LockerError CreateError(JObject error, string requestId)
    {
        var code = RequireInteger(error, "code");
        var message = RequireString(error, "message");
        var data = RequireObject(Require(error, "data"), "error.data");
        RequireInteger(
            data,
            "protocol_version",
            LockerSdkMetadata.ProtocolVersion);
        var kind = RequireString(data, "kind");
        if (!IsValidErrorKind(kind) || !IsValidErrorMessage(message))
        {
            throw new ProtocolError(
                "Locker CLI error kind is invalid.",
                requestId: requestId);
        }
        var retryableToken = Require(data, "retryable");
        if (retryableToken.Type != JTokenType.Boolean)
        {
            throw new ProtocolError("Locker CLI error retryable field has the wrong type.", requestId: requestId);
        }

        var retryable = (bool)retryableToken;
        int? retryAfterSeconds = null;
        if (data.TryGetValue(
            "retry_after_seconds",
            StringComparison.Ordinal,
            out var retryAfterToken))
        {
            if (retryAfterToken.Type != JTokenType.Integer)
            {
                throw new ProtocolError(
                    "Locker CLI error retry-after field has the wrong type.",
                    requestId: requestId);
            }
            int value;
            try
            {
                value = retryAfterToken.ToObject<int>();
            }
            catch (Exception ex) when (ex is FormatException or OverflowException)
            {
                throw new ProtocolError(
                    "Locker CLI error retry-after field is outside the integer range.",
                    requestId: requestId);
            }
            if (value is < 0 or > 86400)
            {
                throw new ProtocolError(
                    "Locker CLI error retry-after field has an unsupported value.",
                    requestId: requestId);
            }
            if (code == -32029 && kind == "rate_limited")
            {
                retryAfterSeconds = value;
            }
        }
        string? serverRequestId = null;
        if (data.TryGetValue(
            "server_request_id",
            StringComparison.Ordinal,
            out var serverRequestToken))
        {
            if (serverRequestToken.Type != JTokenType.String)
            {
                throw new ProtocolError(
                    "Locker CLI error server request ID has the wrong type.",
                    requestId: requestId);
            }
            serverRequestId = (string?)serverRequestToken;
            if (serverRequestId is null
                || !IsValidServerRequestId(serverRequestId))
            {
                throw new ProtocolError(
                    "Locker CLI error server request ID has an unsupported value.",
                    requestId: requestId);
            }
        }
        var effectiveRetryable = retryable
            && !IsNormativelyNonRetryable(code, kind);
        var safeMessage = SafeErrorMessage(code, kind);
        if (!IsStandardProtocolCode(code)
            && !IsLockerServerErrorCode(code))
        {
            return WithServerRequestId(new ProtocolError(
                "unsupported JSON-RPC error code",
                code,
                requestId,
                kind), serverRequestId);
        }
        if ((code == -32009 || code == -32000)
            && IsAlreadyExistsKind(kind))
        {
            return WithServerRequestId(new AlreadyExistsError(
                safeMessage,
                code,
                requestId,
                kind,
                effectiveRetryable), serverRequestId);
        }
        if (code == -32009 || (code == -32000 && kind == "conflict"))
        {
            return WithServerRequestId(new ConflictError(
                safeMessage,
                code,
                requestId,
                kind,
                effectiveRetryable), serverRequestId);
        }
        if (code == -32022
            || (code == -32000 && kind == "validation_error"))
        {
            return WithServerRequestId(new ValidationError(
                safeMessage,
                code,
                requestId,
                kind,
                effectiveRetryable), serverRequestId);
        }
        if (code == -32070
            || (code == -32000 && IsIntegrityKind(kind)))
        {
            return WithServerRequestId(new IntegrityError(
                safeMessage,
                code,
                requestId,
                kind,
                effectiveRetryable), serverRequestId);
        }
        if (code == -32000 && kind == "request_rejected")
        {
            return WithServerRequestId(new RequestRejectedError(
                safeMessage,
                code,
                requestId,
                kind,
                false), serverRequestId);
        }
        if (code == -32000 && kind == "response_too_large")
        {
            return WithServerRequestId(new ResponseTooLargeError(
                safeMessage,
                code,
                requestId,
                kind,
                false), serverRequestId);
        }
        if (code == -32000 && kind == "cancelled")
        {
            return WithServerRequestId(new OperationCancelledError(
                safeMessage,
                code,
                requestId,
                kind,
                false), serverRequestId);
        }
        LockerError mapped = code switch
        {
            -32001 => new AuthenticationError(safeMessage, code, requestId, kind, effectiveRetryable),
            -32003 => new PermissionDeniedError(safeMessage, code, requestId, kind, effectiveRetryable),
            -32004 => new ResourceNotFoundError(safeMessage, code, requestId, kind, effectiveRetryable),
            -32029 => new RateLimitError(
                safeMessage,
                code,
                requestId,
                kind,
                effectiveRetryable,
                retryAfterSeconds),
            -32050 => new APIConnectionError(safeMessage, code, requestId, kind, effectiveRetryable),
            -32051 => new APIServerError(safeMessage, code, requestId, kind, effectiveRetryable),
            -32060 => new LocalStorageError(safeMessage, code, requestId, kind, effectiveRetryable),
            -32000 => new APIError(safeMessage, code, requestId, kind, effectiveRetryable),
            -32700 or -32600 or -32601 or -32602 or -32603 =>
                new ProtocolError(safeMessage, code, requestId, kind),
            _ => new APIError(safeMessage, code, requestId, kind, effectiveRetryable),
        };
        return WithServerRequestId(mapped, serverRequestId);
    }

    private static T WithServerRequestId<T>(
        T error,
        string? serverRequestId)
        where T : LockerError
    {
        error.ServerRequestId = serverRequestId;
        return error;
    }

    private static string SafeErrorMessage(int code, string kind)
    {
        if ((code == -32009 || code == -32000)
            && IsAlreadyExistsKind(kind))
        {
            return kind switch
            {
                "secret_already_exists" =>
                    "a secret with this key already exists",
                "environment_already_exists" =>
                    "an environment with this name already exists",
                _ => "the requested resource already exists",
            };
        }

        return code switch
        {
            -32700 => "the Locker CLI returned invalid JSON",
            -32600 => "the Locker CLI rejected the request envelope",
            -32601 => "the requested Locker operation is not supported",
            -32602 => "the Locker request parameters are invalid",
            -32603 => "the Locker CLI encountered an internal protocol error",
            -32001 => "authentication failed",
            -32003 => "you do not have permission to perform this operation",
            -32004 => kind switch
            {
                "secret_not_found" => "the requested secret was not found",
                "environment_not_found" => "the requested environment was not found",
                _ => "the requested resource was not found",
            },
            -32009 => "the operation conflicts with current state",
            -32022 => "the request is invalid",
            -32029 => "too many requests; retry later",
            -32050 => kind == "network_timeout"
                ? "network request timed out"
                : "network request failed",
            -32051 => kind == "internal_error"
                ? "the request could not be completed"
                : "the service is temporarily unavailable",
            -32060 => "local storage operation failed",
            -32070 => IntegrityMessage(kind),
            -32000 => kind switch
            {
                "conflict" => "the operation conflicts with current state",
                "validation_error" => "the request is invalid",
                "request_rejected" => "the request is invalid",
                "response_too_large" => "protocol response exceeds the size limit",
                "cancelled" => "request cancelled",
                "integrity_error" => "stored data failed an integrity check",
                "transport_integrity_error" => "transport integrity verification failed",
                "data_integrity_error" or "data_error" => "data integrity verification failed",
                _ => "the Locker operation failed",
            },
            _ => "the Locker operation failed",
        };
    }

    private static bool IsAlreadyExistsKind(string kind) =>
        kind is "already_exists"
            or "secret_already_exists"
            or "environment_already_exists"
            or "duplicate_hash";

    private static bool IsNormativelyNonRetryable(int code, string kind) =>
        IsStandardProtocolCode(code)
        || code is -32000
            or -32001
            or -32003
            or -32004
            or -32009
            or -32022
            or -32060
            or -32070
        || (code == -32051 && kind == "internal_error");

    private static bool IsIntegrityKind(string kind) =>
        kind is "integrity_error"
            or "transport_integrity_error"
            or "data_integrity_error"
            or "data_error";

    private static string IntegrityMessage(string kind) => kind switch
    {
        "integrity_error" => "stored data failed an integrity check",
        "transport_integrity_error" => "transport integrity verification failed",
        "data_integrity_error" or "data_error" => "data integrity verification failed",
        _ => "data integrity verification failed",
    };

    private static bool IsStandardProtocolCode(int code) =>
        code is -32700 or -32600 or -32601 or -32602 or -32603;

    private static bool IsLockerServerErrorCode(int code) =>
        code is >= -32099 and <= -32000;

    private static bool IsValidErrorKind(string kind)
    {
        if (kind.Length is < 1 or > 64
            || kind[0] is < 'a' or > 'z')
        {
            return false;
        }
        foreach (var value in kind.AsSpan(1))
        {
            if (value is not (>= 'a' and <= 'z')
                && value is not (>= '0' and <= '9')
                && value != '_')
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsValidErrorMessage(string message)
    {
        if (message.Length == 0)
        {
            return false;
        }
        var count = 0;
        foreach (var value in message.EnumerateRunes())
        {
            if (++count > 512
                || value.Value <= 0x1f
                || value.Value is >= 0x7f and <= 0x9f)
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsValidServerRequestId(string requestId)
    {
        if (requestId.Length is < 16 or > 128)
        {
            return false;
        }
        foreach (var value in requestId)
        {
            if (value is not (>= 'A' and <= 'Z')
                && value is not (>= 'a' and <= 'z')
                && value is not (>= '0' and <= '9')
                && value is not ('_' or '-'))
            {
                return false;
            }
        }
        return true;
    }

    private static JToken Require(JObject value, string name)
    {
        if (!value.TryGetValue(name, StringComparison.Ordinal, out var token) || token is null)
        {
            throw new ProtocolError($"Locker CLI response is missing required field {name}.");
        }

        return token;
    }

    private static JObject RequireObject(JToken token, string name)
    {
        if (token is not JObject value)
        {
            throw new ProtocolError($"Locker CLI response field {name} has the wrong type.");
        }

        return value;
    }

    private static string RequireString(JObject value, string name, string? exact = null)
    {
        var token = Require(value, name);
        if (token.Type != JTokenType.String)
        {
            throw new ProtocolError($"Locker CLI response field {name} has the wrong type.");
        }

        var result = (string?)token
            ?? throw new ProtocolError($"Locker CLI response field {name} cannot be null.");
        if (exact is not null && !string.Equals(result, exact, StringComparison.Ordinal))
        {
            throw new ProtocolError($"Locker CLI response field {name} has an unsupported value.");
        }

        return result;
    }

    private static int RequireInteger(JObject value, string name, int? exact = null)
    {
        var token = Require(value, name);
        if (token.Type != JTokenType.Integer)
        {
            throw new ProtocolError($"Locker CLI response field {name} has the wrong type.");
        }

        int result;
        try
        {
            result = token.ToObject<int>();
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            throw new ProtocolError($"Locker CLI response field {name} is outside the integer range.");
        }

        if (exact is not null && result != exact.Value)
        {
            throw new ProtocolError($"Locker CLI response field {name} has an unsupported value.");
        }

        return result;
    }
}
