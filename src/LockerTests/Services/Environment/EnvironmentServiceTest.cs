namespace LockerTests
{
    using Xunit;
    using Locker;
    using Environment = Locker.Environment;

    public class EnvironmentServiceTest : BaseLockerTest
    {
        private readonly EnvironmentService _service;
        private readonly EnvironmentCreateOptions _createOptions;
        private readonly string _environmentMockName = "env1";

        public EnvironmentServiceTest() :
            base()
        {
            this._service = new EnvironmentService();
            this._createOptions = new EnvironmentCreateOptions
            {
                Name = "Test1",
                ExternalUrl = "test1.cystack.net"
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
            Assert.IsType<Environment>(returnData);
            Environment environment = (Locker.Environment)(returnData);
            Assert.Equal(environment.Name, _createOptions.Name);
            Assert.Equal(environment.ExternalUrl, _createOptions.ExternalUrl);
        }

        [Fact]
        public void CreateWithoutJsonOption()
        {
            var requestOptions = new RequestOptions()
            {
                IsJson = false
            };
            var environment = this._service.Create(this._createOptions, requestOptions);
            Assert.NotNull(environment);
            Assert.IsType<string>(environment);
        }

        [Fact]
        public void List()
        {
            var environments = this._service.List();
            Assert.NotNull(environments);
            Assert.IsType<LockerList<Environment>>(environments);
        }

        [Fact]
        public void Get()
        {
            var env = this._service.Get(name: this._environmentMockName);
            Assert.NotNull(env);
            Assert.IsType<string>(env);
        }

        [Fact]
        public void ModifyWithJsonOption()
        {
            var updateOptions = new EnvironmentUpdateOptions()
            {
                Name = "env1",
                ExternalUrl = "env1.com"
            };
            var requestOptions = new RequestOptions()
            {
                IsJson = true
            };
            var env = this._service.Modify(name: this._environmentMockName, updateOptions: updateOptions,
                requestOptions: requestOptions);
            Assert.NotNull(env);
            Assert.IsType<Environment>(env);
        }
        
        [Fact]
        public void ModifyWithoutJsonOption()
        {
            var updateOptions = new EnvironmentUpdateOptions()
            {
                Name = "env1",
                ExternalUrl = "env1.com"
            };
            var requestOptions = new RequestOptions()
            {
            };
            var env = this._service.Modify(name: this._environmentMockName, updateOptions: updateOptions,
                requestOptions: requestOptions);
            Assert.NotNull(env);
            Assert.IsType<string>(env);
        }
    }
}