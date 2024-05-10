using System.Collections.Generic;

namespace LockerTests
{
    using Xunit;
    using Locker;
    using Environment = Locker.Environment;

    public class EnvironmentServiceTest : BaseLockerTest
    {
        private readonly EnvironmentService _service;
        private readonly string _envPrefixName = "env";
        private readonly string _envTail = "_2";
        private readonly string _envName;
        private readonly string _envUpdateName;

        public EnvironmentServiceTest() :
            base()
        {
            _service = new EnvironmentService();
            _envName = _envPrefixName + _envTail;
            _envUpdateName = "update_" + _envName;
        }


        [Fact]
        public void List()
        {
            var environments = this._service.List(null, _jsonOpts);
            Assert.NotNull(environments);
            Assert.IsType<List<Environment>>(environments);
        }

        [Fact]
        public void Create()
        {
            var env = _service.Create(
                new EnvironmentCreateOptions()
                {
                    Name = _envName,
                    ExternalUrl = _envName,
                    Description = _envName
                });
            Assert.NotNull(env);
            Assert.IsType<Environment>(env);
            Environment envObj = (Environment)env;
            Assert.Equal(envObj.Name, _envName);
            Assert.Equal(envObj.ExternalUrl, _envName);
            Assert.Equal(envObj.Description, _envName);
        }


        [Fact]
        public void Get()
        {
            var env = this._service.Get(name: this._envName, null, _jsonOpts);
            Assert.NotNull(env);
            Assert.IsType<Environment>(env);
            Environment envObj = (Locker.Environment)(env);
            Assert.Equal(envObj.Name, _envName);
        }

        [Fact]
        public void ModifyDescription()
        {
            Environment updatedEnv = (Locker.Environment)_service.Modify(_envName, new EnvironmentUpdateOptions()
            {
                Description = _envUpdateName
            }, _jsonOpts);
            Assert.Equal(updatedEnv.Description, _envUpdateName);
            // Revert update
            _service.Modify(_envName, new EnvironmentUpdateOptions()
            {
                Description = _envName
            });
        }

        [Fact]
        public void ModifyExternalUrl()
        {
            Environment updatedEnv = (Locker.Environment)_service.Modify(_envName, new EnvironmentUpdateOptions()
            {
                ExternalUrl = _envUpdateName
            }, _jsonOpts);
            Assert.Equal(updatedEnv.ExternalUrl, _envUpdateName);

            // Revert update
            _service.Modify(_envName, new EnvironmentUpdateOptions()
            {
                ExternalUrl = _envName
            });
        }

        [Fact]
        public void ModifyEnvName()
        {
            Environment updatedEnv = (Locker.Environment)_service.Modify(_envName, new EnvironmentUpdateOptions()
            {
                Name = _envUpdateName
            }, _jsonOpts);
            Assert.Equal(updatedEnv.Name, _envUpdateName);
            Environment retrieveEnv = (Locker.Environment)_service.Get(_envUpdateName, null, _jsonOpts);
            Assert.Equal(retrieveEnv.Name, _envUpdateName);
            // Revert update
            updatedEnv = (Locker.Environment)_service.Modify(_envUpdateName, new EnvironmentUpdateOptions()
            {
                Name = _envName
            }, _jsonOpts);
            Assert.Equal(updatedEnv.Name, _envName);
        }
    }
}