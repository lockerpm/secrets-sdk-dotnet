using Newtonsoft.Json;

namespace Locker;

public class SecretUpdateOptions : BaseOptions
{
    [JsonProperty("key")]
    public string? Key { get; set; }

    [JsonProperty("value")]
    public string? Value { get; set; }

    [JsonProperty("description")]
    public string? Description { get; set; }

    [JsonProperty("environment")]
    public string? EnvironmentName { get; set; }

    [JsonIgnore]
    public bool ClearEnvironment { get; set; }

    [Obsolete("Human CLI argument construction is not supported by protocol v1.")]
    public override string BuildOptions() => string.Empty;
}
