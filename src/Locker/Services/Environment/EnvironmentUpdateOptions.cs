using Newtonsoft.Json;

namespace Locker;

public class EnvironmentUpdateOptions : BaseOptions
{
    [JsonProperty("name")]
    public string? Name { get; set; }

    [JsonProperty("external_url")]
    public string? ExternalUrl { get; set; }

    [JsonProperty("description")]
    public string? Description { get; set; }

    [Obsolete("Human CLI argument construction is not supported by protocol v1.")]
    public override string BuildOptions() => string.Empty;
}
