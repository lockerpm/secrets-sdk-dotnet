using System.Runtime.CompilerServices;

namespace LockerAdversarialCandidate;

internal static class ModuleInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        var sentinel = Environment.GetEnvironmentVariable(
            "LOCKER_RELEASE_VERIFIER_SENTINEL");
        if (!string.IsNullOrWhiteSpace(sentinel))
        {
            File.WriteAllText(sentinel, "candidate assembly executed");
        }
    }
}
