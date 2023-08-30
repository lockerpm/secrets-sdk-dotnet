namespace LockerExample
{
    using Locker;
    using Environment = Locker.Environment;

    public class EnvironmentExample
    {
        private static readonly EnvironmentService Service = new EnvironmentService();

        public static object ListEnvironment()
        {
            var options = new Locker.EnvironmentListOptions()
            {
            };
            var requestOptions = new RequestOptions()
            {
                IsJson = false
            };

            var environments = Service.List(options, requestOptions);
            return environments;
        }

        public static object GetEnvironment()
        {
            var options = new Locker.EnvironmentRetrieveOptions();
            var requestOptions = new RequestOptions()
            {
                IsJson = false
            };
            var environment = Service.Get("env1", options, requestOptions);
            return environment;
        }

        public static object ModifyEnvironment()
        {
            var option = new EnvironmentUpdateOptions()
            {
                Name = "env1",
                Description = "new description",
                ExternalUrl = "new external url"
            };
            var requestOptions = new RequestOptions()
            {
                IsJson = false
            };
            var environment = Service.Modify(id: "env1", updateOptions: option, requestOptions);
            return environment;
        }

        public static object CreateEnvironment()
        {
            var option = new EnvironmentCreateOptions()
            {
                Name = "env3",
                ExternalUrl = "env3 external url",
                Description = "env3 description"
            };
            var requestOptions = new RequestOptions()
            {
                IsJson = false
            };
            var environment = Service.Create(option, requestOptions);
            return environment;
        }
    }
}