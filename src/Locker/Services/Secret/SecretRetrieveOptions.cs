namespace Locker;

public class SecretRetrieveOptions : BaseOptions
{
    public string? EnvironmentName { get; set; }

    [Obsolete("Human CLI argument construction is not supported by protocol v1.")]
    public override string BuildOptions() => string.Empty;
}
