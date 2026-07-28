using Locker;
using Xunit;

namespace LockerTests;

public sealed class ProtocolClientTests
{
    [Fact]
    public async Task SupportsAllTenVaultMethods()
    {
        var client = CreateClient();

        Assert.Equal("key", (await client.Secrets.GetAsync("key")).Key);
        Assert.Equal(2, (await client.Secrets.ListAsync()).Count);
        var secretPage = await client.Secrets.ListPageAsync(
            new SecretListPageOptions
            {
                EnvironmentName = "production",
                PageSize = 1,
            });
        Assert.Equal("secret_page", secretPage.Object);
        Assert.Single(secretPage.Items);
        Assert.Equal("secret-next", secretPage.NextCursor);
        var finalSecretPage = await client.Secrets.ListPageAsync(
            new SecretListPageOptions
            {
                EnvironmentName = "production",
                PageSize = 1,
                Cursor = secretPage.NextCursor,
            });
        Assert.Null(finalSecretPage.NextCursor);
        Assert.Equal(
            "created",
            (await client.Secrets.CreateAsync(
                new SecretCreateOptions { Key = "created", Value = string.Empty })).Key);
        Assert.Equal(
            "updated",
            (await client.Secrets.UpdateAsync(
                "updated",
                new SecretUpdateOptions { Description = string.Empty })).Key);

        Assert.Equal("production", (await client.Environments.GetAsync("production")).Name);
        Assert.Single(await client.Environments.ListAsync());
        var environmentPage = await client.Environments.ListPageAsync(
            new EnvironmentListPageOptions { PageSize = 1 });
        Assert.Equal("environment_page", environmentPage.Object);
        Assert.Single(environmentPage.Items);
        Assert.Null(environmentPage.NextCursor);
        Assert.Equal(
            "created",
            (await client.Environments.CreateAsync(
                new EnvironmentCreateOptions { Name = "created" })).Name);
        Assert.Equal(
            "production",
            (await client.Environments.UpdateAsync(
                "production",
                new EnvironmentUpdateOptions { Description = string.Empty })).Name);
    }

    [Theory]
    [InlineData("malformed-response")]
    [InlineData("trailing-response")]
    [InlineData("duplicate-response")]
    [InlineData("unpaired-surrogate-response")]
    [InlineData("wrong-id")]
    [InlineData("both-result-error")]
    [InlineData("wrong-data-type")]
    public async Task RejectsInvalidProtocolResponses(string key)
    {
        var error = await Assert.ThrowsAsync<ProtocolError>(
            () => CreateClient().Secrets.GetAsync(key));
        Assert.DoesNotContain("secret-value", error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("test-access", error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("test-secret", error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("\"jsonrpc\"", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task BindsResponsesToNegotiatedCliVersion()
    {
        var error = await Assert.ThrowsAsync<ProtocolError>(
            () => CreateClient().Secrets.GetAsync("cli-version-mismatch"));
        Assert.Contains(
            "differs from negotiated",
            error.Message,
            StringComparison.Ordinal);

        await WithFixtureModeAsync(
            "capability-version-mismatch",
            async () =>
            {
                await Assert.ThrowsAsync<ProtocolError>(
                    () => CreateClient().Secrets.GetAsync("key"));
            });
    }

    [Fact]
    public async Task BinaryIdentityChangeInvalidatesCapabilities()
    {
        var originalWriteTime = File.GetLastWriteTimeUtc(FakeCliPath);
        var modeDirectory = Path.Combine(
            Path.GetTempPath(),
            "locker-test-count-capabilities");
        var countPath = Path.Combine(modeDirectory, "capabilities.count");
        Directory.CreateDirectory(modeDirectory);
        if (File.Exists(countPath))
        {
            File.Delete(countPath);
        }
        var originalTmp = System.Environment.GetEnvironmentVariable("TMP");
        try
        {
            System.Environment.SetEnvironmentVariable("TMP", modeDirectory);
            using var client = CreateClient();
            await client.Secrets.GetAsync("first");
            File.SetLastWriteTimeUtc(
                FakeCliPath,
                originalWriteTime.AddSeconds(2));
            await client.Secrets.GetAsync("second");

            Assert.Equal(2, File.ReadAllLines(countPath).Length);
        }
        finally
        {
            File.SetLastWriteTimeUtc(FakeCliPath, originalWriteTime);
            System.Environment.SetEnvironmentVariable("TMP", originalTmp);
            if (File.Exists(countPath))
            {
                File.Delete(countPath);
            }

            Directory.Delete(modeDirectory, recursive: false);
        }
    }

    [Fact]
    public async Task MapsErrorsByNumericCodeAndDefaultsOnlyNotFound()
    {
        var client = CreateClient();
        var missing = await Assert.ThrowsAsync<ResourceNotFoundError>(
            () => client.Secrets.GetAsync("missing"));
        Assert.Equal(-32004, missing.Code);
        Assert.Equal("not_found_error", missing.Kind);
        Assert.Equal("fallback", client.Secrets.GetOrDefault("missing", "fallback"));

        var authentication = await Assert.ThrowsAsync<AuthenticationError>(
            () => client.Secrets.GetAsync("auth-error"));
        Assert.Equal(-32001, authentication.Code);
        Assert.Throws<AuthenticationError>(
            () => client.Secrets.GetOrDefault("auth-error", "unsafe-default"));

        var operation = await Assert.ThrowsAsync<APIError>(
            () => client.Secrets.GetAsync("unsafe-error"));
        Assert.Equal("Locker operation failed.", operation.Message);
        Assert.DoesNotContain("secret-value-from-cli", operation.ToString(), StringComparison.Ordinal);

        var tooLarge = await Assert.ThrowsAsync<APIError>(
            () => client.Secrets.GetAsync("response-too-large-error"));
        Assert.Equal(-32000, tooLarge.Code);
        Assert.Equal("response_too_large", tooLarge.Kind);
        Assert.False(tooLarge.Retryable);
    }

    [Fact]
    public async Task RejectsInvalidPageOptionsBeforeStartingCli()
    {
        var client = CreateClient();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => client.Secrets.ListPageAsync(
                new SecretListPageOptions { PageSize = 0 }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => client.Environments.ListPageAsync(
                new EnvironmentListPageOptions { Cursor = string.Empty }));
    }

    [Fact]
    public async Task NegotiatesWithoutAdditivePageMethodsAndFailsPageCallsLocally()
    {
        await WithFixtureModeAsync(
            "no-page-methods",
            async () =>
            {
                using var client = CreateClient();

                Assert.Equal("key", (await client.Secrets.GetAsync("key")).Key);
                await Assert.ThrowsAsync<ProtocolError>(
                    () => client.Secrets.ListPageAsync(
                        new SecretListPageOptions
                        {
                            EnvironmentName = "production",
                            PageSize = 1,
                        }));
                await Assert.ThrowsAsync<ProtocolError>(
                    () => client.Environments.ListPageAsync(
                        new EnvironmentListPageOptions { PageSize = 1 }));
            });
    }

    [Fact]
    public async Task RejectsCapabilitiesMissingSystemMethod()
    {
        await WithFixtureModeAsync(
            "missing-system-method",
            async () =>
            {
                using var client = CreateClient();
                await Assert.ThrowsAsync<ProtocolError>(
                    () => client.Secrets.GetAsync("key"));
            });
    }

    [Fact]
    public void RequiresResponseLimitAndValidCapabilityMethods()
    {
        var valid = new Newtonsoft.Json.Linq.JObject
        {
            ["protocol"] = new Newtonsoft.Json.Linq.JObject
            {
                ["name"] = "locker.sdk",
                ["min_version"] = 1,
                ["max_version"] = 1,
                ["transport"] = "json-rpc-2.0-stdio",
            },
            ["cli"] = new Newtonsoft.Json.Linq.JObject { ["version"] = "test" },
            ["methods"] = new Newtonsoft.Json.Linq.JArray(
                "system.capabilities",
                "secret.get",
                "secret.list",
                "secret.create",
                "secret.update",
                "environment.get",
                "environment.list",
                "environment.create",
                "environment.update"),
            ["limits"] = new Newtonsoft.Json.Linq.JObject
            {
                ["max_request_bytes"] = 20 * 1024 * 1024,
                ["max_response_bytes"] = 20 * 1024 * 1024,
                ["max_json_depth"] = 256,
            },
        };

        var parsed = ProtocolDataParser.ParseCapabilities(valid);
        Assert.Contains("system.capabilities", parsed.Methods);
        Assert.Equal(20 * 1024 * 1024, parsed.MaxResponseBytes);
        LockerClient.ValidateRequiredMethods(parsed.Methods);

        var missingBaseMethod = new HashSet<string>(
            parsed.Methods,
            StringComparer.Ordinal);
        missingBaseMethod.Remove("secret.get");
        Assert.Throws<ProtocolError>(
            () => LockerClient.ValidateRequiredMethods(missingBaseMethod));

        var missingSystemMethod = new HashSet<string>(
            parsed.Methods,
            StringComparer.Ordinal);
        missingSystemMethod.Remove("system.capabilities");
        Assert.Throws<ProtocolError>(
            () => LockerClient.ValidateRequiredMethods(missingSystemMethod));

        valid["limits"]!["max_request_bytes"] = long.MaxValue;
        valid["limits"]!["max_response_bytes"] = long.MaxValue;
        parsed = ProtocolDataParser.ParseCapabilities(valid);
        Assert.Equal(LockerClientOptions.ProtocolRequestLimitBytes, parsed.MaxRequestBytes);
        Assert.Equal(LockerClientOptions.ProtocolResponseLimitBytes, parsed.MaxResponseBytes);

        ((Newtonsoft.Json.Linq.JObject)valid["limits"]!).Remove("max_response_bytes");
        Assert.Throws<ProtocolError>(() => ProtocolDataParser.ParseCapabilities(valid));
    }

    [Fact]
    public void RejectsMalformedOrOversizedPageData()
    {
        var missingCursor = new Newtonsoft.Json.Linq.JObject
        {
            ["object"] = "secret_page",
            ["items"] = new Newtonsoft.Json.Linq.JArray(),
        };
        Assert.Throws<ProtocolError>(
            () => ProtocolDataParser.ParseSecretPage(missingCursor));

        var oversized = new Newtonsoft.Json.Linq.JObject
        {
            ["object"] = "environment_page",
            ["items"] = new Newtonsoft.Json.Linq.JArray(
                Enumerable.Range(0, 1001)
                    .Select(_ => Newtonsoft.Json.Linq.JValue.CreateNull())),
            ["next_cursor"] = null,
        };
        Assert.Throws<ProtocolError>(
            () => ProtocolDataParser.ParseEnvironmentPage(oversized));

        var invalidCursor = new Newtonsoft.Json.Linq.JObject
        {
            ["object"] = "secret_page",
            ["items"] = new Newtonsoft.Json.Linq.JArray(),
            ["next_cursor"] = string.Empty,
        };
        Assert.Throws<ProtocolError>(
            () => ProtocolDataParser.ParseSecretPage(invalidCursor));
    }

    [Fact]
    public async Task StripsCredentialEnvironmentFromChild()
    {
        var names = new[]
        {
            "LOCKER_ACCESS_KEY_ID",
            "LOCKER_SECRET_ACCESS_KEY",
            "ACCESS_KEY_ID",
            "SECRET_ACCESS_KEY",
        };
        var originals = names.ToDictionary(
            name => name,
            System.Environment.GetEnvironmentVariable,
            StringComparer.Ordinal);
        try
        {
            foreach (var name in names)
            {
                System.Environment.SetEnvironmentVariable(name, "must-not-be-inherited");
            }

            var secret = await CreateClient().Secrets.GetAsync("environment-leak");
            Assert.Equal("secret-value", secret.Value);
        }
        finally
        {
            foreach (var pair in originals)
            {
                System.Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }
        }
    }

    [Fact]
    public async Task PreservesProxyEnvironmentForChild()
    {
        var names = new[]
        {
            "HTTP_PROXY",
            "HTTPS_PROXY",
            "NO_PROXY",
            "ALL_PROXY",
            "http_proxy",
            "https_proxy",
            "no_proxy",
            "all_proxy",
        };
        foreach (var name in names)
        {
            var original = System.Environment.GetEnvironmentVariable(name);
            try
            {
                System.Environment.SetEnvironmentVariable(
                    name,
                    $"proxy-sentinel-{name}");
                var secret = await CreateClient()
                    .Secrets.GetAsync($"environment-pass:{name}");
                Assert.Equal("secret-value", secret.Value);
            }
            finally
            {
                System.Environment.SetEnvironmentVariable(name, original);
            }
        }
    }

    [Fact]
    public async Task EnforcesTimeoutAndStdoutLimit()
    {
        var timeoutClient = CreateClient(timeout: TimeSpan.FromMilliseconds(250));
        await Assert.ThrowsAsync<LockerTimeoutError>(
            () => timeoutClient.Secrets.GetAsync("sleep"));

        var boundedClient = CreateClient(maxStdoutBytes: 1024);
        await Assert.ThrowsAsync<LockerResponseTooLargeError>(
            () => boundedClient.Secrets.GetAsync("huge-response"));

        var options = CreateOptions(FakeCliPath);
        var transport = new ProtocolTransport(options);
        await Assert.ThrowsAsync<LockerResponseTooLargeError>(
            () => transport.CallAsync(
                "secret.get",
                new Newtonsoft.Json.Linq.JObject
                {
                    ["context"] = transport.CreateContext(),
                    ["key"] = "response-cap",
                },
                CancellationToken.None,
                LockerClientOptions.ProtocolRequestLimitBytes,
                256,
                LockerClientOptions.ProtocolJsonDepthLimit));

        var stderrClient = new LockerClient(
            new LockerClientOptions(
                "test-access",
                "test-secret",
                FakeCliPath,
                maxStderrBytes: 1024));
        await Assert.ThrowsAsync<LockerResponseTooLargeError>(
            () => stderrClient.Secrets.GetAsync("huge-stderr"));
    }

    [Fact]
    public async Task CancellationTerminatesExchangePromptly()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        var started = System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateClient().Secrets.GetAsync("sleep", cancellationToken: cancellation.Token));
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(3), started.Elapsed.ToString());
    }

    [Fact]
    public async Task TimeoutTerminatesProcessTree()
    {
        var pidPath = Path.Combine(
            Path.GetTempPath(),
            $"locker-dotnet-child-{Guid.NewGuid():N}.pid");
        try
        {
            await Assert.ThrowsAsync<LockerTimeoutError>(
                () => CreateClient(timeout: TimeSpan.FromMilliseconds(500))
                    .Secrets.GetAsync($"tree-sleep:{pidPath}"));
            Assert.True(File.Exists(pidPath));
            var pid = int.Parse(
                await File.ReadAllTextAsync(pidPath),
                System.Globalization.CultureInfo.InvariantCulture);
            System.Diagnostics.Process? child = null;
            try
            {
                child = System.Diagnostics.Process.GetProcessById(pid);
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch (ArgumentException)
            {
                // The child already exited and no process owns the PID.
            }
            finally
            {
                if (child is { HasExited: false })
                {
                    child.Kill(entireProcessTree: true);
                }

                child?.Dispose();
            }
        }
        finally
        {
            if (File.Exists(pidPath))
            {
                File.Delete(pidPath);
            }
        }
    }

    [Fact]
    public async Task TimeoutTerminatesDescendantAfterDirectParentExits()
    {
        var pidPath = Path.Combine(
            Path.GetTempPath(),
            $"locker-dotnet-orphan-{Guid.NewGuid():N}.pid");
        try
        {
            await Assert.ThrowsAsync<LockerTimeoutError>(
                () => CreateClient(timeout: TimeSpan.FromMilliseconds(500))
                    .Secrets.GetAsync($"parent-exit-tree:{pidPath}"));
            Assert.True(File.Exists(pidPath));
            var pid = int.Parse(
                await File.ReadAllTextAsync(pidPath),
                System.Globalization.CultureInfo.InvariantCulture);
            await AssertProcessExitedAsync(pid);
        }
        finally
        {
            if (File.Exists(pidPath))
            {
                File.Delete(pidPath);
            }
        }
    }

    [Fact]
    public async Task RejectsOversizedRequestBeforeStartingCli()
    {
        var oversized = new string('x', LockerClientOptions.ProtocolRequestLimitBytes);
        await Assert.ThrowsAsync<ProtocolError>(
            () => CreateClient().Secrets.GetAsync(oversized));
    }

    [Fact]
    public void ScannerHelperIsFailClosed()
    {
        var client = CreateClient();
        Assert.Equal("secret-value", client.Secrets.GetRequired("key"));
        Assert.Throws<ResourceNotFoundError>(() => client.Secrets.GetRequired("missing"));
    }

    [Fact]
    public async Task ExplicitPathPrecedesEnvironmentPath()
    {
        var original = System.Environment.GetEnvironmentVariable("LOCKER_CLI_PATH");
        try
        {
            System.Environment.SetEnvironmentVariable("LOCKER_CLI_PATH", "does-not-exist");
            var resolved = await LockerCliResolver.ResolveAsync(
                CreateOptions(FakeCliPath),
                CancellationToken.None);
            Assert.Equal(FakeCliPath, resolved);
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("LOCKER_CLI_PATH", original);
        }
    }

    private static LockerClient CreateClient(
        TimeSpan? timeout = null,
        int maxStdoutBytes = LockerClientOptions.ProtocolRequestLimitBytes) =>
        new(CreateOptions(FakeCliPath, timeout, maxStdoutBytes));

    private static LockerClientOptions CreateOptions(
        string cliPath,
        TimeSpan? timeout = null,
        int maxStdoutBytes = LockerClientOptions.ProtocolRequestLimitBytes) =>
        new(
            "test-access",
            "test-secret",
            cliPath,
            timeout: timeout,
            maxStdoutBytes: maxStdoutBytes);

    private static async Task AssertProcessExitedAsync(int processId)
    {
        System.Diagnostics.Process? process = null;
        try
        {
            process = System.Diagnostics.Process.GetProcessById(processId);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch (ArgumentException)
        {
            // The process already exited and no process owns the PID.
        }
        finally
        {
            if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
            }

            process?.Dispose();
        }
    }

    private static async Task WithFixtureModeAsync(
        string mode,
        Func<Task> action)
    {
        var original = System.Environment.GetEnvironmentVariable("TMP");
        var modeDirectory = Path.Combine(
            Path.GetTempPath(),
            $"locker-test-{mode}");
        Directory.CreateDirectory(modeDirectory);
        try
        {
            System.Environment.SetEnvironmentVariable("TMP", modeDirectory);
            await action();
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("TMP", original);
            Directory.Delete(modeDirectory, recursive: false);
        }
    }

    private static string FakeCliPath
    {
        get
        {
            var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name
                ?? throw new InvalidOperationException("Test build configuration is unavailable.");
            var extension = OperatingSystem.IsWindows() ? ".exe" : string.Empty;
            return Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "LockerTestCli",
                "bin",
                configuration,
                "net8.0",
                $"LockerTestCli{extension}"));
        }
    }
}
