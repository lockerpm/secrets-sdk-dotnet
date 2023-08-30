namespace LockerTests
{
    using Xunit;
    using Locker;
    using Environment = Locker.Environment;

    public class EnvironmentServiceTest : BaseLockerTest
    {
        private readonly EnvironmentService _service;
        private readonly EnvironmentCreateOptions _createOptions;
        private readonly string _environmentMockName = "Test";

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
        }
    }
}