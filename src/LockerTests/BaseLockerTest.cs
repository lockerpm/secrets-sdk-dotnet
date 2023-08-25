namespace LockerTests
{
    using System.IO;
    using System.Net;
    using System.Net.Http;
    using System.Reflection;
    using System.Text;
    using Locker;
    using Xunit;

    [Collection("locker-mock tests")]
    public class BaseLockerTest
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="BaseLockerTest"/> class with no fixtures.
        /// </summary>
        public BaseLockerTest()
        {
            LockerConfiguration.Instance.Init(envPath: "lockertest.env");
        }

        protected static string GetResourceAsString(string path)
        {
            var fullpath = "LockerTests.Resources." + path;

            var type = typeof(BaseLockerTest).GetTypeInfo().Assembly.GetManifestResourceStream(fullpath);
            var contents = new StreamReader(
                type,
                Encoding.UTF8).ReadToEnd();

            return contents;
        }
    }
}