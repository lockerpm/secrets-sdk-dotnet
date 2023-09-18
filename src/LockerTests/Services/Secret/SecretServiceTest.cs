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
            };
        }

        [Fact]
        public void CreateWithJsonOption()
        {
            var requestOptions = new RequestOptions()
            {
                IsJson = true
            };
            var returnData = this._service.Create(this._createOptions, requestOptions);
            Assert.NotNull(returnData);
            Assert.IsType<Secret>(returnData);
            Secret secret = (Secret)returnData;
            Assert.Equal(secret.Key, _createOptions.Key);
            Assert.Equal(secret.Value, _createOptions.Value);
            Assert.Equal(secret.EnvironmentName, _createOptions.EnvironemntName);
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
            var secret = this._service.GetSecret(name: _secretIdMock);
            Assert.NotNull(secret);
            Assert.IsType<string>(secret);
        }

        [Fact]
        public void ModifyWithJsonOption()
        {
            var option = new SecretUpdateOptions()
            {
                Key="key1",
                Value = "key1 new value",
                Description = "key1 new description",
            };
            var requestOptions = new RequestOptions()
            {
                IsJson = true
            };
            var secret = this._service.Modify(name: "key1", updateOptions: option, requestOptions);
            Assert.NotNull(secret);
            Assert.IsType<Secret>(secret);
            
        }
        [Fact]
        public void ModifyWithoutJsonOption()
        {
            var option = new SecretUpdateOptions()
            {
                Key="key1",
                Value = "key1 new value",
                Description = "key1 new description",
            };
            var requestOptions = new RequestOptions()
            {
                IsJson = false
            };
            var secret = this._service.Modify(name: "key1", updateOptions: option, requestOptions);
            Assert.NotNull(secret);
            Assert.IsType<string>(secret);
            
        }
    }
}