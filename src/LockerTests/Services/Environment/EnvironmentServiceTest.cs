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
        public void Create()
        {
            var environment = this._service.Create(this._createOptions);
            Assert.NotNull(environment);
            Assert.IsType<Environment>(environment);
            Assert.Equal(environment.Name, _createOptions.Name);
            Assert.Equal(environment.ExternalUrl, _createOptions.ExternalUrl);
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