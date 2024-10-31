namespace LockerExample
{
    using Locker;

    public class SecretExample
    {
        private static readonly SecretService Service = new Locker.SecretService();

        public static object ListSecret()
        {
            var options = new Locker.SecretListOptions()
            {
            };
            var requestOption = new RequestOptions()
            {
                IsJson = false
            };

            var secrets = Service.List(requestOptions: requestOption);

            return secrets;
        }

        public static object GetSecret()
        {
            var options = new SecretRetrieveOptions();
            var requestOptions = new RequestOptions();
            object value = Service.GetSecret(
                name: "Key1",
                // defaultValue: "default",
                retrieveOptions: options,
                requestOptions: requestOptions
            );
            return value;
        }

        public static object CreateSecret()
        {
            var option = new Locker.SecretCreateOptions
            {
                Key = "Key 3",
                Value = "Value 3",
                Description = "Desc 3",
            };
            var requestOptions = new RequestOptions();
            var secret = Service.Create(option, requestOptions);
            return secret;
        }

        public static object ModifySecret()
        {
            var option = new SecretUpdateOptions()
            {
                Value = "test1 new value",
                Description = "test1 new description",
            };
            var requestOptions = new RequestOptions();
            var secret = Service.Modify("test1", updateOptions: option, requestOptions);
            return secret;
        }
    }
}