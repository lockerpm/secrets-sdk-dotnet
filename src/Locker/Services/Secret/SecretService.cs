namespace Locker
{
    public class SecretService : Service<Secret>,
        ICreatable<Secret, SecretCreateOptions>,
        IRetrievable<Secret, SecretRetrieveOptions>,
        IUpdatable<Secret, SecretUpdateOptions>,
        IListable<Secret, SecretListOptions>
    {
        public SecretService() : base()
        {
        }


        protected override string BaseCli => "secret";


        public object List(SecretListOptions listOptions = null, RequestOptions requestOptions = null)
        {
            return this.ListEntities(listOptions, requestOptions);
        }

        public object Create(SecretCreateOptions createOptions, RequestOptions requestOptions = null)
        {
            return this.CreateEntity(createOptions, requestOptions);
        }

        public object Get(string id, SecretRetrieveOptions retrieveOptions = null, RequestOptions requestOptions = null)
        {
            return this.GetEntity(id, retrieveOptions, requestOptions);
        }

        public object Get(string id, string environmentName, SecretRetrieveOptions retrieveOptions = null,
            RequestOptions requestOptions = null)
        {
            string cli_ = $"{BaseCli} get --id {id} --env {environmentName}";

            return this.Call(
                cli_: cli_,
                options: retrieveOptions,
                requestOptions: requestOptions
            );
        }

        public string GetSecret(string id, string defaultValue = "", string environmentName = "",
            SecretRetrieveOptions retrieveOptions = null,
            RequestOptions requestOptions = null)
        {
            string secretValue = "";

            try
            {
                if (environmentName == "")
                {
                    secretValue = (string)this.Get(
                        id: id,
                        retrieveOptions: retrieveOptions,
                        requestOptions: requestOptions
                    );
                }
                else
                {
                    secretValue = (string)this.Get(
                        id: id,
                        environmentName: environmentName,
                        retrieveOptions: retrieveOptions,
                        requestOptions: requestOptions
                    );
                }
            }
            catch (CliRunError e)
            {
                secretValue = defaultValue;
                Console.WriteLine(e);
            }

            return secretValue == "" ? defaultValue : secretValue;
        }

        public object Modify(string id, SecretUpdateOptions updateOptions, RequestOptions requestOptions = null)
        {
            updateOptions.Key = updateOptions.Key ?? id;

            return this.UpdateEntity(id, updateOptions, requestOptions);
        }

        public object Modify(string id, string environmentName, SecretUpdateOptions updateOptions,
            RequestOptions requestOptions = null)
        {
            string cli_ = $"{BaseCli} update --id {id} --env {environmentName}";

            return this.Call(
                cli_: cli_,
                options: updateOptions,
                requestOptions: requestOptions
            );
        }
    }
}