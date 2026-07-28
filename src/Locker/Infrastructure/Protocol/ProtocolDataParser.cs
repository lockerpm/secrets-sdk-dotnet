using Newtonsoft.Json.Linq;

namespace Locker;

internal sealed record ParsedCapabilities(
    string CliVersion,
    IReadOnlySet<string> Methods,
    int MaxRequestBytes,
    int MaxResponseBytes,
    int MaxJsonDepth);

internal static class ProtocolDataParser
{
    internal static ParsedCapabilities ParseCapabilities(JToken data)
    {
        var root = Object(data, "capabilities");
        var protocol = Object(Required(root, "protocol"), "capabilities.protocol");
        String(protocol, "name", "locker.sdk");
        var minimumVersion = Integer(protocol, "min_version", minimum: 1);
        var maximumVersion = Integer(protocol, "max_version", minimum: 1);
        if (minimumVersion > maximumVersion || minimumVersion > 1 || maximumVersion < 1)
        {
            throw new ProtocolError("Locker CLI does not advertise protocol v1.");
        }

        String(protocol, "transport", "json-rpc-2.0-stdio");

        var cli = Object(Required(root, "cli"), "capabilities.cli");
        var cliVersion = String(cli, "version");
        if (string.IsNullOrWhiteSpace(cliVersion))
        {
            throw new ProtocolError("Locker CLI capabilities version is empty.");
        }

        var limits = Object(Required(root, "limits"), "capabilities.limits");
        var maxRequest = Integer(limits, "max_request_bytes", minimum: 1);
        var maxResponse = Integer(limits, "max_response_bytes", minimum: 1);
        var maxJsonDepth = limits.TryGetValue(
            "max_json_depth",
            StringComparison.Ordinal,
            out var depthToken)
            ? IntegerToken(depthToken, "max_json_depth", minimum: 1)
            : LockerClientOptions.ProtocolJsonDepthLimit;
        var methodsToken = Required(root, "methods");
        if (methodsToken is not JArray methodsArray)
        {
            throw new ProtocolError("Locker CLI capabilities methods field has the wrong type.");
        }

        var methods = new HashSet<string>(StringComparer.Ordinal);
        foreach (var methodToken in methodsArray)
        {
            var method = methodToken.Type == JTokenType.String
                ? (string?)methodToken
                : null;
            if (string.IsNullOrWhiteSpace(method) || !methods.Add(method))
            {
                throw new ProtocolError("Locker CLI capabilities contains an invalid or duplicate method.");
            }
        }

        return new ParsedCapabilities(
            cliVersion,
            methods,
            checked((int)Math.Min(maxRequest, LockerClientOptions.ProtocolRequestLimitBytes)),
            checked((int)Math.Min(maxResponse, LockerClientOptions.ProtocolResponseLimitBytes)),
            checked((int)Math.Min(maxJsonDepth, LockerClientOptions.ProtocolJsonDepthLimit)));
    }

    internal static Secret ParseSecret(JToken data)
    {
        ValidateSecret(Object(data, "secret"));
        return data.ToObject<Secret>()
            ?? throw new ProtocolError("Locker CLI returned an empty secret.");
    }

    internal static IReadOnlyList<Secret> ParseSecrets(JToken data)
    {
        if (data is not JArray array)
        {
            throw new ProtocolError("Locker CLI secret list data has the wrong type.");
        }

        foreach (var item in array)
        {
            ValidateSecret(Object(item, "secret"));
        }

        return array.ToObject<List<Secret>>()
            ?? throw new ProtocolError("Locker CLI returned an invalid secret list.");
    }

    internal static SecretPage ParseSecretPage(JToken data)
    {
        var root = Object(data, "secret page");
        String(root, "object", "secret_page");
        var items = PageItems(root, "secret page");
        var secrets = new List<Secret>(items.Count);
        foreach (var item in items)
        {
            secrets.Add(ParseSecret(item));
        }

        return new SecretPage(secrets, PageCursor(root));
    }

    internal static Environment ParseEnvironment(JToken data)
    {
        ValidateEnvironment(Object(data, "environment"));
        return data.ToObject<Environment>()
            ?? throw new ProtocolError("Locker CLI returned an empty environment.");
    }

    internal static IReadOnlyList<Environment> ParseEnvironments(JToken data)
    {
        if (data is not JArray array)
        {
            throw new ProtocolError("Locker CLI environment list data has the wrong type.");
        }

        foreach (var item in array)
        {
            ValidateEnvironment(Object(item, "environment"));
        }

        return array.ToObject<List<Environment>>()
            ?? throw new ProtocolError("Locker CLI returned an invalid environment list.");
    }

    internal static EnvironmentPage ParseEnvironmentPage(JToken data)
    {
        var root = Object(data, "environment page");
        String(root, "object", "environment_page");
        var items = PageItems(root, "environment page");
        var environments = new List<Environment>(items.Count);
        foreach (var item in items)
        {
            environments.Add(ParseEnvironment(item));
        }

        return new EnvironmentPage(environments, PageCursor(root));
    }

    private static JArray PageItems(JObject page, string name)
    {
        if (Required(page, "items") is not JArray items || items.Count > 1000)
        {
            throw new ProtocolError($"Locker CLI {name} items field has the wrong type or size.");
        }

        return items;
    }

    private static string? PageCursor(JObject page)
    {
        var cursor = Required(page, "next_cursor");
        if (cursor.Type == JTokenType.Null)
        {
            return null;
        }

        if (cursor.Type != JTokenType.String)
        {
            throw new ProtocolError("Locker CLI page cursor has the wrong type.");
        }

        var value = (string?)cursor;
        if (string.IsNullOrEmpty(value) || value.Length > 4096)
        {
            throw new ProtocolError("Locker CLI page cursor has an unsupported value.");
        }

        return value;
    }

    private static void ValidateSecret(JObject value)
    {
        Forbidden(value, "secret_hash");
        Forbidden(value, "environment_hash");
        String(value, "object", "secret");
        String(value, "id");
        Number(value, "creation_date");
        Number(value, "revision_date");
        NullableNumber(value, "updated_date");
        NullableNumber(value, "deleted_date");
        NullableNumber(value, "last_use_date");
        Integer(value, "project_id");
        NullableString(value, "environment_id");
        NullableString(value, "environment_name");
        String(value, "key");
        String(value, "value");
        String(value, "description");
    }

    private static void ValidateEnvironment(JObject value)
    {
        Forbidden(value, "environment_hash");
        String(value, "object", "environment");
        String(value, "id");
        String(value, "name");
        String(value, "external_url");
        String(value, "description");
        Number(value, "creation_date");
        Number(value, "revision_date");
        NullableNumber(value, "updated_date");
        Integer(value, "project_id");
    }

    private static JObject Object(JToken token, string name) =>
        token as JObject
        ?? throw new ProtocolError($"Locker CLI {name} data has the wrong type.");

    private static JToken Required(JObject value, string name)
    {
        if (!value.TryGetValue(name, StringComparison.Ordinal, out var result) || result is null)
        {
            throw new ProtocolError($"Locker CLI data is missing required field {name}.");
        }

        return result;
    }

    private static void Forbidden(JObject value, string name)
    {
        if (value.TryGetValue(name, StringComparison.Ordinal, out _))
        {
            throw new ProtocolError($"Locker CLI data contains forbidden field {name}.");
        }
    }

    private static string String(JObject value, string name, string? exact = null)
    {
        var token = Required(value, name);
        if (token.Type != JTokenType.String)
        {
            throw new ProtocolError($"Locker CLI data field {name} has the wrong type.");
        }

        var result = (string?)token
            ?? throw new ProtocolError($"Locker CLI data field {name} cannot be null.");
        if (exact is not null && !string.Equals(result, exact, StringComparison.Ordinal))
        {
            throw new ProtocolError($"Locker CLI data field {name} has an unsupported value.");
        }

        return result;
    }

    private static void NullableString(JObject value, string name)
    {
        var token = Required(value, name);
        if (token.Type is not (JTokenType.String or JTokenType.Null))
        {
            throw new ProtocolError($"Locker CLI data field {name} has the wrong type.");
        }
    }

    private static long Integer(
        JObject value,
        string name,
        long? minimum = null,
        long? maximum = null)
    {
        return IntegerToken(Required(value, name), name, minimum, maximum);
    }

    private static long IntegerToken(
        JToken token,
        string name,
        long? minimum = null,
        long? maximum = null)
    {
        if (token.Type != JTokenType.Integer)
        {
            throw new ProtocolError($"Locker CLI data field {name} has the wrong type.");
        }

        long result;
        try
        {
            result = token.ToObject<long>();
        }
        catch (Exception ex) when (ex is OverflowException or FormatException)
        {
            throw new ProtocolError($"Locker CLI data field {name} is outside the integer range.");
        }

        if ((minimum is not null && result < minimum) || (maximum is not null && result > maximum))
        {
            throw new ProtocolError($"Locker CLI data field {name} has an unsupported value.");
        }

        return result;
    }

    private static void Number(JObject value, string name)
    {
        var token = Required(value, name);
        if (token.Type is not (JTokenType.Integer or JTokenType.Float))
        {
            throw new ProtocolError($"Locker CLI data field {name} has the wrong type.");
        }
    }

    private static void NullableNumber(JObject value, string name)
    {
        var token = Required(value, name);
        if (token.Type is not (JTokenType.Integer or JTokenType.Float or JTokenType.Null))
        {
            throw new ProtocolError($"Locker CLI data field {name} has the wrong type.");
        }
    }
}
