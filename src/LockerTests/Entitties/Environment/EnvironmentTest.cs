
namespace LockerTests
{
    using Newtonsoft.Json;
    using Xunit;
    public class EnvironmentTest : BaseLockerTest
    {
        [Fact]
        public void Deserialize()
        {
            var json = GetResourceAsString("api_fixtures.environment.json");
            var env = JsonConvert.DeserializeObject<Locker.Environment>(json);
            Assert.NotNull(env);
            Assert.IsType<Locker.Environment>(env);
            Assert.Equal("environment", env.Object);
        }
    }
    
}

