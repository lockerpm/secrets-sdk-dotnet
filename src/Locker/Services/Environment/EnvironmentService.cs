namespace Locker
{
    public class EnvironmentService : Service<Environment>,
        ICreatable<Environment, EnvironmentCreateOptions>,
        IRetrievable<Environment, EnvironmentRetrieveOptions>,
        IUpdatable<Environment, EnvironmentUpdateOptions>,
        IListable<Environment, EnvironmentListOptions>

    {
        public EnvironmentService() : base()
        {
        }

        protected override string BaseCli => "environment";

        public Environment Create(EnvironmentCreateOptions createOptions, RequestOptions requestOptions = null)
        {
            return this.CreateEntity(createOptions, requestOptions);
        }


        public LockerList<Environment> List(EnvironmentListOptions listOptions = null,
            RequestOptions requestOptions = null)
        {
            return this.ListEntities(listOptions, requestOptions);
        }

        public Environment Get(string id, EnvironmentRetrieveOptions retrieveOptions = null,
            RequestOptions requestOptions = null)
        {
            var environment = this.Retrieve(id, retrieveOptions, requestOptions);
            return environment;
        }

        public Environment Modify(string id, EnvironmentUpdateOptions updateOptions,
            RequestOptions requestOptions = null)
        {
            return this.Update(id, updateOptions: updateOptions, requestOptions: requestOptions);
        }

        public Environment Update(string id, EnvironmentUpdateOptions updateOptions,
            RequestOptions requestOptions = null)
        {
            return this.UpdateEntity(id, updateOptions, requestOptions);
        }

        public Environment? Retrieve(string id, EnvironmentRetrieveOptions retrieveOptions = null,
            RequestOptions requestOptions = null)
        {
            return this.GetEntity(id, retrieveOptions, requestOptions);
        }
    }
}