using System.Collections.Frozen;
using Newtonsoft.Json.Linq;

namespace Locker;

public sealed class LockerClient : IDisposable
{
    private static readonly HashSet<string> RequiredMethods = new(StringComparer.Ordinal)
    {
        "secret.get",
        "secret.list",
        "secret.create",
        "secret.update",
        "environment.get",
        "environment.list",
        "environment.create",
        "environment.update",
        "system.capabilities",
    };

    private readonly ProtocolTransport transport;
    private readonly SemaphoreSlim capabilitiesLock = new(1, 1);
    private NegotiatedState? negotiatedState;

    public LockerClient(LockerClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Options = options;
        transport = new ProtocolTransport(options);
        Secrets = new SecretService(this);
        Environments = new EnvironmentService(this);
    }

    public LockerClientOptions Options { get; }
    public SecretService Secrets { get; }
    public EnvironmentService Environments { get; }

    internal JObject CreateContext() => transport.CreateContext();

    internal async Task<JToken> CallOperationAsync(
        string method,
        JObject parameters,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await EnsureCapabilitiesAsync(cancellationToken).ConfigureAwait(false);
            var state = Volatile.Read(ref negotiatedState)
                ?? throw new ProtocolError("Locker CLI capabilities are unavailable.");
            if (!state.SupportedMethods.Contains(method))
            {
                throw new ProtocolError("Locker CLI does not advertise the requested SDK operation.");
            }

            try
            {
                var result = await transport.CallAsync(
                        method,
                        parameters,
                        cancellationToken,
                        state.MaxRequestBytes,
                        state.MaxResponseBytes,
                        state.MaxJsonDepth,
                        state.BinaryIdentity,
                        state.CliVersion)
                    .ConfigureAwait(false);
                return result.Data;
            }
            catch (CliBinaryChangedError error) when (
                attempt == 0
                && error.SafeToRetry)
            {
                InvalidateCapabilities();
            }
        }

        throw new CliBinaryChangedError(safeToRetry: false);
    }

    public async Task EnsureCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        var observedIdentity = await transport
            .ResolveBinaryIdentityAsync(cancellationToken)
            .ConfigureAwait(false);
        var currentState = Volatile.Read(ref negotiatedState);
        if (currentState?.BinaryIdentity == observedIdentity)
        {
            return;
        }

        await capabilitiesLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            observedIdentity = await transport
                .ResolveBinaryIdentityAsync(cancellationToken)
                .ConfigureAwait(false);
            currentState = Volatile.Read(ref negotiatedState);
            if (currentState?.BinaryIdentity == observedIdentity)
            {
                return;
            }

            InvalidateCapabilities();
            var result = await transport.CallAsync(
                    "system.capabilities",
                    new JObject(),
                    cancellationToken,
                    expectedBinaryIdentity: observedIdentity)
                .ConfigureAwait(false);
            var capabilities = ProtocolDataParser.ParseCapabilities(result.Data);
            ValidateRequiredMethods(capabilities.Methods);
            if (!string.Equals(
                capabilities.CliVersion,
                result.CliVersion,
                StringComparison.Ordinal))
            {
                throw new ProtocolError(
                    "Locker CLI capability version differs from response metadata.");
            }

            var nextState = new NegotiatedState(
                capabilities.Methods.ToFrozenSet(StringComparer.Ordinal),
                capabilities.MaxRequestBytes,
                capabilities.MaxResponseBytes,
                capabilities.MaxJsonDepth,
                result.BinaryIdentity,
                capabilities.CliVersion);
            Volatile.Write(ref negotiatedState, nextState);
        }
        finally
        {
            capabilitiesLock.Release();
        }
    }

    internal static void ValidateRequiredMethods(IReadOnlySet<string> methods)
    {
        ArgumentNullException.ThrowIfNull(methods);
        if (!RequiredMethods.IsSubsetOf(methods))
        {
            throw new ProtocolError(
                "Locker CLI is missing a required protocol v1 method.");
        }
    }

    private void InvalidateCapabilities()
    {
        Volatile.Write(ref negotiatedState, null);
    }

    public void Dispose() => capabilitiesLock.Dispose();

    private sealed record NegotiatedState(
        FrozenSet<string> SupportedMethods,
        int MaxRequestBytes,
        int MaxResponseBytes,
        int MaxJsonDepth,
        CliBinaryIdentity BinaryIdentity,
        string CliVersion);
}
