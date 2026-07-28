using System.Globalization;
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
        RequireString(error, "message");
        var data = RequireObject(Require(error, "data"), "error.data");
        RequireInteger(
            data,
            "protocol_version",
            LockerSdkMetadata.ProtocolVersion);
        var kind = RequireString(data, "kind");
        if (string.IsNullOrWhiteSpace(kind) || kind.Length > 256)
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
        var safeMessage = SafeErrorMessage(code);
        return code switch
        {
            -32001 => new AuthenticationError(safeMessage, code, requestId, kind, retryable),
            -32003 => new PermissionDeniedError(safeMessage, code, requestId, kind, retryable),
            -32004 => new ResourceNotFoundError(safeMessage, code, requestId, kind, retryable),
            -32029 => new RateLimitError(safeMessage, code, requestId, kind, retryable),
            -32050 => new APIConnectionError(safeMessage, code, requestId, kind, retryable),
            -32051 => new APIServerError(safeMessage, code, requestId, kind, retryable),
            -32060 => new LocalStorageError(safeMessage, code, requestId, kind, retryable),
            -32000 => new APIError(safeMessage, code, requestId, kind, retryable),
            -32700 or -32600 or -32601 or -32602 or -32603 =>
                new ProtocolError(safeMessage, code, requestId, kind),
            _ => new APIError(safeMessage, code, requestId, kind, retryable),
        };
    }

    private static string SafeErrorMessage(int code) => code switch
    {
        -32700 => "Locker CLI could not parse the SDK protocol request.",
        -32600 => "Locker CLI rejected the SDK protocol request.",
        -32601 => "Locker CLI does not support the SDK operation.",
        -32602 => "Locker CLI rejected the SDK operation parameters.",
        -32603 => "Locker CLI protocol failed.",
        -32001 => "Locker authentication failed.",
        -32003 => "Locker permission was denied.",
        -32004 => "Locker resource was not found.",
        -32029 => "Locker request was rate limited.",
        -32050 => "Locker network request failed.",
        -32051 => "Locker server request failed.",
        -32060 => "Locker local storage operation failed.",
        _ => "Locker operation failed.",
    };

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
