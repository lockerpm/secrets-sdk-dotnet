using System.Security.Cryptography;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Locker.ReleaseVerifier;

internal static class Program
{
    private const int MaxJsonBytes = 1 << 20;
    private const int MaxTextBytes = 1 << 20;
    private const int MaxPackageBytes = 128 << 20;
    private const int MaxExpandedPackageBytes = 256 << 20;
    private const int MaxArchiveEntries = 64;

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private static readonly Regex SemVerPattern = new(
        "^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)" +
        "(?:-(?:0|[1-9][0-9]*|[0-9A-Za-z-]*[A-Za-z-][0-9A-Za-z-]*)" +
        "(?:\\.(?:0|[1-9][0-9]*|[0-9A-Za-z-]*[A-Za-z-][0-9A-Za-z-]*))*)?$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking
    );

    private static readonly Regex CorePropertiesPattern = new(
        "^package/services/metadata/core-properties/[0-9a-f]{32}\\.psmdcp$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking
    );

    private static readonly HashSet<string> TrustFields = new(StringComparer.Ordinal)
    {
        "base_url",
        "check_interval_seconds",
        "key_id",
        "public_key",
        "schema_version",
    };

    private static readonly HashSet<string> ArgumentFields = new(StringComparer.Ordinal)
    {
        "package",
        "public-key",
        "root",
        "tag",
        "version",
    };

    private static readonly HashSet<string> RequiredPackageEntries = new(StringComparer.Ordinal)
    {
        "_rels/.rels",
        "[Content_Types].xml",
        "LICENSE",
        "README.md",
        "lib/net8.0/Locker.dll",
        "lib/net8.0/Locker.xml",
        "lockersm.nuspec",
        "protocol/locker-rpc-errors.v1.json",
    };

    public static int Main(string[] args)
    {
        try
        {
            var options = ParseArguments(args);
            VerifyRelease(
                Path.GetFullPath(RequireOption(options, "root")),
                RequireOption(options, "version"),
                RequireOption(options, "tag"),
                RequireOption(options, "public-key"),
                Path.GetFullPath(RequireOption(options, "package"))
            );
            Console.WriteLine("Locker .NET release artifact verification passed.");
            return 0;
        }
        catch (ReleaseVerificationException exception)
        {
            Console.Error.WriteLine($"Release verification failed: {exception.Message}");
            return 1;
        }
        catch (Exception)
        {
            Console.Error.WriteLine("Release verification failed unexpectedly.");
            return 1;
        }
    }

    private static void VerifyRelease(
        string repositoryRoot,
        string version,
        string tag,
        string independentPublicKey,
        string packagePath
    )
    {
        if (!Directory.Exists(repositoryRoot))
        {
            throw new ReleaseVerificationException("repository root is unavailable");
        }

        var baseVersion = ReadUtf8Text(
            Path.Combine(repositoryRoot, "VERSION"),
            128,
            "VERSION"
        ).Trim();
        if (!SemVerPattern.IsMatch(baseVersion))
        {
            throw new ReleaseVerificationException("VERSION is not canonical SemVer");
        }
        if (!SemVerPattern.IsMatch(version))
        {
            throw new ReleaseVerificationException(
                "derived release version is not canonical SemVer"
            );
        }
        var baseParts = baseVersion
            .Split('.')
            .Select(
                part => int.Parse(
                    part,
                    System.Globalization.CultureInfo.InvariantCulture
                )
            )
            .ToArray();
        var releaseParts = version
            .Split('.')
            .Select(
                part => int.Parse(
                    part,
                    System.Globalization.CultureInfo.InvariantCulture
                )
            )
            .ToArray();
        if (
            releaseParts[0] != baseParts[0]
            || releaseParts[1] != baseParts[1]
            || releaseParts[2] < baseParts[2]
        )
        {
            throw new ReleaseVerificationException(
                "derived release version is outside the reviewed release line"
            );
        }
        if (!string.Equals(tag, $"v{version}", StringComparison.Ordinal))
        {
            throw new ReleaseVerificationException(
                "release tag does not match the derived release version"
            );
        }

        var project = ReadProjectMetadata(
            Path.Combine(repositoryRoot, "src", "Locker", "Locker.csproj")
        );
        if (
            !string.Equals(project.PackageId, "lockersm", StringComparison.Ordinal)
            || !string.Equals(project.Version, baseVersion, StringComparison.Ordinal)
            || !string.Equals(project.LicenseFile, "LICENSE", StringComparison.Ordinal)
            || !string.Equals(project.ReadmeFile, "README.md", StringComparison.Ordinal)
        )
        {
            throw new ReleaseVerificationException(
                "Locker.csproj package identity/version/license/readme is invalid"
            );
        }

        var licenseBytes = ReadRegularBytes(
            Path.Combine(repositoryRoot, "LICENSE"),
            MaxTextBytes,
            "LICENSE"
        );
        RequireNonWhitespaceUtf8(licenseBytes, "LICENSE");
        var readmeBytes = ReadRegularBytes(
            Path.Combine(repositoryRoot, "README.md"),
            MaxTextBytes,
            "README.md"
        );
        RequireNonWhitespaceUtf8(readmeBytes, "README.md");

        var trustPath = Path.Combine(
            repositoryRoot,
            "src",
            "Locker",
            "locker-cli-release.json"
        );
        var trustBytes = ReadRegularBytes(
            trustPath,
            MaxJsonBytes,
            "SDK CLI release trust"
        );
        var trust = ReadTrust(trustBytes);
        ValidateTrust(trust, independentPublicKey, trustBytes);
        var errorCatalogBytes = ReadRegularBytes(
            Path.Combine(
                repositoryRoot,
                "src",
                "Locker",
                "Protocol",
                "locker-rpc-errors.v1.json"
            ),
            MaxJsonBytes,
            "SDK RPC error catalog"
        );
        RequireNonWhitespaceUtf8(errorCatalogBytes, "SDK RPC error catalog");

        VerifyPackage(
            packagePath,
            version,
            licenseBytes,
            readmeBytes,
            trustBytes,
            errorCatalogBytes
        );
    }

    private static Dictionary<string, string> ParseArguments(string[] args)
    {
        if (args.Length == 0 || args.Length % 2 != 0)
        {
            throw new ReleaseVerificationException("release verifier arguments are invalid");
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index += 2)
        {
            var name = args[index];
            if (!name.StartsWith("--", StringComparison.Ordinal) || name.Length == 2)
            {
                throw new ReleaseVerificationException(
                    "release verifier arguments are invalid"
                );
            }
            if (!result.TryAdd(name[2..], args[index + 1]))
            {
                throw new ReleaseVerificationException(
                    "release verifier contains a duplicate argument"
                );
            }
        }

        if (!ArgumentFields.SetEquals(result.Keys))
        {
            throw new ReleaseVerificationException(
                "release verifier argument fields do not match the contract"
            );
        }
        return result;
    }

    private static string RequireOption(
        Dictionary<string, string> options,
        string name
    )
    {
        if (
            !options.TryGetValue(name, out var value)
            || string.IsNullOrWhiteSpace(value)
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal)
        )
        {
            throw new ReleaseVerificationException(
                $"release verifier option --{name} is empty"
            );
        }
        return value;
    }

    private static ProjectMetadata ReadProjectMetadata(string projectPath)
    {
        var bytes = ReadRegularBytes(projectPath, MaxTextBytes, "Locker.csproj");
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            MaxCharactersInDocument = MaxTextBytes,
            XmlResolver = null,
        };
        XDocument document;
        try
        {
            using var input = new MemoryStream(bytes, writable: false);
            using var reader = XmlReader.Create(input, settings);
            document = XDocument.Load(reader, LoadOptions.None);
        }
        catch (XmlException exception)
        {
            throw new ReleaseVerificationException(
                "Locker.csproj is not safe XML",
                exception
            );
        }

        return new ProjectMetadata(
            ReadSingleElement(document, "PackageId", "Locker.csproj"),
            ReadSingleElement(document, "Version", "Locker.csproj"),
            ReadSingleElement(document, "PackageLicenseFile", "Locker.csproj"),
            ReadSingleElement(document, "PackageReadmeFile", "Locker.csproj")
        );
    }

    private static string ReadSingleElement(
        XDocument document,
        string localName,
        string label
    )
    {
        var elements = document
            .Descendants()
            .Where(element => element.Name.LocalName == localName)
            .ToArray();
        if (elements.Length != 1)
        {
            throw new ReleaseVerificationException(
                $"{label} must contain exactly one {localName}"
            );
        }
        return RequireString(elements[0].Value, $"{label} {localName}");
    }

    private static ReleaseTrust ReadTrust(byte[] bytes)
    {
        using var document = ReadStrictJson(
            bytes,
            TrustFields,
            "SDK CLI release trust"
        );
        var root = document.RootElement;
        return new ReleaseTrust(
            RequireInt32(root, "schema_version", "SDK CLI release trust"),
            RequireJsonString(root, "base_url", "SDK CLI release trust"),
            RequireJsonString(root, "key_id", "SDK CLI release trust"),
            RequireJsonString(root, "public_key", "SDK CLI release trust"),
            RequireInt64(
                root,
                "check_interval_seconds",
                "SDK CLI release trust"
            )
        );
    }

    private static JsonDocument ReadStrictJson(
        byte[] bytes,
        HashSet<string> expectedFields,
        string label
    )
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(
                bytes,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 128,
                }
            );
        }
        catch (JsonException exception)
        {
            throw new ReleaseVerificationException(
                $"{label} is not strict JSON",
                exception
            );
        }
        try
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ReleaseVerificationException($"{label} must be an object");
            }
            RejectDuplicateJsonFields(document.RootElement, label);
            var fields = document.RootElement
                .EnumerateObject()
                .Select(property => property.Name)
                .ToHashSet(StringComparer.Ordinal);
            if (!expectedFields.SetEquals(fields))
            {
                throw new ReleaseVerificationException(
                    $"{label} fields do not match the release contract"
                );
            }
            return document;
        }
        catch
        {
            document.Dispose();
            throw;
        }
    }

    private static void RejectDuplicateJsonFields(JsonElement element, string label)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                {
                    var fields = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var property in element.EnumerateObject())
                    {
                        if (!fields.Add(property.Name))
                        {
                            throw new ReleaseVerificationException(
                                $"{label} contains duplicate field {property.Name}"
                            );
                        }
                        RejectDuplicateJsonFields(property.Value, label);
                    }
                    break;
                }
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    RejectDuplicateJsonFields(item, label);
                }
                break;
        }
    }

    private static int RequireInt32(JsonElement root, string name, string label)
    {
        var value = root.GetProperty(name);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var result))
        {
            throw new ReleaseVerificationException($"{label}.{name} must be an integer");
        }
        return result;
    }

    private static long RequireInt64(JsonElement root, string name, string label)
    {
        var value = root.GetProperty(name);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var result))
        {
            throw new ReleaseVerificationException($"{label}.{name} must be an integer");
        }
        return result;
    }

    private static string RequireJsonString(
        JsonElement root,
        string name,
        string label
    )
    {
        var value = root.GetProperty(name);
        if (value.ValueKind != JsonValueKind.String)
        {
            throw new ReleaseVerificationException($"{label}.{name} must be a string");
        }
        return RequireString(value.GetString(), $"{label}.{name}");
    }

    private static string RequireString(string? value, string label)
    {
        if (
            string.IsNullOrWhiteSpace(value)
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal)
        )
        {
            throw new ReleaseVerificationException(
                $"{label} must be a nonempty trimmed string"
            );
        }
        return value;
    }

    private static void ValidateTrust(
        ReleaseTrust trust,
        string independentPublicKey,
        byte[] sourceBytes
    )
    {
        if (
            trust.SchemaVersion != 2
            || !string.Equals(
                trust.BaseUrl,
                "https://files.locker.io/cli/releases/",
                StringComparison.Ordinal
            )
            || !string.Equals(
                trust.KeyId,
                "locker-cli-release-v1",
                StringComparison.Ordinal
            )
            || trust.CheckIntervalSeconds != 21_600
        )
        {
            throw new ReleaseVerificationException(
                "SDK CLI release trust coordinates are invalid"
            );
        }

        var embedded = DecodeCanonicalBase64Url(
            trust.PublicKey,
            "embedded Locker CLI release public key"
        );
        var independent = DecodeCanonicalBase64Url(
            independentPublicKey,
            "protected Locker CLI release public key"
        );
        try
        {
            if (
                !string.Equals(
                    trust.PublicKey,
                    independentPublicKey,
                    StringComparison.Ordinal
                )
                || !CryptographicOperations.FixedTimeEquals(embedded, independent)
            )
            {
                throw new ReleaseVerificationException(
                    "embedded Locker CLI release key does not match the protected independent key"
                );
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(embedded);
            CryptographicOperations.ZeroMemory(independent);
        }

        var expectedSource = StrictUtf8.GetBytes(
            "{\"base_url\":\"https://files.locker.io/cli/releases/\"," +
            "\"check_interval_seconds\":21600," +
            "\"key_id\":\"locker-cli-release-v1\"," +
            $"\"public_key\":\"{trust.PublicKey}\"," +
            "\"schema_version\":2}\n"
        );
        if (!sourceBytes.AsSpan().SequenceEqual(expectedSource))
        {
            throw new ReleaseVerificationException(
                "SDK CLI release trust must be canonical JSON followed by one LF"
            );
        }
    }

    private static byte[] DecodeCanonicalBase64Url(string value, string label)
    {
        if (
            value.Length < 1
            || value.Any(
                character =>
                    !char.IsAsciiLetterOrDigit(character)
                    && character is not ('-' or '_')
            )
            || value.Contains('=')
        )
        {
            throw new ReleaseVerificationException(
                $"{label} is not unpadded base64url"
            );
        }

        byte[] decoded;
        try
        {
            var padding = (4 - value.Length % 4) % 4;
            decoded = Convert.FromBase64String(
                value.Replace('-', '+').Replace('_', '/')
                + new string('=', padding)
            );
        }
        catch (FormatException exception)
        {
            throw new ReleaseVerificationException(
                $"{label} is not unpadded base64url",
                exception
            );
        }

        var roundTrip = Convert.ToBase64String(decoded)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        if (decoded.Length != 32 || !string.Equals(roundTrip, value, StringComparison.Ordinal))
        {
            CryptographicOperations.ZeroMemory(decoded);
            throw new ReleaseVerificationException(
                $"{label} must encode exactly 32 raw Ed25519 bytes"
            );
        }
        return decoded;
    }

    private static void VerifyPackage(
        string packagePath,
        string version,
        byte[] licenseBytes,
        byte[] readmeBytes,
        byte[] trustBytes,
        byte[] errorCatalogBytes
    )
    {
        var expectedName = $"lockersm.{version}.nupkg";
        if (
            !string.Equals(
                Path.GetFileName(packagePath),
                expectedName,
                StringComparison.Ordinal
            )
        )
        {
            throw new ReleaseVerificationException(
                $"NuGet package filename must be {expectedName}"
            );
        }
        _ = ReadRegularFileInfo(packagePath, MaxPackageBytes, "NuGet package");

        try
        {
            using var input = new FileStream(
                packagePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 64 * 1024,
                FileOptions.SequentialScan
            );
            using var archive = new ZipArchive(
                input,
                ZipArchiveMode.Read,
                leaveOpen: false,
                entryNameEncoding: StrictUtf8
            );
            VerifyPackageEntries(
                archive,
                version,
                licenseBytes,
                readmeBytes,
                trustBytes,
                errorCatalogBytes
            );
        }
        catch (
            Exception exception
        ) when (
            exception is InvalidDataException
            or IOException
            or UnauthorizedAccessException
        )
        {
            throw new ReleaseVerificationException(
                "NuGet package is not a safe readable archive",
                exception
            );
        }
    }

    private static void VerifyPackageEntries(
        ZipArchive archive,
        string version,
        byte[] licenseBytes,
        byte[] readmeBytes,
        byte[] trustBytes,
        byte[] errorCatalogBytes
    )
    {
        if (archive.Entries.Count < 1 || archive.Entries.Count > MaxArchiveEntries)
        {
            throw new ReleaseVerificationException(
                "NuGet package entry count is invalid"
            );
        }

        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        long expandedSize = 0;
        foreach (var entry in archive.Entries)
        {
            ValidateArchivePath(entry.FullName);
            if (!entries.TryAdd(entry.FullName, entry))
            {
                throw new ReleaseVerificationException(
                    $"NuGet package contains duplicate path {entry.FullName}"
                );
            }
            if (entry.Length < 0 || entry.Length > MaxPackageBytes)
            {
                throw new ReleaseVerificationException(
                    $"NuGet package entry is oversized: {entry.FullName}"
                );
            }
            expandedSize = checked(expandedSize + entry.Length);
            if (expandedSize > MaxExpandedPackageBytes)
            {
                throw new ReleaseVerificationException(
                    "expanded NuGet package exceeds the size limit"
                );
            }
        }

        var coreProperties = entries.Keys
            .Where(name => CorePropertiesPattern.IsMatch(name))
            .ToArray();
        if (
            coreProperties.Length != 1
            || entries.Count != RequiredPackageEntries.Count + 1
            || !RequiredPackageEntries.All(entries.ContainsKey)
            || entries.Keys.Any(
                name =>
                    !RequiredPackageEntries.Contains(name)
                    && !CorePropertiesPattern.IsMatch(name)
            )
        )
        {
            throw new ReleaseVerificationException(
                "NuGet package payload does not match the exact allowlist"
            );
        }

        RequireEqual(
            ReadArchiveEntry(entries["LICENSE"], MaxTextBytes),
            licenseBytes,
            "NuGet LICENSE differs from source"
        );
        RequireEqual(
            ReadArchiveEntry(entries["README.md"], MaxTextBytes),
            readmeBytes,
            "NuGet README differs from source"
        );
        RequireEqual(
            ReadArchiveEntry(
                entries["protocol/locker-rpc-errors.v1.json"],
                MaxJsonBytes
            ),
            errorCatalogBytes,
            "NuGet RPC error catalog differs from source"
        );
        VerifyNuspec(
            ReadArchiveEntry(entries["lockersm.nuspec"], MaxTextBytes),
            version
        );
        VerifyEmbeddedTrust(
            ReadArchiveEntry(entries["lib/net8.0/Locker.dll"], MaxPackageBytes),
            trustBytes
        );
    }

    private static void ValidateArchivePath(string name)
    {
        if (
            string.IsNullOrEmpty(name)
            || name.StartsWith('/')
            || name.Contains('\\')
            || name.Split('/').Any(
                part =>
                    string.IsNullOrEmpty(part)
                    || string.Equals(part, ".", StringComparison.Ordinal)
                    || string.Equals(part, "..", StringComparison.Ordinal)
            )
        )
        {
            throw new ReleaseVerificationException(
                $"NuGet package contains unsafe path {name}"
            );
        }
    }

    internal static byte[] ReadArchiveEntry(ZipArchiveEntry entry, int maximum)
    {
        if (entry.Length < 1 || entry.Length > maximum)
        {
            throw new ReleaseVerificationException(
                $"NuGet package entry has invalid size: {entry.FullName}"
            );
        }
        using var input = entry.Open();
        return ReadBoundedArchiveStream(
            input,
            entry.Length,
            maximum,
            entry.FullName
        );
    }

    internal static byte[] ReadBoundedArchiveStream(
        Stream input,
        long declaredLength,
        int maximum,
        string entryName
    )
    {
        if (declaredLength < 1 || declaredLength > maximum)
        {
            throw new ReleaseVerificationException(
                $"NuGet package entry has invalid size: {entryName}"
            );
        }

        using var output = new MemoryStream((int)declaredLength);
        var buffer = new byte[Math.Min(81920, maximum)];
        while (true)
        {
            var read = input.Read(buffer, 0, buffer.Length);
            if (read == 0)
            {
                break;
            }
            if (output.Length + read > declaredLength
                || output.Length + read > maximum)
            {
                throw new ReleaseVerificationException(
                    $"NuGet package entry exceeded its declared size: {entryName}"
                );
            }
            output.Write(buffer, 0, read);
        }
        if (output.Length != declaredLength)
        {
            throw new ReleaseVerificationException(
                $"NuGet package entry is truncated: {entryName}"
            );
        }
        return output.ToArray();
    }

    internal static void VerifyNuspec(byte[] bytes, string version)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            MaxCharactersInDocument = MaxTextBytes,
            XmlResolver = null,
        };
        XDocument document;
        try
        {
            using var input = new MemoryStream(bytes, writable: false);
            using var reader = XmlReader.Create(input, settings);
            document = XDocument.Load(reader, LoadOptions.None);
        }
        catch (XmlException exception)
        {
            throw new ReleaseVerificationException(
                "NuGet nuspec is not safe XML",
                exception
            );
        }

        var packageNamespace = XNamespace.Get(
            "http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"
        );
        var root = document.Root;
        var metadataElements = root?.Elements(packageNamespace + "metadata").ToArray()
            ?? [];
        if (
            root?.Name != packageNamespace + "package"
            || metadataElements.Length != 1
        )
        {
            throw new ReleaseVerificationException(
                "NuGet nuspec package metadata is invalid"
            );
        }
        var metadata = metadataElements[0];
        var id = ReadSingleChild(metadata, "id");
        var packageVersion = ReadSingleChild(metadata, "version");
        var readme = ReadSingleChild(metadata, "readme");
        var licenseElements = metadata
            .Elements(metadata.Name.Namespace + "license")
            .ToArray();
        if (
            licenseElements.Length != 1
            || !string.Equals(
                licenseElements[0].Attribute("type")?.Value,
                "file",
                StringComparison.Ordinal
            )
            || !string.Equals(
                RequireString(licenseElements[0].Value, "NuGet nuspec license"),
                "LICENSE",
                StringComparison.Ordinal
            )
            || !string.Equals(id, "lockersm", StringComparison.Ordinal)
            || !string.Equals(packageVersion, version, StringComparison.Ordinal)
            || !string.Equals(readme, "README.md", StringComparison.Ordinal)
        )
        {
            throw new ReleaseVerificationException(
                "NuGet nuspec identity/version/license/readme is invalid"
            );
        }

        VerifyNuspecDependencies(metadata);
    }

    private static void VerifyNuspecDependencies(XElement metadata)
    {
        var containers = metadata
            .Elements(metadata.Name.Namespace + "dependencies")
            .ToArray();
        if (containers.Length != 1)
        {
            throw new ReleaseVerificationException(
                "NuGet nuspec must contain one reviewed dependency graph"
            );
        }

        var groups = containers[0].Elements().ToArray();
        if (
            groups.Length != 1
            || groups[0].Name != metadata.Name.Namespace + "group"
            || !HasExactAttributes(
                groups[0],
                ("targetFramework", "net8.0")
            )
        )
        {
            throw new ReleaseVerificationException(
                "NuGet nuspec dependency framework is invalid"
            );
        }

        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["BouncyCastle.Cryptography"] = "2.6.2",
            ["Newtonsoft.Json"] = "13.0.4",
        };
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var dependencies = groups[0].Elements().ToArray();
        if (dependencies.Length != expected.Count)
        {
            throw new ReleaseVerificationException(
                "NuGet nuspec dependency count is invalid"
            );
        }
        foreach (var dependency in dependencies)
        {
            var id = dependency.Attribute("id")?.Value;
            var version = dependency.Attribute("version")?.Value;
            if (
                dependency.Name.LocalName != "dependency"
                || dependency.Name.Namespace != metadata.Name.Namespace
                || id is null
                || version is null
                || !expected.TryGetValue(id, out var expectedVersion)
                || !seen.Add(id)
                || !string.Equals(version, expectedVersion, StringComparison.Ordinal)
                || !HasExactAttributes(
                    dependency,
                    ("id", id),
                    ("version", expectedVersion),
                    ("exclude", "Build,Analyzers")
                )
                || dependency.HasElements
                || !string.IsNullOrWhiteSpace(dependency.Value)
            )
            {
                throw new ReleaseVerificationException(
                    "NuGet nuspec contains an unreviewed dependency"
                );
            }
        }
    }

    private static bool HasExactAttributes(
        XElement element,
        params (string Name, string Value)[] expected
    )
    {
        var attributes = element
            .Attributes()
            .Where(attribute => !attribute.IsNamespaceDeclaration)
            .ToArray();
        return attributes.Length == expected.Length
            && attributes.All(
                attribute => attribute.Name.Namespace == XNamespace.None
            )
            && expected.All(
                pair =>
                    attributes.SingleOrDefault(
                        attribute => attribute.Name.LocalName == pair.Name
                    )?.Value == pair.Value
            );
    }

    private static string ReadSingleChild(XElement parent, string localName)
    {
        var elements = parent
            .Elements(parent.Name.Namespace + localName)
            .ToArray();
        if (elements.Length != 1)
        {
            throw new ReleaseVerificationException(
                $"NuGet nuspec must contain exactly one {localName}"
            );
        }
        return RequireString(elements[0].Value, $"NuGet nuspec {localName}");
    }

    internal static void VerifyEmbeddedTrust(byte[] assemblyBytes, byte[] trustBytes)
    {
        const string resourceName = "Locker.locker-cli-release.json";
        if (
            assemblyBytes.Length is < 1 or > MaxPackageBytes
            || trustBytes.Length is < 1 or > MaxJsonBytes
        )
        {
            throw new ReleaseVerificationException(
                "Locker.dll or CLI release trust resource exceeds its input bound"
            );
        }
        try
        {
            using var assemblyInput = new MemoryStream(assemblyBytes, writable: false);
            using var peReader = new PEReader(
                assemblyInput,
                PEStreamOptions.PrefetchEntireImage
                    | PEStreamOptions.PrefetchMetadata
            );
            if (!peReader.HasMetadata)
            {
                throw new ReleaseVerificationException(
                    "Locker.dll is missing its canonical CLI release trust resource"
                );
            }

            var metadata = peReader.GetMetadataReader();
            var matchingResources = metadata.ManifestResources
                .Select(metadata.GetManifestResource)
                .Where(
                    resource =>
                        string.Equals(
                            metadata.GetString(resource.Name),
                            resourceName,
                            StringComparison.Ordinal
                        )
                )
                .ToArray();
            if (
                matchingResources.Length != 1
                || !matchingResources[0].Implementation.IsNil
            )
            {
                throw new ReleaseVerificationException(
                    "Locker.dll is missing its canonical embedded CLI release trust resource"
                );
            }

            var resourcesDirectory =
                peReader.PEHeaders.CorHeader?.ResourcesDirectory
                ?? throw new ReleaseVerificationException(
                    "Locker.dll managed resource directory is missing"
                );
            var offset = checked((int)matchingResources[0].Offset);
            if (
                resourcesDirectory.RelativeVirtualAddress == 0
                || resourcesDirectory.Size < sizeof(int)
                || offset < 0
                || offset > resourcesDirectory.Size - sizeof(int)
            )
            {
                throw new ReleaseVerificationException(
                    "Locker.dll CLI release trust resource offset is invalid"
                );
            }

            var resourceBlock = peReader.GetSectionData(
                resourcesDirectory.RelativeVirtualAddress
            );
            if (resourcesDirectory.Size > resourceBlock.Length)
            {
                throw new ReleaseVerificationException(
                    "Locker.dll managed resource directory exceeds its PE section"
                );
            }
            var resourceReader = resourceBlock.GetReader(
                offset,
                resourcesDirectory.Size - offset
            );
            var resourceLength = resourceReader.ReadInt32();
            if (
                resourceLength < 1
                || resourceLength > MaxJsonBytes
                || resourceLength > resourceReader.RemainingBytes
            )
            {
                throw new ReleaseVerificationException(
                    "Locker.dll CLI release trust resource size is invalid"
                );
            }

            RequireEqual(
                resourceReader.ReadBytes(resourceLength),
                trustBytes,
                "Locker.dll CLI release trust resource differs from source"
            );
        }
        catch (ReleaseVerificationException)
        {
            throw;
        }
        catch (
            Exception exception
        ) when (
            exception is BadImageFormatException
            or ArgumentException
            or IOException
            or InvalidOperationException
            or OverflowException
        )
        {
            throw new ReleaseVerificationException(
                "Locker.dll CLI release trust resource is unreadable",
                exception
            );
        }
    }

    private static FileInfo ReadRegularFileInfo(
        string path,
        int maximum,
        string label
    )
    {
        FileInfo info;
        try
        {
            info = new FileInfo(path);
            info.Refresh();
            if (
                !info.Exists
                || (info.Attributes & FileAttributes.Directory) != 0
                || (info.Attributes & FileAttributes.ReparsePoint) != 0
                || info.LinkTarget is not null
                || info.Length < 1
                || info.Length > maximum
            )
            {
                throw new ReleaseVerificationException(
                    $"{label} must be a bounded regular non-symlink file"
                );
            }
        }
        catch (
            Exception exception
        ) when (
            exception is IOException
            or UnauthorizedAccessException
            or System.Security.SecurityException
        )
        {
            throw new ReleaseVerificationException($"{label} is unavailable", exception);
        }
        return info;
    }

    private static byte[] ReadRegularBytes(string path, int maximum, string label)
    {
        var info = ReadRegularFileInfo(path, maximum, label);
        try
        {
            var bytes = File.ReadAllBytes(info.FullName);
            if (bytes.Length < 1 || bytes.Length > maximum)
            {
                throw new ReleaseVerificationException(
                    $"{label} changed while it was being read"
                );
            }
            return bytes;
        }
        catch (IOException exception)
        {
            throw new ReleaseVerificationException($"{label} cannot be read", exception);
        }
    }

    private static string ReadUtf8Text(string path, int maximum, string label)
    {
        try
        {
            return StrictUtf8.GetString(ReadRegularBytes(path, maximum, label));
        }
        catch (DecoderFallbackException exception)
        {
            throw new ReleaseVerificationException($"{label} is not UTF-8", exception);
        }
    }

    private static void RequireNonWhitespaceUtf8(byte[] bytes, string label)
    {
        string text;
        try
        {
            text = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new ReleaseVerificationException($"{label} is not UTF-8", exception);
        }
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ReleaseVerificationException($"{label} is empty");
        }
    }

    private static void RequireEqual(byte[] actual, byte[] expected, string message)
    {
        if (!actual.AsSpan().SequenceEqual(expected))
        {
            throw new ReleaseVerificationException(message);
        }
    }

    private sealed record ProjectMetadata(
        string PackageId,
        string Version,
        string LicenseFile,
        string ReadmeFile
    );

    private sealed record ReleaseTrust(
        int SchemaVersion,
        string BaseUrl,
        string KeyId,
        string PublicKey,
        long CheckIntervalSeconds
    );
}

internal sealed class ReleaseVerificationException : Exception
{
    public ReleaseVerificationException(string message)
        : base(message) { }

    public ReleaseVerificationException(string message, Exception innerException)
        : base(message, innerException) { }
}
