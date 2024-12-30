namespace Locker
{
    using Newtonsoft.Json;

    public class SecretUpdateOptions : BaseOptions
    {
        [JsonProperty("key")] public string Key { get; set; }

        [JsonProperty("value")] public string Value { get; set; }

        [JsonProperty("description")] public string Description { get; set; }
        [JsonProperty("environment_name")] public string EnvironmentName { get; set; }

        public override string BuildOptions()
        {
            string cli = "";
            if (Key != null)
            {
                cli += $" --new-key {Key}";
            }

            if (Value != null)
            {
                cli += $" --new-value {Value}";
            }

            if (Description != null)
            {
                cli += $" --new-description {Description}";
            }

            if (EnvironmentName != null)
            {
                cli += $" --new-environment {EnvironmentName}";
            }

            return cli;
        }
    }
}