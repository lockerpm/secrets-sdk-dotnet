using System;
using System.Collections.Generic;

namespace LockerTests
{
    using Locker;
    using Xunit;

    public class SecretServiceTest : BaseLockerTest
    {
        private readonly SecretService _service;
        private readonly string _secPrefixName = "sec";
        private readonly string _secTail = "_3";
        private readonly string _secName;
        private readonly string _secUpdateName;
        private string _defaultEnvName;

        private void GetDefaultEnv()
        {
            var envService = new EnvironmentService();
            List<Environment> listEnv =
                (List<Environment>)envService.List(null, new RequestOptions() { IsJson = true });
            if (listEnv.Count > 0)
            {
                _defaultEnvName = listEnv[0].Name;
            }
            else
            {
                _defaultEnvName = "default_env";
                envService.Create(new EnvironmentCreateOptions()
                {
                    Name = _defaultEnvName,
                    ExternalUrl = _defaultEnvName
                });
            }
        }

        public SecretServiceTest() :
            base()
        {
            _service = new SecretService();
            _secName = _secPrefixName + _secTail;
            _secUpdateName = "update_" + _secName;
            GetDefaultEnv();
        }

        [Fact]
        public void List()
        {
            var secrets = this._service.List(null, _jsonOpts);
            Assert.NotNull(secrets);
            Assert.IsType<List<Secret>>(secrets);
        }

        [Fact]
        public void CreateWithoutEnv()
        {
            var sec = _service.Create(
                new SecretCreateOptions()
                {
                    Key = _secName,
                    Value = _secName,
                    Description = _secName,
                }, _jsonOpts);

            Assert.NotNull(sec);
            Assert.IsType<Secret>(sec);
            Secret secObj = (Secret)sec;
            Assert.Equal(secObj.Key, _secName);
            Assert.Equal(secObj.Value, _secName);
            Assert.Equal(secObj.Description, _secName);
            Assert.Null(secObj.EnvironmentName);
        }

        [Fact]
        public void CreateWithEnv()
        {
            var sec = _service.Create(
                new SecretCreateOptions()
                {
                    Key = _secName,
                    Value = _secName,
                    Description = _secName,
                    EnvironmentName = _defaultEnvName
                }, _jsonOpts);
            Assert.NotNull(sec);
            Assert.IsType<Secret>(sec);
            Secret secObj = (Secret)sec;
            Assert.Equal(secObj.Key, _secName);
            Assert.Equal(secObj.Value, _secName);
            Assert.Equal(secObj.Description, _secName);
            Assert.Equal(secObj.EnvironmentName, _defaultEnvName);
        }


        [Fact]
        public void GetWithoutEnv()
        {
            var secret = this._service.Get(name: _secName, null, _jsonOpts);
            Assert.NotNull(secret);
            Assert.IsType<Secret>(secret);
            var secObj = (Secret)secret;
            Assert.Equal(secObj.Key, _secName);
            Assert.Equal(secObj.Value, _secName);
            Assert.Equal(secObj.Description, _secName);
            Assert.Null(secObj.EnvironmentName);
        }

        [Fact]
        public void GetWithEnv()
        {
            var secret = this._service.Get(name: _secName, _defaultEnvName, null, _jsonOpts);
            Assert.NotNull(secret);
            Assert.IsType<Secret>(secret);
            var secObj = (Secret)secret;
            Assert.Equal(secObj.Key, _secName);
            Assert.Equal(secObj.Value, _secName);
            Assert.Equal(secObj.Description, _secName);
            Assert.Equal(secObj.EnvironmentName,_defaultEnvName);
        }

        [Fact]
        public void ModifyValue()
        {
            Secret updatedSec1 = (Secret)_service.Modify(
                _secName,
                new SecretUpdateOptions()
                {
                    Value = _secUpdateName
                },
                _jsonOpts);
            Secret updatedSec2 = (Secret)_service.Modify(
                _secName,
                _defaultEnvName,
                new SecretUpdateOptions()
                {
                    Value = _secUpdateName
                },
                _jsonOpts
            );

            Assert.Equal(updatedSec1.Value, _secUpdateName);
            Assert.Equal(updatedSec2.Value, _secUpdateName);

            // Revert update
            _service.Modify(
                _secName,
                new SecretUpdateOptions()
                {
                    Value = _secName
                },
                _jsonOpts);
            _service.Modify(
                _secName,
                _defaultEnvName,
                new SecretUpdateOptions()
                {
                    Value = _secName
                },
                _jsonOpts
            );
        }

        [Fact]
        public void ModifyDescription()
        {
            Secret updatedSec1 = (Secret)_service.Modify(
                _secName,
                new SecretUpdateOptions()
                {
                    Description = _secUpdateName
                },
                _jsonOpts);
            Secret updatedSec2 = (Secret)_service.Modify(
                _secName,
                _defaultEnvName,
                new SecretUpdateOptions()
                {
                    Description = _secUpdateName
                },
                _jsonOpts
            );

            Assert.Equal(updatedSec1.Description, _secUpdateName);
            Assert.Equal(updatedSec2.Description, _secUpdateName);

            // Revert update
            _service.Modify(
                _secName,
                new SecretUpdateOptions()
                {
                    Description = _secName
                },
                _jsonOpts);
            _service.Modify(
                _secName,
                _defaultEnvName,
                new SecretUpdateOptions()
                {
                    Description = _secName
                },
                _jsonOpts
            );
        }

        [Fact]
        public void ModifyEnvironment()
        {
            string _defaultEnvName2 = _defaultEnvName + "_2";
            EnvironmentService environmentService = new EnvironmentService();
            try
            {
                environmentService.Get(name: _defaultEnvName2);
            }
            catch (LockerError e)
            {
                environmentService.Create(new EnvironmentCreateOptions()
                {
                    Name = _defaultEnvName2,
                    ExternalUrl = _defaultEnvName2
                });
            }

            Secret updatedSec1 = (Secret)_service.Modify(
                _secName,
                new SecretUpdateOptions()
                {
                    EnvironmentName = _defaultEnvName2
                },
                _jsonOpts
            );
            Secret updatedSec2 = (Secret)_service.Modify(
                _secName,
                _defaultEnvName,
                new SecretUpdateOptions()
                {
                    EnvironmentName = ""
                },
                _jsonOpts
            );

            Assert.Equal(updatedSec1.EnvironmentName, _defaultEnvName2);
            Assert.Null(updatedSec2.EnvironmentName);

            // Revert update
            _service.Modify(
                _secName,
                new SecretUpdateOptions()
                {
                    EnvironmentName = _defaultEnvName
                },
                _jsonOpts);
            _service.Modify(
                _secName,
                _defaultEnvName2,
                new SecretUpdateOptions()
                {
                    EnvironmentName = ""
                },
                _jsonOpts
            );
        }

        [Fact]
        public void ModifyKey()
        {
            Secret updatedSec = (Secret)_service.Modify(
                _secName,
                new SecretUpdateOptions()
                {
                    Key = _secUpdateName
                },
                _jsonOpts
            );


            Assert.Equal(updatedSec.Key, _secUpdateName);

            // Revert update
            _service.Modify(
                _secUpdateName,
                new SecretUpdateOptions()
                {
                    Key = _secName
                },
                _jsonOpts
            );
        }
    }
}