namespace Locker
{
    using Newtonsoft.Json;

    public class AppInfo
    {
        [JsonProperty("name")] public string Name { get; set; }
    }
}