using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace Locker;

internal static partial class SignedUpdateContract
{
    internal const string ReleaseBaseUrl = "https://files.locker.io/cli/releases/";
    internal const string ReleaseKeyId = "locker-cli-release-v1";
    internal const int CheckIntervalSeconds = 21_600;
    internal const int MaxLatestBytes = 64 * 1024;
    internal const int MaxManifestBytes = 1024 * 1024;
    internal const int MaxStateBytes = 64 * 1024;
    internal const int MaxArtifactBytes = 256 * 1024 * 1024;
    internal const int SignatureSize = 64;
    internal const int PublicKeySize = 32;

    private const int SchemaVersion = 2;
    private const int MaxJsonDepth = 64;
    private const string EnvelopeSchema = "io.locker.cli.signed-envelope";
    private const string LatestSchema = "io.locker.cli.update-latest";
    private const string ManifestSchema = "io.locker.cli.update-manifest";
    private const string Product = "locker-cli";
    private const string Algorithm = "Ed25519";
    private const string ProtocolName = "locker.sdk";
    private const string ProtocolTransport = "json-rpc-2.0-stdio";

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static readonly UpdateTarget[] CanonicalTargets =
    {
        new("linux", "amd64", "locker-linux-amd64"),
        new("linux", "arm64", "locker-linux-arm64"),
        new("darwin", "amd64", "locker-darwin-amd64"),
        new("darwin", "arm64", "locker-darwin-arm64"),
        new("windows", "amd64", "locker-windows-amd64.exe"),
    };

    internal static ReleaseTrust ParseReleaseTrust(byte[] bytes)
    {
        var root = RequireObject(ParseStrictJson(bytes, MaxStateBytes), "release trust");
        RequireExactFields(
            root,
            "base_url",
            "check_interval_seconds",
            "key_id",
            "public_key",
            "schema_version");
        RequireCanonicalFile(root, bytes, "release trust");

        var schemaVersion = RequireInteger(root, "schema_version");
        var baseUrl = RequireString(root, "base_url");
        var keyId = RequireString(root, "key_id");
        var publicKey = RequireString(root, "public_key");
        var checkInterval = RequireInteger(root, "check_interval_seconds");
        if (schemaVersion != SchemaVersion
            || baseUrl != ReleaseBaseUrl
            || keyId != ReleaseKeyId
            || checkInterval != CheckIntervalSeconds)
        {
            throw new InvalidDataException("Locker CLI release trust metadata is invalid.");
        }

        if (publicKey.Length != 0)
        {
            _ = DecodePublicKey(publicKey);
        }

        return new ReleaseTrust(
            new Uri(baseUrl, UriKind.Absolute),
            keyId,
            publicKey,
            TimeSpan.FromSeconds(checkInterval));
    }

    internal static byte[] DecodePublicKey(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            throw new LockerCliDistributionUnavailableError(
                "Managed installation is unavailable because the bundled Locker CLI release public key is missing.");
        }

        return DecodeCanonicalBase64Url(value, PublicKeySize, "release public key");
    }

    internal static LatestRelease ParseLatest(byte[] bytes, byte[] publicKey)
    {
        var envelope = VerifyEnvelope(bytes, publicKey, LatestSchema, MaxLatestBytes);
        var root = envelope.Payload;
        RequireExactFields(
            root,
            "manifest",
            "product",
            "schema",
            "schema_version",
            "source_commit",
            "version");
        RequireString(root, "product", Product);
        var version = RequireReleaseVersion(root, "version");
        var sourceCommit = RequireCommit(root, "source_commit");
        var manifest = RequireObject(root["manifest"], "latest manifest");
        RequireExactFields(manifest, "path", "sha256", "size");

        var path = RequireString(manifest, "path");
        var sha256 = RequireSha256(manifest, "sha256");
        var size = RequireInteger(manifest, "size");
        if (path != $"{version}/manifest.json"
            || path.Length > 320
            || size is < 1 or > MaxManifestBytes)
        {
            throw new InvalidDataException("Signed latest manifest pointer is invalid.");
        }

        return new LatestRelease(
            version,
            sourceCommit,
            new ManifestPointer(path, sha256, checked((int)size)));
    }

    internal static ReleaseManifest ParseManifest(byte[] bytes, byte[] publicKey)
    {
        var envelope = VerifyEnvelope(bytes, publicKey, ManifestSchema, MaxManifestBytes);
        var root = envelope.Payload;
        RequireExactFields(
            root,
            "artifacts",
            "product",
            "protocol",
            "schema",
            "schema_version",
            "source_commit",
            "version");
        RequireString(root, "product", Product);
        var version = RequireReleaseVersion(root, "version");
        var sourceCommit = RequireCommit(root, "source_commit");

        var protocol = RequireObject(root["protocol"], "manifest protocol");
        RequireExactFields(protocol, "max_version", "min_version", "name", "transport");
        if (RequireInteger(protocol, "min_version") != LockerSdkMetadata.ProtocolVersion
            || RequireInteger(protocol, "max_version") != LockerSdkMetadata.ProtocolVersion
            || RequireString(protocol, "name") != ProtocolName
            || RequireString(protocol, "transport") != ProtocolTransport)
        {
            throw new InvalidDataException("Signed manifest protocol is incompatible.");
        }

        if (root["artifacts"] is not JArray artifacts
            || artifacts.Count != CanonicalTargets.Length)
        {
            throw new InvalidDataException(
                "Signed manifest must contain the canonical five artifacts.");
        }

        var expected = CanonicalTargets.ToDictionary(
            target => target.Filename,
            StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var parsed = new List<ReleaseArtifact>(CanonicalTargets.Length);
        foreach (var token in artifacts)
        {
            var artifact = RequireObject(token, "manifest artifact");
            RequireExactFields(
                artifact,
                "arch",
                "filename",
                "os",
                "path",
                "sha256",
                "signature_path",
                "size");

            var filename = RequireString(artifact, "filename");
            var os = RequireString(artifact, "os");
            var arch = RequireString(artifact, "arch");
            var path = RequireString(artifact, "path");
            var signaturePath = RequireString(artifact, "signature_path");
            var sha256 = RequireSha256(artifact, "sha256");
            var size = RequireInteger(artifact, "size");
            if (!expected.TryGetValue(filename, out var target)
                || !seen.Add(filename)
                || target.OS != os
                || target.Arch != arch
                || path != $"{version}/{filename}"
                || signaturePath != $"{version}/{filename}.sig"
                || path.Length > 320
                || signaturePath.Length > 324
                || size is < 1 or > MaxArtifactBytes)
            {
                throw new InvalidDataException("Signed manifest artifact is invalid.");
            }

            parsed.Add(new ReleaseArtifact(
                os,
                arch,
                filename,
                path,
                signaturePath,
                sha256,
                size));
        }

        return new ReleaseManifest(version, sourceCommit, parsed);
    }

    internal static ReleaseArtifact SelectCurrentArtifact(ReleaseManifest manifest)
    {
        var target = CurrentTarget();
        return manifest.Artifacts.SingleOrDefault(
            artifact => artifact.Filename == target.Filename
                && artifact.OS == target.OS
                && artifact.Arch == target.Arch)
            ?? throw new PlatformNotSupportedException(
                "The signed Locker CLI release has no artifact for this platform.");
    }

    internal static UpdateTarget CurrentTarget()
    {
        var arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture;
        if (OperatingSystem.IsWindows() && arch == System.Runtime.InteropServices.Architecture.X64)
        {
            return CanonicalTargets[4];
        }

        if (OperatingSystem.IsLinux())
        {
            return arch switch
            {
                System.Runtime.InteropServices.Architecture.X64 => CanonicalTargets[0],
                System.Runtime.InteropServices.Architecture.Arm64 => CanonicalTargets[1],
                _ => throw new PlatformNotSupportedException(
                    "Locker CLI supports only amd64 and arm64 Linux processes."),
            };
        }

        if (OperatingSystem.IsMacOS())
        {
            return arch switch
            {
                System.Runtime.InteropServices.Architecture.X64 => CanonicalTargets[2],
                System.Runtime.InteropServices.Architecture.Arm64 => CanonicalTargets[3],
                _ => throw new PlatformNotSupportedException(
                    "Locker CLI supports only amd64 and arm64 macOS processes."),
            };
        }

        throw new PlatformNotSupportedException(
            "The signed Locker CLI channel does not support this platform.");
    }

    internal static void VerifyLatestManifestBinding(
        LatestRelease latest,
        ReleaseManifest manifest,
        byte[] manifestBytes)
    {
        if (latest.Version != manifest.Version
            || latest.SourceCommit != manifest.SourceCommit
            || manifestBytes.Length != latest.Manifest.Size
            || !MatchesSha256(manifestBytes, latest.Manifest.Sha256))
        {
            throw new InvalidDataException(
                "Signed latest metadata does not bind the signed manifest.");
        }
    }

    internal static void VerifyDetachedSignature(
        byte[] data,
        byte[] signature,
        byte[] publicKey)
    {
        if (signature.Length != SignatureSize || publicKey.Length != PublicKeySize)
        {
            throw new InvalidDataException(
                "Locker CLI detached signature metadata is invalid.");
        }

        var verifier = new Ed25519Signer();
        verifier.Init(false, new Ed25519PublicKeyParameters(publicKey, 0));
        verifier.BlockUpdate(data, 0, data.Length);
        if (!verifier.VerifySignature(signature))
        {
            throw new InvalidDataException(
                "Locker CLI detached signature verification failed.");
        }
    }

    internal static void VerifyArtifactHeader(byte[] data, ReleaseArtifact artifact)
    {
        var target = CanonicalTargets.SingleOrDefault(
            candidate => candidate.Filename == artifact.Filename);
        if (target is null || target.OS != artifact.OS || target.Arch != artifact.Arch)
        {
            throw new InvalidDataException("Locker CLI artifact target is invalid.");
        }

        switch (artifact.OS)
        {
            case "linux":
                {
                    if (data.Length < 20
                        || data[0] != 0x7f
                        || data[1] != (byte)'E'
                        || data[2] != (byte)'L'
                        || data[3] != (byte)'F'
                        || data[4] != 2
                        || data[5] is not (1 or 2))
                    {
                        throw new InvalidDataException(
                            "Locker CLI artifact is not a 64-bit ELF executable.");
                    }

                    var machine = data[5] == 1
                        ? System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(18, 2))
                        : System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(18, 2));
                    var expected = artifact.Arch == "amd64" ? (ushort)0x3e : (ushort)0xb7;
                    if (machine != expected)
                    {
                        throw new InvalidDataException(
                            "Locker CLI ELF architecture is invalid.");
                    }

                    break;
                }
            case "darwin":
                {
                    if (data.Length < 8
                        || !data.AsSpan(0, 4).SequenceEqual(
                            new byte[] { 0xcf, 0xfa, 0xed, 0xfe }))
                    {
                        throw new InvalidDataException(
                            "Locker CLI artifact is not a 64-bit little-endian Mach-O executable.");
                    }

                    var cpu = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(
                        data.AsSpan(4, 4));
                    var expected = artifact.Arch == "amd64" ? 0x01000007u : 0x0100000cu;
                    if (cpu != expected)
                    {
                        throw new InvalidDataException(
                            "Locker CLI Mach-O architecture is invalid.");
                    }

                    break;
                }
            case "windows":
                {
                    if (data.Length < 64 || data[0] != (byte)'M' || data[1] != (byte)'Z')
                    {
                        throw new InvalidDataException(
                            "Locker CLI artifact is not a PE executable.");
                    }

                    var headerOffset = checked((int)System.Buffers.Binary.BinaryPrimitives
                        .ReadUInt32LittleEndian(data.AsSpan(60, 4)));
                    if (headerOffset < 64
                        || headerOffset > data.Length - 6
                        || !data.AsSpan(headerOffset, 4).SequenceEqual(
                            new byte[] { (byte)'P', (byte)'E', 0, 0 })
                        || System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(
                            data.AsSpan(headerOffset + 4, 2)) != 0x8664)
                    {
                        throw new InvalidDataException(
                            "Locker CLI PE architecture is invalid.");
                    }

                    break;
                }
            default:
                throw new InvalidDataException(
                    "Locker CLI artifact operating system is invalid.");
        }
    }

    internal static int CompareVersions(string left, string right)
    {
        var leftMatch = ReleaseVersionPattern().Match(left);
        var rightMatch = ReleaseVersionPattern().Match(right);
        if (!leftMatch.Success || !rightMatch.Success)
        {
            throw new InvalidDataException("Locker CLI release version is invalid.");
        }

        var minor = CompareNumericComponent(
            leftMatch.Groups[1].Value,
            rightMatch.Groups[1].Value);
        return minor != 0
            ? minor
            : CompareNumericComponent(
                leftMatch.Groups[2].Value,
                rightMatch.Groups[2].Value);
    }

    private static int CompareNumericComponent(string left, string right)
    {
        var length = left.Length.CompareTo(right.Length);
        return length != 0 ? length : string.CompareOrdinal(left, right);
    }

    internal static bool MatchesSha256(byte[] data, string expected)
    {
        if (!Sha256Pattern().IsMatch(expected))
        {
            return false;
        }

        var actual = SHA256.HashData(data);
        var expectedBytes = Convert.FromHexString(expected);
        try
        {
            return CryptographicOperations.FixedTimeEquals(actual, expectedBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(actual);
            CryptographicOperations.ZeroMemory(expectedBytes);
        }
    }

    internal static string Sha256Hex(byte[] data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    internal static JToken ParseStrictJson(byte[] data, int maximumBytes)
    {
        if (data.Length is < 1
            || data.Length > maximumBytes
            || data.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }))
        {
            throw new InvalidDataException("Strict update JSON input is invalid.");
        }

        string json;
        try
        {
            json = StrictUtf8.GetString(data);
        }
        catch (DecoderFallbackException error)
        {
            throw new InvalidDataException(
                "Strict update JSON is not valid UTF-8.",
                error);
        }

        RejectComments(json);
        try
        {
            using var text = new StringReader(json);
            using var reader = new JsonTextReader(text)
            {
                CloseInput = true,
                DateParseHandling = DateParseHandling.None,
                FloatParseHandling = FloatParseHandling.Decimal,
                MaxDepth = MaxJsonDepth,
                SupportMultipleContent = false,
            };
            var value = JToken.Load(
                reader,
                new JsonLoadSettings
                {
                    CommentHandling = CommentHandling.Load,
                    DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                    LineInfoHandling = LineInfoHandling.Ignore,
                });
            if (reader.Read())
            {
                throw new InvalidDataException(
                    "Strict update JSON contains trailing data.");
            }

            ValidateValue(value);
            return value;
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("Strict update JSON is invalid.", error);
        }
    }

    internal static byte[] CanonicalJson(JToken value)
    {
        ValidateValue(value);
        var output = new StringBuilder();
        WriteCanonical(value, output);
        return Encoding.ASCII.GetBytes(output.ToString());
    }

    internal static void RequireCanonicalFile(
        JToken value,
        byte[] bytes,
        string label)
    {
        var canonical = CanonicalJson(value);
        var expected = new byte[canonical.Length + 1];
        canonical.CopyTo(expected, 0);
        expected[^1] = (byte)'\n';
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(bytes, expected))
            {
                throw new InvalidDataException(
                    $"{label} must be canonical JSON followed by exactly one LF.");
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(expected);
        }
    }

    internal static bool HasExactFields(JObject value, params string[] fields) =>
        value.Count == fields.Length && fields.All(value.ContainsKey);

    internal static JObject RequireObject(JToken? value, string label) =>
        value as JObject
        ?? throw new InvalidDataException($"{label} must be an object.");

    internal static void RequireExactFields(JObject value, params string[] fields)
    {
        if (!HasExactFields(value, fields))
        {
            throw new InvalidDataException(
                "Signed update metadata contains missing or unknown fields.");
        }
    }

    internal static string RequireString(
        JObject value,
        string field,
        string? exact = null)
    {
        if (value[field] is not JValue { Type: JTokenType.String } token)
        {
            throw new InvalidDataException(
                $"Signed update field {field} must be a string.");
        }

        var result = (string)token.Value!;
        if (exact is not null && result != exact)
        {
            throw new InvalidDataException(
                $"Signed update field {field} has an invalid value.");
        }

        return result;
    }

    internal static long RequireInteger(JObject value, string field)
    {
        if (value[field] is not JValue { Type: JTokenType.Integer } token)
        {
            throw new InvalidDataException(
                $"Signed update field {field} must be an integer.");
        }

        try
        {
            return Convert.ToInt64(token.Value, CultureInfo.InvariantCulture);
        }
        catch (Exception error) when (
            error is OverflowException or InvalidCastException or FormatException)
        {
            throw new InvalidDataException(
                $"Signed update field {field} is outside the signed 64-bit range.",
                error);
        }
    }

    private static SignedEnvelope VerifyEnvelope(
        byte[] bytes,
        byte[] publicKey,
        string expectedPayloadSchema,
        int maximumBytes)
    {
        if (publicKey.Length != PublicKeySize)
        {
            throw new InvalidDataException("Locker CLI release public key is invalid.");
        }

        var root = RequireObject(ParseStrictJson(bytes, maximumBytes), "signed envelope");
        RequireExactFields(
            root,
            "algorithm",
            "key_id",
            "payload",
            "schema",
            "schema_version",
            "signature");
        RequireCanonicalFile(root, bytes, "signed update envelope");
        RequireString(root, "algorithm", Algorithm);
        RequireString(root, "key_id", ReleaseKeyId);
        RequireString(root, "schema", EnvelopeSchema);
        if (RequireInteger(root, "schema_version") != SchemaVersion)
        {
            throw new InvalidDataException(
                "Signed update envelope schema version is invalid.");
        }

        var payloadBytes = DecodeCanonicalBase64Url(
            RequireString(root, "payload"),
            expectedLength: null,
            "signed payload");
        if (payloadBytes.Length is < 1 || payloadBytes.Length > maximumBytes)
        {
            throw new InvalidDataException("Signed update payload size is invalid.");
        }

        var signature = DecodeCanonicalBase64Url(
            RequireString(root, "signature"),
            SignatureSize,
            "signed envelope signature");
        var payload = RequireObject(
            ParseStrictJson(payloadBytes, maximumBytes),
            "signed update payload");
        var canonicalPayload = CanonicalJson(payload);
        if (!CryptographicOperations.FixedTimeEquals(payloadBytes, canonicalPayload))
        {
            throw new InvalidDataException(
                "Signed update payload is not canonical JSON.");
        }

        RequireString(payload, "schema", expectedPayloadSchema);
        if (RequireInteger(payload, "schema_version") != SchemaVersion)
        {
            throw new InvalidDataException(
                "Signed update payload schema version is invalid.");
        }

        var verifier = new Ed25519Signer();
        verifier.Init(false, new Ed25519PublicKeyParameters(publicKey, 0));
        verifier.BlockUpdate(payloadBytes, 0, payloadBytes.Length);
        if (!verifier.VerifySignature(signature))
        {
            throw new InvalidDataException(
                "Signed update envelope signature verification failed.");
        }

        return new SignedEnvelope(payload, payloadBytes);
    }

    private static byte[] DecodeCanonicalBase64Url(
        string value,
        int? expectedLength,
        string label)
    {
        if (value.Length == 0
            || value.Any(character =>
                !char.IsAsciiLetterOrDigit(character)
                && character is not ('_' or '-'))
            || value.Contains('='))
        {
            throw new InvalidDataException($"{label} is not canonical base64url.");
        }

        byte[] decoded;
        try
        {
            var padding = (4 - (value.Length % 4)) % 4;
            var base64 = value.Replace('-', '+').Replace('_', '/')
                + new string('=', padding);
            decoded = Convert.FromBase64String(base64);
        }
        catch (FormatException error)
        {
            throw new InvalidDataException(
                $"{label} is not canonical base64url.",
                error);
        }

        var roundTrip = Convert.ToBase64String(decoded)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        if (roundTrip != value
            || expectedLength is not null
            && decoded.Length != expectedLength)
        {
            throw new InvalidDataException($"{label} is not canonical base64url.");
        }

        return decoded;
    }

    private static string RequireReleaseVersion(JObject value, string field)
    {
        var result = RequireString(value, field);
        if (!ReleaseVersionPattern().IsMatch(result))
        {
            throw new InvalidDataException(
                "Locker CLI release version must be a canonical stable major-2 version.");
        }

        return result;
    }

    private static string RequireCommit(JObject value, string field)
    {
        var result = RequireString(value, field);
        if (!CommitPattern().IsMatch(result))
        {
            throw new InvalidDataException(
                "Locker CLI source commit is invalid.");
        }

        return result;
    }

    private static string RequireSha256(JObject value, string field)
    {
        var result = RequireString(value, field);
        if (!Sha256Pattern().IsMatch(result))
        {
            throw new InvalidDataException(
                "Locker CLI SHA-256 value is invalid.");
        }

        return result;
    }

    private static void ValidateValue(JToken value)
    {
        switch (value.Type)
        {
            case JTokenType.Object:
                foreach (var property in ((JObject)value).Properties())
                {
                    RequireAscii(property.Name);
                    ValidateValue(property.Value);
                }

                break;
            case JTokenType.Array:
                foreach (var child in value.Children())
                {
                    ValidateValue(child);
                }

                break;
            case JTokenType.String:
                RequireAscii((string)((JValue)value).Value!);
                break;
            case JTokenType.Integer:
                try
                {
                    _ = Convert.ToInt64(
                        ((JValue)value).Value,
                        CultureInfo.InvariantCulture);
                }
                catch (Exception error) when (
                    error is OverflowException or InvalidCastException or FormatException)
                {
                    throw new InvalidDataException(
                        "Update JSON integer is outside the signed 64-bit range.",
                        error);
                }

                break;
            case JTokenType.Boolean:
            case JTokenType.Null:
                break;
            default:
                throw new InvalidDataException(
                    "Update JSON supports only objects, arrays, ASCII strings, signed 64-bit integers, booleans, and null.");
        }
    }

    private static void RequireAscii(string value)
    {
        if (value.Any(character => character > 0x7f))
        {
            throw new InvalidDataException(
                "Update JSON keys and string values must be ASCII.");
        }
    }

    private static void RejectComments(string json)
    {
        var inString = false;
        var escaped = false;
        foreach (var character in json)
        {
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (character == '\\')
                {
                    escaped = true;
                }
                else if (character == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (character == '"')
            {
                inString = true;
            }
            else if (character == '/')
            {
                throw new InvalidDataException(
                    "Strict update JSON comments are not allowed.");
            }
        }
    }

    private static void WriteCanonical(JToken value, StringBuilder output)
    {
        switch (value.Type)
        {
            case JTokenType.Object:
                {
                    output.Append('{');
                    var first = true;
                    foreach (var property in ((JObject)value).Properties()
                        .OrderBy(property => property.Name, StringComparer.Ordinal))
                    {
                        if (!first)
                        {
                            output.Append(',');
                        }

                        first = false;
                        WriteString(property.Name, output);
                        output.Append(':');
                        WriteCanonical(property.Value, output);
                    }

                    output.Append('}');
                    break;
                }
            case JTokenType.Array:
                {
                    output.Append('[');
                    var first = true;
                    foreach (var child in value.Children())
                    {
                        if (!first)
                        {
                            output.Append(',');
                        }

                        first = false;
                        WriteCanonical(child, output);
                    }

                    output.Append(']');
                    break;
                }
            case JTokenType.String:
                WriteString((string)((JValue)value).Value!, output);
                break;
            case JTokenType.Integer:
                output.Append(Convert.ToInt64(
                    ((JValue)value).Value,
                    CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture));
                break;
            case JTokenType.Boolean:
                output.Append((bool)((JValue)value).Value! ? "true" : "false");
                break;
            case JTokenType.Null:
                output.Append("null");
                break;
            default:
                throw new InvalidDataException("Update JSON contains an unsupported value.");
        }
    }

    private static void WriteString(string value, StringBuilder output)
    {
        output.Append('"');
        foreach (var character in value)
        {
            switch (character)
            {
                case '"':
                    output.Append("\\\"");
                    break;
                case '\\':
                    output.Append("\\\\");
                    break;
                case '\b':
                    output.Append("\\b");
                    break;
                case '\f':
                    output.Append("\\f");
                    break;
                case '\n':
                    output.Append("\\n");
                    break;
                case '\r':
                    output.Append("\\r");
                    break;
                case '\t':
                    output.Append("\\t");
                    break;
                default:
                    if (character < 0x20 || character == 0x7f)
                    {
                        output.Append("\\u");
                        output.Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        output.Append(character);
                    }

                    break;
            }
        }

        output.Append('"');
    }

    [GeneratedRegex(
        "^2\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex ReleaseVersionPattern();

    [GeneratedRegex(
        "^(?:[0-9a-f]{40}|[0-9a-f]{64})$",
        RegexOptions.CultureInvariant)]
    private static partial Regex CommitPattern();

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();
}

internal sealed record ReleaseTrust(
    Uri BaseUri,
    string KeyId,
    string PublicKey,
    TimeSpan CheckInterval);

internal sealed record SignedEnvelope(JObject Payload, byte[] PayloadBytes);

internal sealed record LatestRelease(
    string Version,
    string SourceCommit,
    ManifestPointer Manifest);

internal sealed record ManifestPointer(string Path, string Sha256, int Size);

internal sealed record ReleaseManifest(
    string Version,
    string SourceCommit,
    IReadOnlyList<ReleaseArtifact> Artifacts);

internal sealed record ReleaseArtifact(
    string OS,
    string Arch,
    string Filename,
    string Path,
    string SignaturePath,
    string Sha256,
    long Size);

internal sealed record UpdateTarget(string OS, string Arch, string Filename);
