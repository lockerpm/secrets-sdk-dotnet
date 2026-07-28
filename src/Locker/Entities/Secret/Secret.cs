using Newtonsoft.Json;

namespace Locker;

public class Secret : LockerEntity<Secret>, IHasId, IHasObject
{
    [JsonProperty("object", Required = Required.Always)]
    public string Object { get; set; } = "secret";

    [JsonProperty("id", Required = Required.Always)]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("creation_date", Required = Required.Always)]
    public double CreationDate { get; set; }

    [JsonProperty("revision_date", Required = Required.Always)]
    public double RevisionDate { get; set; }

    [JsonProperty("updated_date", Required = Required.AllowNull)]
    public double? UpdatedDate { get; set; }

    [JsonProperty("deleted_date", Required = Required.AllowNull)]
    public double? DeletedDate { get; set; }

    [JsonProperty("last_use_date", Required = Required.AllowNull)]
    public double? LastUseDate { get; set; }

    [JsonProperty("project_id", Required = Required.Always)]
    public long ProjectId { get; set; }

    [JsonProperty("environment_id", Required = Required.AllowNull)]
    public string? EnvironmentId { get; set; }

    [JsonProperty("environment_name", Required = Required.AllowNull)]
    public string? EnvironmentName { get; set; }

    [JsonProperty("key", Required = Required.Always)]
    public string Key { get; set; } = string.Empty;

    [JsonProperty("value", Required = Required.Always)]
    public string Value { get; set; } = string.Empty;

    [JsonProperty("description", Required = Required.Always)]
    public string Description { get; set; } = string.Empty;

    [JsonIgnore]
    [Obsolete("Protocol v1 returns key, value, and description directly.")]
    public SecretData? Data { get; set; }

    [JsonIgnore]
    [Obsolete("Protocol v1 deliberately excludes internal hashes.")]
    public string? Hash { get; set; }

    public override string ToString() => $"<Locker.Secret id={Id} key={Key}>";
}
