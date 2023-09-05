using Locker;
using Environment = System.Environment;

namespace LockerExample
{
    public class Program
    {
        public static void Main(string[] args)
        {
            LockerConfiguration.Instance.Init();
            // var secrets = SecretExample.ListSecret();
            // Console.WriteLine($"List secret:\n{secrets}");
            // var secret = SecretExample.GetSecret();
            // Console.WriteLine($"Get secret:\n{secret}");
            // var updatedSecret = SecretExample.ModifySecret();
            // Console.WriteLine($"Updated secret:\n{updatedSecret}");
            // var newSecret = SecretExample.CreateSecret();
            // Console.WriteLine($"New secret:\n{newSecret}");

            var envs = EnvironmentExample.ListEnvironment();
            Console.WriteLine($"List env:\n{envs}");
            var env = EnvironmentExample.GetEnvironment();
            Console.WriteLine($"Get env: {env}");
            var updatedEnv = EnvironmentExample.ModifyEnvironment();
            Console.WriteLine($"Updated env: {updatedEnv}");
            // var newEnv = EnvironmentExample.CreateEnvironment();
            // Console.WriteLine($"New Env:\n {newEnv}");
        }
    }
}