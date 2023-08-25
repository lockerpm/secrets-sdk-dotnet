namespace LockerTests
{
    using Locker;
    using Xunit;

    public class SecretServiceTest : BaseLockerTest
    {
        private readonly SecretService _service;
        private readonly SecretCreateOptions _createOptions;
        private readonly string _secretIdMock = "test1";

        public SecretServiceTest() :
            base()
        {
            this._service = new SecretService();
            this._createOptions = new SecretCreateOptions()
            {
                Key = "test1",
                Value = "test1",
                EnvironemntId = "1"
            };
        }

        [Fact]
        public void Create()
        {
            var secret = this._service.Create(this._createOptions);
            Assert.NotNull(secret);
            Assert.IsType<Secret>(secret);
            Assert.Equal(secret.Data.Key, _createOptions.Key);
            Assert.Equal(secret.Data.Value, _createOptions.Value);
            Assert.Equal(secret.EnvironmentId, _createOptions.EnvironemntId);
        }

        [Fact]
        public void List()
        {
            var secrets = this._service.List();
            Assert.NotNull(secrets);
            Assert.IsType<LockerList<Secret>>(secrets);
        }

        [Fact]
        public void Get()
        {
            var secret = this._service.Get(_secretIdMock);
            Assert.NotNull(secret);
            Assert.IsType<string>(secret);
        }
    }
}