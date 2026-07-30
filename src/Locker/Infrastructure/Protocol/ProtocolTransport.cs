using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Locker;

internal sealed class ProtocolTransport : IDisposable
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly string[] ChildEnvironmentAllowlist =
    {
        "PATH",
        "PATHEXT",
        "SystemRoot",
        "WINDIR",
        "COMSPEC",
        "TEMP",
        "TMP",
        "HOME",
        "USERPROFILE",
        "LOCALAPPDATA",
        "LANG",
        "LC_ALL",
        "SSL_CERT_FILE",
        "SSL_CERT_DIR",
        "HTTP_PROXY",
        "HTTPS_PROXY",
        "NO_PROXY",
        "ALL_PROXY",
        "http_proxy",
        "https_proxy",
        "no_proxy",
        "all_proxy",
    };

    private readonly LockerClientOptions options;
    private readonly Lazy<LockerCredentials> credentials;
    private readonly Lazy<LockerCliInstaller> managedInstaller;
    private readonly Func<CancellationToken, Task<string>> managedResolver;

    internal ProtocolTransport(LockerClientOptions options)
        : this(options, managedResolver: null)
    {
    }

    internal ProtocolTransport(
        LockerClientOptions options,
        Func<CancellationToken, Task<string>>? managedResolver)
    {
        this.options = options;
        credentials = new Lazy<LockerCredentials>(
            () => LockerCredentials.Resolve(
                options.AccessKeyId,
                options.SecretAccessKey),
            LazyThreadSafetyMode.ExecutionAndPublication);
        managedInstaller = new Lazy<LockerCliInstaller>(
            () => new LockerCliInstaller(),
            LazyThreadSafetyMode.ExecutionAndPublication);
        this.managedResolver = managedResolver
            ?? (token => managedInstaller.Value.ResolveForExecutionAsync(token));
    }

    internal async Task<CliBinaryIdentity> ResolveBinaryIdentityAsync(
        CancellationToken cancellationToken)
    {
        var path = await LockerCliResolver.ResolveAsync(
                options,
                managedResolver,
                cancellationToken)
            .ConfigureAwait(false);
        return CliBinaryIdentity.Capture(path);
    }

    public void Dispose()
    {
        if (managedInstaller.IsValueCreated)
        {
            managedInstaller.Value.Dispose();
        }
    }

    internal async Task<ProtocolCallResult> CallAsync(
        string method,
        JObject parameters,
        CancellationToken cancellationToken,
        int maxRequestBytes = LockerClientOptions.ProtocolRequestLimitBytes,
        int maxResponseBytes = LockerClientOptions.ProtocolResponseLimitBytes,
        int maxJsonDepth = LockerClientOptions.ProtocolJsonDepthLimit,
        CliBinaryIdentity? expectedBinaryIdentity = null,
        string? expectedCliVersion = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = credentials.Value;
        if (maxRequestBytes <= 0
            || maxRequestBytes > LockerClientOptions.ProtocolRequestLimitBytes
            || maxResponseBytes <= 0
            || maxResponseBytes > LockerClientOptions.ProtocolResponseLimitBytes
            || maxJsonDepth <= 0
            || maxJsonDepth > LockerClientOptions.ProtocolJsonDepthLimit)
        {
            throw new ProtocolError("Locker SDK protocol limits are invalid.");
        }

        // Resolve immediately before every process execution. For a managed
        // binary this re-runs signed-manifest plus streamed digest/header
        // checks; its detached signature is bound on install or first cache
        // load. Explicit caller-owned paths retain identity-only validation.
        var binaryIdentity = await ResolveBinaryIdentityAsync(cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        using var executionLease = AcquireExecutionLease(
            binaryIdentity.CanonicalPath);
        var leasedIdentity = CliBinaryIdentity.Capture(
            binaryIdentity.CanonicalPath);
        if (leasedIdentity != binaryIdentity)
        {
            throw new CliBinaryChangedError(safeToRetry: true);
        }

        binaryIdentity = leasedIdentity;
        if (expectedBinaryIdentity is not null
            && binaryIdentity != expectedBinaryIdentity)
        {
            throw new CliBinaryChangedError(safeToRetry: true);
        }
        cancellationToken.ThrowIfCancellationRequested();
        var requestId = Guid.NewGuid().ToString("N");
        var request = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = requestId,
            ["method"] = method,
            ["params"] = parameters,
        };
        var requestBytes = StrictUtf8.GetBytes(request.ToString(Formatting.None));
        if (requestBytes.Length > maxRequestBytes)
        {
            CryptographicOperations.ZeroMemory(requestBytes);
            throw new ProtocolError("Locker SDK protocol request exceeds the negotiated limit.");
        }

        var startInfo = CreateStartInfo(binaryIdentity.CanonicalPath);
        ProcessTreeGuard processTree;
        try
        {
            processTree = ProcessTreeGuard.Prepare(startInfo);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            CryptographicOperations.ZeroMemory(requestBytes);
            throw new CliRunError(
                "Locker CLI process isolation could not be initialized.",
                innerException: ex);
        }
        using var processTreeLease = processTree;
        using var process = new Process { StartInfo = startInfo };

        try
        {
            if (!process.Start())
            {
                throw new CliRunError("Locker CLI could not be started.");
            }
            processTree.Attach(process);
        }
        catch (LockerError)
        {
            CryptographicOperations.ZeroMemory(requestBytes);
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            CryptographicOperations.ZeroMemory(requestBytes);
            throw new CliRunError("Locker CLI could not be started.", innerException: ex);
        }

        using var timeout = new CancellationTokenSource(options.Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token);
        using var boundedReadMonitorCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(linked.Token);

        var stdoutTask = ReadBoundedAsync(
            process.StandardOutput.BaseStream,
            Math.Min(options.MaxStdoutBytes, maxResponseBytes),
            "stdout",
            CancellationToken.None);
        var stderrTask = ReadBoundedAsync(
            process.StandardError.BaseStream,
            options.MaxStderrBytes,
            "stderr",
            CancellationToken.None);

        try
        {
            await process.StandardInput.BaseStream.WriteAsync(requestBytes, linked.Token)
                .ConfigureAwait(false);
            await process.StandardInput.BaseStream.FlushAsync(linked.Token).ConfigureAwait(false);
            process.StandardInput.Close();

            var exchangeTask = Task.WhenAll(
                stdoutTask,
                stderrTask,
                process.WaitForExitAsync(CancellationToken.None));
            var cancellationSignal = Task.Delay(Timeout.InfiniteTimeSpan, linked.Token);
            var boundedReadFailure = Task.WhenAny(
                MonitorBoundedReadFailureAsync(
                    stdoutTask,
                    boundedReadMonitorCancellation.Token),
                MonitorBoundedReadFailureAsync(
                    stderrTask,
                    boundedReadMonitorCancellation.Token));
            var completed = await Task.WhenAny(
                exchangeTask,
                cancellationSignal,
                boundedReadFailure).ConfigureAwait(false);
            if (completed == boundedReadFailure)
            {
                await await boundedReadFailure.ConfigureAwait(false);
            }

            if (completed == cancellationSignal)
            {
                processTree.Terminate(process);
                await DrainAfterTerminationAsync(stdoutTask, stderrTask).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                throw new LockerTimeoutError();
            }

            await exchangeTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            processTree.Terminate(process);
            await DrainAfterTerminationAsync(stdoutTask, stderrTask).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            throw new LockerTimeoutError();
        }
        catch (LockerResponseTooLargeError)
        {
            processTree.Terminate(process);
            await DrainAfterTerminationAsync(stdoutTask, stderrTask).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex) when (ex is not LockerError)
        {
            processTree.Terminate(process);
            await DrainAfterTerminationAsync(stdoutTask, stderrTask).ConfigureAwait(false);
            throw new CliRunError("Locker CLI protocol transport failed.", innerException: ex);
        }
        finally
        {
            boundedReadMonitorCancellation.Cancel();
            CryptographicOperations.ZeroMemory(requestBytes);
        }

        if (process.ExitCode != 0)
        {
            ZeroCompletedOutput(stdoutTask);
            ZeroCompletedOutput(stderrTask);
            throw new CliRunError(
                "Locker CLI exited before completing the protocol exchange.",
                process.ExitCode);
        }

        string stdout;
        var stdoutBytes = await stdoutTask.ConfigureAwait(false);
        try
        {
            stdout = StrictUtf8.GetString(stdoutBytes);
        }
        catch (DecoderFallbackException)
        {
            throw new ProtocolError("Locker CLI stdout is not valid UTF-8.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(stdoutBytes);
        }

        ZeroCompletedOutput(stderrTask);

        cancellationToken.ThrowIfCancellationRequested();
        var decoded = StrictProtocolResponse.Parse(
            stdout,
            requestId,
            maxJsonDepth);
        cancellationToken.ThrowIfCancellationRequested();
        if (expectedCliVersion is not null
            && !string.Equals(
                decoded.CliVersion,
                expectedCliVersion,
                StringComparison.Ordinal))
        {
            throw new ProtocolError(
                "Locker CLI response version differs from negotiated capabilities.",
                requestId: requestId);
        }

        var identityAfter = CliBinaryIdentity.Capture(
            binaryIdentity.CanonicalPath);
        if (identityAfter != binaryIdentity)
        {
            throw new CliBinaryChangedError(safeToRetry: false);
        }

        return new ProtocolCallResult(
            decoded.Data,
            decoded.CliVersion,
            identityAfter);
    }

    internal JObject CreateContext()
    {
        var resolvedCredentials = credentials.Value;
        var context = new JObject
        {
            ["protocol_version"] = LockerSdkMetadata.ProtocolVersion,
            ["credentials"] = new JObject
            {
                ["access_key_id"] = resolvedCredentials.AccessKeyId,
                ["secret_access_key"] =
                    resolvedCredentials.SecretAccessKey,
            },
            ["client"] = new JObject
            {
                ["name"] = "locker-dotnet",
                ["version"] = LockerSdkMetadata.Version,
            },
            ["transport"] = new JObject
            {
                ["api_base"] = options.ApiBase,
                ["headers"] = JObject.FromObject(options.Headers),
                ["insecure_skip_tls_verify"] = options.InsecureSkipTlsVerify,
            },
            ["cache"] = new JObject
            {
                ["force_refresh"] = options.ForceRefresh,
                ["max_age_seconds"] = options.MaxAgeSeconds,
            },
        };
        return context;
    }

    private static ProcessStartInfo CreateStartInfo(string cliPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = cliPath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("sdk");

        var allowed = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in ChildEnvironmentAllowlist)
        {
            var value = System.Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrEmpty(value))
            {
                allowed[name] = value;
            }
        }

        startInfo.Environment.Clear();
        foreach (var pair in allowed)
        {
            startInfo.Environment[pair.Key] = pair.Value;
        }

        return startInfo;
    }

    private static FileStream AcquireExecutionLease(string path)
    {
        try
        {
            return new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 1,
                FileOptions.RandomAccess);
        }
        catch (Exception error) when (
            error is IOException
            or UnauthorizedAccessException
            or System.Security.SecurityException)
        {
            throw new CliRunError(
                "Locker CLI executable could not be bound for execution.",
                innerException: error);
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(
        Stream stream,
        int limit,
        string streamName,
        CancellationToken cancellationToken)
    {
        using var output = new MemoryStream(Math.Min(limit, 81920));
        var buffer = new byte[81920];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return output.ToArray();
            }

            if (output.Length + read > limit)
            {
                throw new LockerResponseTooLargeError(streamName);
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task DrainAfterTerminationAsync(params Task<byte[]>[] tasks)
    {
        try
        {
            await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch
        {
            // Preserve the original bounded transport error without exposing child output.
        }
        finally
        {
            foreach (var task in tasks)
            {
                ZeroCompletedOutput(task);
            }
        }
    }

    private static void ZeroCompletedOutput(Task<byte[]> task)
    {
        if (task.Status == TaskStatus.RanToCompletion)
        {
            CryptographicOperations.ZeroMemory(task.Result);
        }
    }

    private static async Task MonitorBoundedReadFailureAsync(
        Task<byte[]> task,
        CancellationToken cancellationToken)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (LockerResponseTooLargeError)
        {
            throw;
        }

        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
    }
}
