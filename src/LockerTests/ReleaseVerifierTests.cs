using Locker;
using System.Text;
using Xunit;

namespace LockerTests;

public sealed class ReleaseVerifierTests
{
    [Fact]
    public void EmbeddedTrustInspectionNeverExecutesCandidateAssembly()
    {
        using var temporary = new TemporaryDirectory();
        var sentinel = Path.Combine(temporary.Path, "module-initializer-ran");
        var original = System.Environment.GetEnvironmentVariable(
            "LOCKER_RELEASE_VERIFIER_SENTINEL");
        try
        {
            Assert.DoesNotContain(
                AppDomain.CurrentDomain.GetAssemblies(),
                assembly =>
                    assembly.GetName().Name == "Locker.AdversarialCandidate");
            System.Environment.SetEnvironmentVariable(
                "LOCKER_RELEASE_VERIFIER_SENTINEL",
                sentinel);
            var candidate = File.ReadAllBytes(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "Locker.AdversarialCandidate.dll"));
            using var trustedResource = typeof(LockerCliInstaller).Assembly
                .GetManifestResourceStream("Locker.locker-cli-release.json");
            Assert.NotNull(trustedResource);
            using var trustedBytes = new MemoryStream();
            trustedResource.CopyTo(trustedBytes);

            Locker.ReleaseVerifier.Program.VerifyEmbeddedTrust(
                candidate,
                trustedBytes.ToArray());

            Assert.False(
                File.Exists(sentinel),
                "Release verification executed candidate package code.");
            Assert.DoesNotContain(
                AppDomain.CurrentDomain.GetAssemblies(),
                assembly =>
                    assembly.GetName().Name == "Locker.AdversarialCandidate");
        }
        finally
        {
            System.Environment.SetEnvironmentVariable(
                "LOCKER_RELEASE_VERIFIER_SENTINEL",
                original);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(63)]
    public void EmbeddedTrustInspectionRejectsMalformedCandidateImages(int size)
    {
        var candidate = new byte[size];
        var trust = new byte[] { (byte)'{', (byte)'}', (byte)'\n' };

        Assert.Throws<Locker.ReleaseVerifier.ReleaseVerificationException>(
            () => Locker.ReleaseVerifier.Program.VerifyEmbeddedTrust(
                candidate,
                trust));
    }

    [Fact]
    public void ArchiveEntryReadStopsWhenInflatedDataExceedsDeclaredSize()
    {
        var payload = Enumerable.Repeat((byte)'A', 1024 * 1024).ToArray();
        using var candidate = new MemoryStream(payload, writable: false);
        Assert.Throws<Locker.ReleaseVerifier.ReleaseVerificationException>(
            () => Locker.ReleaseVerifier.Program.ReadBoundedArchiveStream(
                candidate,
                declaredLength: 1,
                maximum: 1024,
                entryName: "payload"));
    }

    [Fact]
    public void NuspecDependencyAllowlistRejectsInjectedPackage()
    {
        const string reviewedDependencies = """
            <dependency id="BouncyCastle.Cryptography" version="2.6.2" exclude="Build,Analyzers" />
            <dependency id="Newtonsoft.Json" version="13.0.4" exclude="Build,Analyzers" />
            """;
        var valid = Nuspec(reviewedDependencies);
        Locker.ReleaseVerifier.Program.VerifyNuspec(valid, "2.0.0");

        var injected = Nuspec(
            reviewedDependencies
                + """

                  <dependency id="Attacker.Package" version="1.0.0" exclude="Build,Analyzers" />
                """);
        Assert.Throws<Locker.ReleaseVerifier.ReleaseVerificationException>(
            () => Locker.ReleaseVerifier.Program.VerifyNuspec(
                injected,
                "2.0.0"));
    }

    private static byte[] Nuspec(string dependencies) =>
        Encoding.UTF8.GetBytes(
            $$"""
              <?xml version="1.0" encoding="utf-8"?>
              <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
                <metadata>
                  <id>lockersm</id>
                  <version>2.0.0</version>
                  <license type="file">LICENSE</license>
                  <readme>README.md</readme>
                  <dependencies>
                    <group targetFramework="net8.0">
                      {{dependencies}}
                    </group>
                  </dependencies>
                </metadata>
              </package>
              """);

    private sealed class TemporaryDirectory : IDisposable
    {
        internal TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"locker-release-verifier-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
