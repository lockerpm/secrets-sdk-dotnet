namespace Locker
{
    using Newtonsoft.Json;

    public class EnvironmentCreateOptions : BaseOptions
    {
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("external_url")] public string ExternalUrl { get; set; }

        [JsonProperty("description")] public string Description { get; set; }

        public override string BuildOptions()
        {
            string cli = $" --name  {Name} --url  {ExternalUrl} --description  {Description}";
            return cli;
        }
    }
}