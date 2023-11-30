namespace Locker
{
    using Newtonsoft.Json;

    public class SecretCreateOptions : BaseOptions
    {
        [JsonProperty("key")] public string Key { get; set; }

        [JsonProperty("value")] public string Value { get; set; }

        [JsonProperty("description")] public string Description { get; set; }

        [JsonProperty("environment_name")] public string EnvironemntName { get; set; } = null;
    }
}