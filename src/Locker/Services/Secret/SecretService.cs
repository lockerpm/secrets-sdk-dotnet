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


        public LockerList<Secret> List(SecretListOptions listOptions = null, RequestOptions requestOptions = null)
        {
            return this.ListEntities(listOptions, requestOptions);
        }

        public Secret Create(SecretCreateOptions createOptions, RequestOptions requestOptions = null)
        {
            createOptions.EnvironemntId = createOptions.EnvironemntId ?? "";
            createOptions.EnvironmentName = createOptions.EnvironmentName ?? "";
            return this.CreateEntity(createOptions, requestOptions);
        }


        public Secret Modify(string id, SecretUpdateOptions updateOptions, string environmentName = "",
            RequestOptions requestOptions = null)
        {
            string cli_ = $"{BaseCli} update --id {id}";
            if (environmentName != "")
            {
                cli_ += $" --env {environmentName}";
            }

            return this.Call(
                cli_: cli_,
                options: updateOptions,
                requestOptions: requestOptions);
        }

        public string Get(string id, string environmentName = "", string defaultValue = null,
            SecretRetrieveOptions retrieveOptions = null,
            RequestOptions requestOptions = null)
        {
            string cli_ = $"{BaseCli} get --id {id}";
            if (environmentName != "")
            {
                cli_ += $" --env {environmentName}";
            }

            var secret = this.Call(
                cli_: cli_,
                options: retrieveOptions,
                requestOptions: requestOptions);
            string value = secret == null ? defaultValue : secret.Data.Value;
            return value;
        }


        public Secret? Retrieve(string id, SecretRetrieveOptions retrieveOptions = null,
            RequestOptions requestOptions = null)
        {
            return this.GetEntity(id, retrieveOptions, requestOptions);
        }

        public Secret? Update(string id, SecretUpdateOptions updateOptions, RequestOptions requestOptions = null)
        {
            return this.UpdateEntity(id, updateOptions, requestOptions);
        }
    }
}