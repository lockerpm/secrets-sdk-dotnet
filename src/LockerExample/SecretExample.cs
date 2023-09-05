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
            var options = new Locker.SecretRetrieveOptions();
            var requestOptions = new RequestOptions();
            object value = Service.Get(id: "key1", retrieveOptions: options, requestOptions: requestOptions);
            return value;
        }

        public static object CreateSecret()
        {
            var option = new Locker.SecretCreateOptions
            {
                Key = "key5",
                Value = "key5 value",
                Description = "key5 description",
                EnvironemntName = "env1"
            };
            var requestOptions = new RequestOptions();
            var secret = Service.Create(option, requestOptions);
            return secret;
        }

        public static object ModifySecret()
        {
            var option = new SecretUpdateOptions()
            {
                Key = "test1",
                Value = "test1 new value",
                Description = "test1 new description",
            };
            var requestOptions = new RequestOptions();
            var secret = Service.Modify(id: "test1", updateOptions: option, requestOptions);
            return secret;
        }
    }
}