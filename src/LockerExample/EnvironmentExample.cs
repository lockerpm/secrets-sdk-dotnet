namespace LockerExample
{
    using Locker;
    using Environment = Locker.Environment;

    public class EnvironmentExample
    {
        private static readonly EnvironmentService Service = new EnvironmentService();

        public static LockerList<Environment> ListEnvironment()
        {
            var options = new Locker.EnvironmentListOptions()
            {
            };

            var environments = Service.List(options);
            return environments;
        }

        public static Environment GetEnvironment(string id)
        {
            var options = new Locker.EnvironmentRetrieveOptions();
            var environment = Service.Get(id, options);
            return environment;
        }

        public static Environment UpdateEnvironment(string id, string newName = "", string newExternalUrl = "",
            string newDescription = "")
        {
            var option = new EnvironmentUpdateOptions()
            {
                Name = newName,
                Description = newDescription,
                ExternalUrl = newExternalUrl
            };
            var environment = Service.Modify(id:id, updateOptions:option);
            return environment;
        }

        public static Environment CreateEnvironment(string name, string externalUrl = "", string description = "")
        {
            var option = new EnvironmentCreateOptions()
            {
                Name = name,
                ExternalUrl = externalUrl,
                Description = description
            };
            var environment = Service.Create(option);
            return environment;
        }
    }
}