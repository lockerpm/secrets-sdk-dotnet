namespace LockerExample
{
    using Locker;

    public class SecretExample
    {
        private static readonly SecretService Service = new Locker.SecretService();

        public static LockerList<Secret> ListSecret()
        {
            var options = new Locker.SecretListOptions()
            {
            };
            var requestOption = new RequestOptions()
            {
                // AccessKey = "HTVRNF70FIVK:BvafRVekop1I8gSaw6DY6LZLHXp+dcdjG5VTKNQi6LA="
            };

            var secrets = Service.List(options, requestOption);

            return secrets;
        }

        public static string GetSecret(string id, string defaultValue = "", string environmentName = "")
        {
            var options = new Locker.SecretRetrieveOptions();

            string value = Service.Get(id: id, environmentName: environmentName, defaultValue: defaultValue,
                retrieveOptions: options);
            Console.WriteLine();
            return value;
        }

        public static Secret CreateSecret(string key, string value = null, string environmentName = "",
            string description = "")
        {
            var option = new Locker.SecretCreateOptions
            {
                Key = key,
                Value = value,
                Description = description,
                EnvironmentName = environmentName
            };
            var secret = Service.Create(option);
            return secret;
        }

        public static Secret ModifySecret(string key, string newValue, string envName = "", string newKey = "",
            string newEnvName = "",
            string newDesc = "")
        {
            var option = new SecretUpdateOptions()
            {
                Key = newKey,
                Value = newValue,
                Description = newDesc,
                EnvironmentName = newEnvName,
            };
            var secret = Service.Modify(id: key, environmentName: envName, updateOptions: option);
            return secret;
        }
    }
}