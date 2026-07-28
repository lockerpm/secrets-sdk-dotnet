using Locker;

using var client = LockerClientFactory.FromEnvironment();
if (args.Contains("--capabilities-only", StringComparer.Ordinal))
{
    await client.EnsureCapabilitiesAsync();
    Console.WriteLine("Locker CLI protocol v1 is available.");
    return;
}

var databasePassword = await client.Secrets.GetRequiredAsync("DATABASE_PASSWORD");
Console.WriteLine($"Loaded DATABASE_PASSWORD ({databasePassword.Length} characters).");
