using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

if (args.Length == 1 && args[0] == "child-sleeper")
{
    using var termination = !OperatingSystem.IsWindows()
        ? System.Runtime.InteropServices.PosixSignalRegistration.Create(
            System.Runtime.InteropServices.PosixSignal.SIGTERM,
            context => context.Cancel = true)
        : null;
    await Task.Delay(TimeSpan.FromSeconds(30));
    return 0;
}

if (args.Length != 1 || args[0] != "sdk")
{
    return 90;
}

var requestText = await Console.In.ReadToEndAsync();
if (System.Text.Encoding.UTF8.GetByteCount(requestText) > 20 * 1024 * 1024)
{
    return 91;
}

var request = JObject.Parse(requestText);
var id = request["id"]!;
var method = request["method"]?.Value<string>() ?? string.Empty;
var parameters = (JObject?)request["params"] ?? new JObject();

if (method == "system.capabilities")
{
    var temporaryDirectory =
        System.Environment.GetEnvironmentVariable("TMP") ?? string.Empty;
    var fixtureMode = Path.GetFileName(
        temporaryDirectory
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    if (string.Equals(
        fixtureMode,
        "locker-test-count-capabilities",
        StringComparison.Ordinal))
    {
        await File.AppendAllTextAsync(
            Path.Combine(temporaryDirectory, "capabilities.count"),
            "1\n");
    }
    if (string.Equals(
        fixtureMode,
        "locker-test-slow-capabilities",
        StringComparison.Ordinal))
    {
        await Task.Delay(TimeSpan.FromMilliseconds(250));
    }
    var methods = new JArray(
        "environment.create",
        "environment.get",
        "environment.list",
        "environment.update",
        "secret.create",
        "secret.get",
        "secret.list",
        "secret.update");
    if (!string.Equals(
            fixtureMode,
            "locker-test-no-page-methods",
            StringComparison.Ordinal))
    {
        methods.Add("environment.list_page");
        methods.Add("secret.list_page");
    }

    if (!string.Equals(
            fixtureMode,
            "locker-test-missing-system-method",
            StringComparison.Ordinal))
    {
        methods.Add("system.capabilities");
    }

    WriteSuccess(
        id,
        new JObject
        {
            ["protocol"] = new JObject
            {
                ["name"] = "locker.sdk",
                ["min_version"] = 1,
                ["max_version"] = 1,
                ["transport"] = "json-rpc-2.0-stdio",
            },
            ["cli"] = new JObject { ["version"] = "test" },
            ["methods"] = methods,
            ["limits"] = new JObject
            {
                ["max_request_bytes"] = 20 * 1024 * 1024,
                ["max_response_bytes"] = 20 * 1024 * 1024,
                ["max_json_depth"] = 256,
            },
        },
        fixtureMode == "locker-test-capability-version-mismatch"
            ? "different-cli"
            : "test");
    return 0;
}

var context = (JObject?)parameters["context"];
var credentials = (JObject?)context?["credentials"];
var client = (JObject?)context?["client"];
if (context?["protocol_version"]?.Value<int>() != 1
    || credentials?["access_key_id"]?.Value<string>() != "test-access"
    || credentials?["secret_access_key"]?.Value<string>() != "test-secret"
    || client?["name"]?.Value<string>() != "locker-dotnet"
    || client?["version"]?.Value<string>() != "1.0.0")
{
    WriteError(id, -32602, "invalid_params", "Invalid method parameters");
    return 0;
}

var key = parameters["key"]?.Value<string>();
switch (key)
{
    case "missing":
        WriteError(id, -32004, "not_found_error", "Locker resource was not found");
        return 0;
    case "auth-error":
        WriteError(id, -32001, "authentication_error", "Locker authentication failed");
        return 0;
    case "unsafe-error":
        WriteError(id, -32000, "operation_error", "secret-value-from-cli");
        return 0;
    case "response-too-large-error":
        WriteError(id, -32000, "response_too_large", "response too large");
        return 0;
    case "malformed-response":
        Console.Write("{");
        return 0;
    case "trailing-response":
        WriteSuccess(id, Secret("trailing-response"));
        Console.Write("{}");
        return 0;
    case "duplicate-response":
        Console.Write(
            $"{{\"jsonrpc\":\"2.0\",\"jsonrpc\":\"2.0\",\"id\":{id.ToString(Formatting.None)},\"result\":{{\"protocol_version\":1,\"data\":{Secret(key).ToString(Formatting.None)},\"meta\":{{\"cli_version\":\"test\"}}}}}}");
        return 0;
    case "unpaired-surrogate-response":
        Console.Write(
            $"{{\"jsonrpc\":\"2.0\",\"id\":{id.ToString(Formatting.None)},\"result\":{{\"protocol_version\":1,\"data\":{Secret(key).ToString(Formatting.None).Replace("secret-value", "\\uD800", StringComparison.Ordinal)},\"meta\":{{\"cli_version\":\"test\"}}}}}}");
        return 0;
    case "wrong-id":
        WriteSuccess(new JValue("wrong"), Secret(key));
        return 0;
    case "both-result-error":
        Console.Write(new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["result"] = new JObject(),
            ["error"] = new JObject(),
        }.ToString(Formatting.None));
        return 0;
    case "huge-response":
        Console.Write(new string('x', 1024 * 1024));
        return 0;
    case "huge-stderr":
        Console.Error.Write(new string('x', 1024 * 1024));
        WriteSuccess(id, Secret(key));
        return 0;
    case "wrong-data-type":
        WriteSuccess(id, new JValue("not-an-object"));
        return 0;
    case "cli-version-mismatch":
        WriteSuccess(id, Secret(key), "different-cli");
        return 0;
    case "sleep":
        await Task.Delay(TimeSpan.FromSeconds(30));
        return 0;
    case "short-sleep":
        await Task.Delay(TimeSpan.FromMilliseconds(250));
        break;
    case "environment-leak":
        if (System.Environment.GetEnvironmentVariable("LOCKER_ACCESS_KEY_ID") is not null
            || System.Environment.GetEnvironmentVariable("LOCKER_SECRET_ACCESS_KEY") is not null
            || System.Environment.GetEnvironmentVariable("ACCESS_KEY_ID") is not null
            || System.Environment.GetEnvironmentVariable("SECRET_ACCESS_KEY") is not null)
        {
            WriteError(id, -32603, "environment_leak", "Unsafe inherited environment");
            return 0;
        }

        break;
    case string environmentKey when environmentKey.StartsWith(
        "environment-pass:",
        StringComparison.Ordinal):
        var environmentName = environmentKey["environment-pass:".Length..];
        if (System.Environment.GetEnvironmentVariable(environmentName)
            != $"proxy-sentinel-{environmentName}")
        {
            WriteError(id, -32603, "environment_missing", "Required proxy environment missing");
            return 0;
        }

        break;
}

if (key?.StartsWith("tree-sleep:", StringComparison.Ordinal) == true
    || key?.StartsWith("parent-exit-tree:", StringComparison.Ordinal) == true)
{
    var parentExits = key.StartsWith("parent-exit-tree:", StringComparison.Ordinal);
    var prefix = parentExits ? "parent-exit-tree:" : "tree-sleep:";
    var pidPath = key[prefix.Length..];
    var childStart = new System.Diagnostics.ProcessStartInfo
    {
        FileName = System.Environment.ProcessPath
            ?? throw new InvalidOperationException("Current process path is unavailable."),
        UseShellExecute = false,
    };
    childStart.ArgumentList.Add("child-sleeper");
    using var child = System.Diagnostics.Process.Start(childStart)
        ?? throw new InvalidOperationException("Unable to start child sleeper.");
    await File.WriteAllTextAsync(pidPath, child.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
    if (!parentExits)
    {
        await Task.Delay(TimeSpan.FromSeconds(30));
    }

    return 0;
}

switch (method)
{
    case "secret.get":
    case "secret.create":
    case "secret.update":
        WriteSuccess(id, Secret(key ?? "created"));
        break;
    case "secret.list":
        WriteSuccess(id, new JArray(Secret("one"), Secret("two")));
        break;
    case "secret.list_page":
        if (parameters["page_size"]?.Value<int>() != 1
            || parameters["environment"]?.Value<string>() != "production")
        {
            WriteError(id, -32602, "invalid_params", "Invalid method parameters");
            break;
        }

        WriteSuccess(id, new JObject
        {
            ["object"] = "secret_page",
            ["items"] = new JArray(Secret("one")),
            ["next_cursor"] = parameters["cursor"] is null ? "secret-next" : null,
        });
        break;
    case "environment.get":
    case "environment.create":
    case "environment.update":
        WriteSuccess(id, Environment(parameters["name"]?.Value<string>() ?? "created"));
        break;
    case "environment.list":
        WriteSuccess(id, new JArray(Environment("production")));
        break;
    case "environment.list_page":
        if (parameters["page_size"]?.Value<int>() != 1)
        {
            WriteError(id, -32602, "invalid_params", "Invalid method parameters");
            break;
        }

        WriteSuccess(id, new JObject
        {
            ["object"] = "environment_page",
            ["items"] = new JArray(Environment("production")),
            ["next_cursor"] = null,
        });
        break;
    default:
        WriteError(id, -32601, "unsupported_method", "Unsupported method");
        break;
}

return 0;

static JObject Secret(string key) => new()
{
    ["object"] = "secret",
    ["id"] = "secret-id",
    ["creation_date"] = 1710000000,
    ["revision_date"] = 1710000001,
    ["updated_date"] = null,
    ["deleted_date"] = null,
    ["last_use_date"] = null,
    ["project_id"] = 42,
    ["environment_id"] = "environment-id",
    ["environment_name"] = "production",
    ["key"] = key,
    ["value"] = "secret-value",
    ["description"] = "",
};

static JObject Environment(string name) => new()
{
    ["object"] = "environment",
    ["id"] = "environment-id",
    ["name"] = name,
    ["external_url"] = "https://example.com",
    ["description"] = "",
    ["creation_date"] = 1710000000,
    ["revision_date"] = 1710000001,
    ["updated_date"] = null,
    ["project_id"] = 42,
};

static void WriteSuccess(
    JToken id,
    JToken data,
    string cliVersion = "test") =>
    Console.Write(new JObject
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id,
        ["result"] = new JObject
        {
            ["protocol_version"] = 1,
            ["data"] = data,
            ["meta"] = new JObject { ["cli_version"] = cliVersion },
        },
    }.ToString(Formatting.None));

static void WriteError(JToken id, int code, string kind, string message) =>
    Console.Write(new JObject
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id,
        ["error"] = new JObject
        {
            ["code"] = code,
            ["message"] = message,
            ["data"] = new JObject
            {
                ["protocol_version"] = 1,
                ["kind"] = kind,
                ["retryable"] = false,
            },
        },
    }.ToString(Formatting.None));
