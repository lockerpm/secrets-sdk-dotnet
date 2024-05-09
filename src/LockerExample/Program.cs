using Locker;

namespace LockerExample
{
    public class Program
    {
        public static void Main(string[] args)
        {
            LockerConfiguration.Instance.Init();
            var jsonRequestOptions = new RequestOptions()
            {
                IsJson = true
            };

            String envPrefixName = "env_dev";
            String envTail = "_1";
            String envName = envPrefixName + envTail;
            String updateEnvName = "update_" + envName;

            // Test environment
            EnvironmentService environmentService = new EnvironmentService();

            var environments = environmentService.List(null, null);
            Console.WriteLine(environments);

            // Create env
            // var newEnv = environmentService.Create(new EnvironmentCreateOptions()
            // {
            //     Name = envName,
            //     ExternalUrl = envName,
            //     Description = envName
            // });
            // Console.WriteLine(newEnv);
            //
            // // Retrieve env
            // var retrieveEnv = environmentService.Get(envName, null, jsonRequestOptions);
            // Console.WriteLine(retrieveEnv);
            //
            // // Update env
            // EnvironmentUpdateOptions environmentUpdateOptions = new EnvironmentUpdateOptions()
            // {
            //     Name = updateEnvName,
            //     ExternalUrl = updateEnvName,
            //     Description = updateEnvName
            // };
            // var updatedEnv = environmentService.Modify(envName, environmentUpdateOptions, jsonRequestOptions);
            // Console.WriteLine(updatedEnv);
            // Console.WriteLine(environmentService.Get(updateEnvName, null, jsonRequestOptions));
            //
            // Console.WriteLine(environmentService.Modify(
            //     updateEnvName,
            //     new EnvironmentUpdateOptions()
            //     {
            //         Name = envName,
            //         Description = envName,
            //         ExternalUrl = envName
            //     }, null)
            // );

            SecretService secretService = new SecretService();
            Console.WriteLine(
                secretService.List(null,jsonRequestOptions)
                );
        }
    }
}