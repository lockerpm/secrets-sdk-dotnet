using Newtonsoft.Json;

namespace Locker;

public class Environment : LockerEntity<Environment>, IHasId, IHasObject
{
    [JsonProperty("object", Required = Required.Always)]
    public string Object { get; set; } = "environment";

    [JsonProperty("id", Required = Required.Always)]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("name", Required = Required.Always)]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("external_url", Required = Required.Always)]
    public string ExternalUrl { get; set; } = string.Empty;

    [JsonProperty("description", Required = Required.Always)]
    public string Description { get; set; } = string.Empty;

    [JsonProperty("creation_date", Required = Required.Always)]
    public double CreationDate { get; set; }

    [JsonProperty("revision_date", Required = Required.Always)]
    public double RevisionDate { get; set; }

    [JsonProperty("updated_date", Required = Required.AllowNull)]
    public double? UpdatedDate { get; set; }

    [JsonProperty("project_id", Required = Required.Always)]
    public long ProjectId { get; set; }

    [JsonIgnore]
    [Obsolete("Protocol v1 deliberately excludes internal hashes.")]
    public string? Hash { get; set; }
}
