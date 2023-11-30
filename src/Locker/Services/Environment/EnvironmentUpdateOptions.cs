namespace Locker
{
    using Newtonsoft.Json;

    public class EnvironmentUpdateOptions : BaseOptions
    {
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("external_url")] public string ExternalUrl { get; set; }
        [JsonProperty("description")] public string Description { get; set; }
    }
}