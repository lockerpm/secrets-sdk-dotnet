using Locker;

namespace LockerExample
{
    public class Program
    {
        public static void Main(string[] args)
        {
            // var env = EnvironmentExample.GetEnvironment("newEnv");
            // Console.WriteLine(env.Name);
            // var modifiedEnv =
            //     EnvironmentExample.UpdateEnvironment("newEnv", newName: "newEnv", newExternalUrl: "newExt");
            // Console.WriteLine(modifiedEnv);

            // setting by .evn file
            LockerConfiguration.Instance.Init(
                envPath: "test.env"
            );
            //
            var secrets = SecretExample.ListSecret();
            foreach (var secret in secrets)
            {
                Console.WriteLine(secret);
            }

            // var newEnv = EnvironmentExample.UpdateEnvironment("env2", "env2.com");
            // Console.WriteLine(newEnv);
        }
    }
}