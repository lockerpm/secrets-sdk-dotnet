namespace Locker
{
    using Newtonsoft.Json;

    public class EnvironmentUpdateOptions : BaseOptions
    {
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("external_url")] public string ExternalUrl { get; set; }
        [JsonProperty("description")] public string Description { get; set; }

        public override string BuildOptions()
        {
            string cli = "";
            if (Name != null)
            {
                cli += $" --new-name {Name}";
            }

            if (ExternalUrl != null)
            {
                cli += $" --new-url {ExternalUrl}";
            }

            if (Description != null)
            {
                cli += $" --new-description {Description}";
            }

            return cli;
        }
    }
}