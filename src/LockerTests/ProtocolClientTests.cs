using Locker;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Security.Cryptography;
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

    [Fact]
    public async Task BindsTypedErrorsOnlyWhenAdvertised()
    {
        foreach (var mode in new[]
        {
            "legacy-error-contract",
            "unknown-error-contract",
        })
        {
            await WithFixtureModeAsync(
                mode,
                async () =>
                {
                    using var client = CreateClient();
                    Assert.Equal(
                        "key",
                        (await client.Secrets.GetAsync("key")).Key);
                });
        }

        await WithFixtureModeAsync(
            "invalid-error-contract",
            async () =>
            {
                using var client = CreateClient();
                await Assert.ThrowsAsync<ProtocolError>(
                    () => client.Secrets.GetAsync("key"));
            });
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
    public async Task ManagedBinaryIsCryptographicallyReverifiedBeforeEveryExecution()
    {
        var fixtureDirectory = Path.Combine(
            Path.GetTempPath(),
            $"locker-dotnet-managed-exec-{Guid.NewGuid():N}");
        Directory.CreateDirectory(fixtureDirectory);
        var originalEnvironmentPath =
            System.Environment.GetEnvironmentVariable("LOCKER_CLI_PATH");
        try
        {
            System.Environment.SetEnvironmentVariable("LOCKER_CLI_PATH", null);
            var sourceDirectory = Path.GetDirectoryName(FakeCliPath)
                ?? throw new InvalidOperationException(
                    "Test CLI directory is unavailable.");
            foreach (var source in Directory.EnumerateFiles(
                sourceDirectory,
                "*"))
            {
                var destination = Path.Combine(
                    fixtureDirectory,
                    Path.GetFileName(source));
                File.Copy(source, destination);
                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(
                        destination,
                        File.GetUnixFileMode(source));
                }
            }

            var managedPath = Path.Combine(
                fixtureDirectory,
                Path.GetFileName(FakeCliPath));
            var expectedSize = new FileInfo(managedPath).Length;
            string expectedSha256;
            await using (var input = File.OpenRead(managedPath))
            {
                expectedSha256 = Convert.ToHexString(
                    await SHA256.HashDataAsync(input)).ToLowerInvariant();
            }
            var verificationCount = 0;
            async Task<string> VerifyManagedAsync(
                CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref verificationCount);
                if (!await LockerCliIntegrity.VerifyAsync(
                    managedPath,
                    expectedSize,
                    expectedSha256,
                    cancellationToken))
                {
                    throw new CliRunError(
                        "Managed Locker CLI failed signed cache verification.");
                }

                return managedPath;
            }

            var options = new LockerClientOptions(
                "test-access",
                "test-secret");
            using var transport = new ProtocolTransport(
                options,
                VerifyManagedAsync);
            await transport.CallAsync(
                "system.capabilities",
                new Newtonsoft.Json.Linq.JObject(),
                CancellationToken.None);

            var originalWriteTime = File.GetLastWriteTimeUtc(managedPath);
            await using (var binary = new FileStream(
                managedPath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.Read))
            {
                binary.Position = binary.Length - 1;
                var original = binary.ReadByte();
                Assert.NotEqual(-1, original);
                binary.Position = binary.Length - 1;
                binary.WriteByte((byte)(original ^ 0x01));
                await binary.FlushAsync();
            }
            File.SetLastWriteTimeUtc(managedPath, originalWriteTime);
            Assert.Equal(expectedSize, new FileInfo(managedPath).Length);
            Assert.Equal(originalWriteTime, File.GetLastWriteTimeUtc(managedPath));

            await Assert.ThrowsAsync<CliRunError>(
                () => transport.CallAsync(
                    "system.capabilities",
                    new Newtonsoft.Json.Linq.JObject(),
                    CancellationToken.None));
            Assert.Equal(2, verificationCount);
        }
        finally
        {
            System.Environment.SetEnvironmentVariable(
                "LOCKER_CLI_PATH",
                originalEnvironmentPath);
            Directory.Delete(fixtureDirectory, recursive: true);
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
        Assert.Equal("the Locker operation failed", operation.Message);
        Assert.DoesNotContain("secret-value-from-cli", operation.ToString(), StringComparison.Ordinal);

        var tooLarge = await Assert.ThrowsAsync<ResponseTooLargeError>(
            () => client.Secrets.GetAsync("response-too-large-error"));
        Assert.Equal(-32000, tooLarge.Code);
        Assert.Equal("response_too_large", tooLarge.Kind);
        Assert.False(tooLarge.Retryable);
    }

    [Fact]
    public void MapsStableAndLegacyOperationErrorTaxonomy()
    {
        var cases = new[]
        {
            (-32009, "secret_already_exists", typeof(AlreadyExistsError)),
            (-32009, "environment_already_exists", typeof(AlreadyExistsError)),
            (-32009, "conflict", typeof(ConflictError)),
            (-32022, "validation_error", typeof(ValidationError)),
            (-32070, "integrity_error", typeof(IntegrityError)),
            (-32051, "internal_error", typeof(APIServerError)),
            (-32000, "duplicate_hash", typeof(AlreadyExistsError)),
            (-32000, "conflict", typeof(ConflictError)),
            (-32000, "validation_error", typeof(ValidationError)),
            (-32000, "integrity_error", typeof(IntegrityError)),
            (-32000, "request_rejected", typeof(RequestRejectedError)),
            (-32000, "response_too_large", typeof(ResponseTooLargeError)),
            (-32000, "cancelled", typeof(OperationCancelledError)),
        };

        foreach (var (code, kind, expectedType) in cases)
        {
            var envelope = new Newtonsoft.Json.Linq.JObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = "request-taxonomy",
                ["error"] = new Newtonsoft.Json.Linq.JObject
                {
                    ["code"] = code,
                    ["message"] = "sensitive-value-from-broken-cli",
                    ["data"] = new Newtonsoft.Json.Linq.JObject
                    {
                        ["protocol_version"] = 1,
                        ["kind"] = kind,
                        ["retryable"] = true,
                    },
                },
            };

            var error = Assert.IsAssignableFrom<LockerError>(
                Record.Exception(
                    () => StrictProtocolResponse.Parse(
                        envelope.ToString(Newtonsoft.Json.Formatting.None),
                        "request-taxonomy")));
            Assert.Equal(expectedType, error.GetType());
            Assert.Equal(code, error.Code);
            Assert.Equal(kind, error.Kind);
            Assert.False(error.Retryable);
            Assert.DoesNotContain(
                "sensitive-value",
                error.Message,
                StringComparison.Ordinal);
            if (kind == "secret_already_exists")
            {
                Assert.Equal(
                    "a secret with this key already exists",
                    error.Message);
            }
            if (error is AlreadyExistsError)
            {
                Assert.IsAssignableFrom<ConflictError>(error);
            }
        }

        var genericEnvelope = new Newtonsoft.Json.Linq.JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = "request-taxonomy",
            ["error"] = new Newtonsoft.Json.Linq.JObject
            {
                ["code"] = -32000,
                ["message"] = "generic rejection",
                ["data"] = new Newtonsoft.Json.Linq.JObject
                {
                    ["protocol_version"] = 1,
                    ["kind"] = "request_rejected",
                    ["retryable"] = false,
                },
            },
        };
        var generic = Assert.IsAssignableFrom<LockerError>(
            Record.Exception(
                () => StrictProtocolResponse.Parse(
                    genericEnvelope.ToString(Newtonsoft.Json.Formatting.None),
                    "request-taxonomy")));
        Assert.IsType<RequestRejectedError>(generic);
        Assert.Equal("the request is invalid", generic.Message);

        var futureServerEnvelope = new Newtonsoft.Json.Linq.JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = "request-taxonomy",
            ["error"] = new Newtonsoft.Json.Linq.JObject
            {
                ["code"] = -32099,
                ["message"] = "safe future error",
                ["data"] = new Newtonsoft.Json.Linq.JObject
                {
                    ["protocol_version"] = 1,
                    ["kind"] = "future_error",
                    ["retryable"] = true,
                },
            },
        };
        var future = Assert.IsType<APIError>(
            Record.Exception(
                () => StrictProtocolResponse.Parse(
                    futureServerEnvelope.ToString(Newtonsoft.Json.Formatting.None),
                    "request-taxonomy")));
        Assert.Equal(-32099, future.Code);
        Assert.True(future.Retryable);

        futureServerEnvelope["error"]!["data"]!["kind"] =
            "secret_already_exists";
        var futureKnownKind = Assert.IsType<APIError>(
            Record.Exception(
                () => StrictProtocolResponse.Parse(
                    futureServerEnvelope.ToString(Newtonsoft.Json.Formatting.None),
                    "request-taxonomy")));
        Assert.Equal("the Locker operation failed", futureKnownKind.Message);
        Assert.True(futureKnownKind.Retryable);

        foreach (var (code, kind, message) in new[]
        {
            (-32100, "future_error", "safe error"),
            (-32000, "Invalid-Kind", "safe error"),
            (-32000, "operation_error", "unsafe\nlog"),
            (-32000, "operation_error", string.Concat(
                Enumerable.Repeat("é", 513))),
        })
        {
            var malformed = new Newtonsoft.Json.Linq.JObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = "request-taxonomy",
                ["error"] = new Newtonsoft.Json.Linq.JObject
                {
                    ["code"] = code,
                    ["message"] = message,
                    ["data"] = new Newtonsoft.Json.Linq.JObject
                    {
                        ["protocol_version"] = 1,
                        ["kind"] = kind,
                        ["retryable"] = false,
                    },
                },
            };
            Assert.IsType<ProtocolError>(
                Record.Exception(
                    () => StrictProtocolResponse.Parse(
                        malformed.ToString(Newtonsoft.Json.Formatting.None),
                        "request-taxonomy")));
        }
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
            ["error_contracts"] = new Newtonsoft.Json.Linq.JArray(
                "typed-v1",
                "future-v2"),
            ["limits"] = new Newtonsoft.Json.Linq.JObject
            {
                ["max_request_bytes"] = 20 * 1024 * 1024,
                ["max_response_bytes"] = 20 * 1024 * 1024,
                ["max_json_depth"] = 256,
            },
        };

        var parsed = ProtocolDataParser.ParseCapabilities(valid);
        Assert.Contains("system.capabilities", parsed.Methods);
        Assert.Contains("typed-v1", parsed.ErrorContracts);
        Assert.Contains("future-v2", parsed.ErrorContracts);
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

        valid["error_contracts"] = new Newtonsoft.Json.Linq.JArray(
            "typed-v1",
            "typed-v1");
        Assert.Throws<ProtocolError>(
            () => ProtocolDataParser.ParseCapabilities(valid));
        valid["error_contracts"] = new Newtonsoft.Json.Linq.JArray(
            "typed-v1");

        ((Newtonsoft.Json.Linq.JObject)valid["limits"]!).Remove("max_response_bytes");
        Assert.Throws<ProtocolError>(() => ProtocolDataParser.ParseCapabilities(valid));
    }

    [Fact]
    public void UsesCanonicalMessagesAndClosedRetrySemantics()
    {
        var cases = new[]
        {
            (-32700, "parse_error", "the Locker CLI returned invalid JSON", false),
            (-32600, "invalid_request", "the Locker CLI rejected the request envelope", false),
            (-32601, "method_not_found", "the requested Locker operation is not supported", false),
            (-32602, "invalid_params", "the Locker request parameters are invalid", false),
            (
                -32603,
                "internal_protocol_error",
                "the Locker CLI encountered an internal protocol error",
                false),
            (-32001, "authentication_error", "authentication failed", false),
            (
                -32003,
                "permission_denied",
                "you do not have permission to perform this operation",
                false),
            (-32004, "not_found_error", "the requested resource was not found", false),
            (-32060, "storage_error", "local storage operation failed", false),
            (-32051, "internal_error", "the request could not be completed", false),
            (
                -32051,
                "service_unavailable",
                "the service is temporarily unavailable",
                true),
        };
        foreach (var (code, kind, message, retryable) in cases)
        {
            var envelope = new Newtonsoft.Json.Linq.JObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = "request-canonical",
                ["error"] = new Newtonsoft.Json.Linq.JObject
                {
                    ["code"] = code,
                    ["message"] = "sensitive-value-from-broken-cli",
                    ["data"] = new Newtonsoft.Json.Linq.JObject
                    {
                        ["protocol_version"] = 1,
                        ["kind"] = kind,
                        ["retryable"] = true,
                    },
                },
            };
            var error = Assert.IsAssignableFrom<LockerError>(
                Record.Exception(
                    () => StrictProtocolResponse.Parse(
                        envelope.ToString(Newtonsoft.Json.Formatting.None),
                        "request-canonical")));
            Assert.Equal(message, error.Message);
            Assert.Equal(retryable, error.Retryable);
        }
    }

    [Fact]
    public void ValidatesAndExposesRateLimitRetryAfterSeconds()
    {
        foreach (var retryAfterSeconds in new[] { 0, 86400 })
        {
            var error = Assert.IsType<RateLimitError>(
                ParseError(
                    -32029,
                    "rate_limited",
                    retryAfterSeconds));
            Assert.Equal(retryAfterSeconds, error.RetryAfterSeconds);
            Assert.True(error.Retryable);
        }

        foreach (var retryAfterSeconds in new Newtonsoft.Json.Linq.JToken[]
        {
            true,
            -1,
            86401,
            1.5,
        })
        {
            Assert.IsType<ProtocolError>(
                ParseError(
                    -32029,
                    "rate_limited",
                    retryAfterSeconds));
        }

        var server = Assert.IsType<APIServerError>(
            ParseError(
                -32051,
                "service_unavailable",
                30));
        Assert.IsNotType<RateLimitError>(server);
    }

    [Fact]
    public void ValidatesAndSeparatesServerRequestId()
    {
        const string serverRequestId = "upstream_Request-123456";
        var mapped = Assert.IsType<APIServerError>(
            ParseServerRequestError(serverRequestId));
        Assert.Equal("json-rpc-request-id", mapped.RequestId);
        Assert.Equal(serverRequestId, mapped.ServerRequestId);

        foreach (var invalid in new JToken[]
        {
            true,
            "short",
            "request.id.not.allowed",
            new string('a', 129),
        })
        {
            Assert.IsType<ProtocolError>(
                ParseServerRequestError(invalid));
        }
    }

    private static LockerError ParseError(
        int code,
        string kind,
        Newtonsoft.Json.Linq.JToken retryAfterSeconds)
    {
        var envelope = new Newtonsoft.Json.Linq.JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = "request-rate-limit",
            ["error"] = new Newtonsoft.Json.Linq.JObject
            {
                ["code"] = code,
                ["message"] = "unsafe error detail",
                ["data"] = new Newtonsoft.Json.Linq.JObject
                {
                    ["protocol_version"] = 1,
                    ["kind"] = kind,
                    ["retryable"] = true,
                    ["retry_after_seconds"] = retryAfterSeconds,
                },
            },
        };
        return Assert.IsAssignableFrom<LockerError>(
            Record.Exception(
                () => StrictProtocolResponse.Parse(
                    envelope.ToString(Newtonsoft.Json.Formatting.None),
                    "request-rate-limit")));
    }

    private static LockerError ParseServerRequestError(
        JToken serverRequestId)
    {
        var envelope = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = "json-rpc-request-id",
            ["error"] = new JObject
            {
                ["code"] = -32051,
                ["message"] = "unsafe server detail",
                ["data"] = new JObject
                {
                    ["protocol_version"] = 1,
                    ["kind"] = "service_unavailable",
                    ["retryable"] = true,
                    ["server_request_id"] = serverRequestId,
                },
            },
        };
        return Assert.IsAssignableFrom<LockerError>(
            Record.Exception(
                () => StrictProtocolResponse.Parse(
                    envelope.ToString(Formatting.None),
                    "json-rpc-request-id")));
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
        var context = transport.CreateContext();
        context["error_contract"] = "typed-v1";
        await Assert.ThrowsAsync<LockerResponseTooLargeError>(
            () => transport.CallAsync(
                "secret.get",
                new Newtonsoft.Json.Linq.JObject
                {
                    ["context"] = context,
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
    public async Task TimeoutBudgetIncludesCapabilitiesAndOperation()
    {
        await WithFixtureModeAsync(
            "slow-capabilities",
            async () =>
            {
                using var client = CreateClient(
                    timeout: TimeSpan.FromMilliseconds(400));

                await Assert.ThrowsAsync<LockerTimeoutError>(
                    () => client.Secrets.GetAsync("short-sleep"));
            });
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
