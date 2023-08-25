namespace LockerTests
{
    using Xunit;
    using Locker;
    using Newtonsoft.Json;

    public class SecretTest : BaseLockerTest
    {
        [Fact]
        public void Deserialize()
        {
            var json = GetResourceAsString("api_fixtures.secret.json");
            var secret = JsonConvert.DeserializeObject<Secret>(json);
            Assert.NotNull(secret);
            Assert.IsType<Secret>(secret);
            Assert.Equal("secret", secret.Object);
        }
    }
}