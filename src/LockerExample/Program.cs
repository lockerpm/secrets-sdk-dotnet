using Locker;

namespace LockerExample
{
    public class Program
    {
        public static void Main(string[] args)
        {
            LockerConfiguration.Instance.Init(
                envPath: "test.env"
            );
            var secrets = SecretExample.ListSecret();
            foreach (var secret in secrets)
            {
                Console.WriteLine(secret);
            }
        }
    }
}