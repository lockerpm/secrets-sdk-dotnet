namespace Locker
{
    using Newtonsoft.Json;

    public class SecretCreateOptions : BaseOptions
    {
        [JsonProperty("key")] public string Key { get; set; }

        [JsonProperty("value")] public string Value { get; set; }

        [JsonProperty("description")] public string Description { get; set; }

        [JsonProperty("environment_name")] public string EnvironmentName { get; set; } = null;

        public override string BuildOptions()
        {
            string cli = $" --key {Key} --value {Value} --description {Description}";
            if (EnvironmentName != null)
            {
                cli += $" --environment {EnvironmentName}";
            }

            return cli;
        }
    }
}