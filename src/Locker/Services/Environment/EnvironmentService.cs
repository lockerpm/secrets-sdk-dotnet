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

        public object Create(EnvironmentCreateOptions createOptions, RequestOptions requestOptions = null)
        {
            return this.CreateEntity(createOptions, requestOptions);
        }


        public object List(EnvironmentListOptions listOptions = null,
            RequestOptions requestOptions = null)
        {
            return this.ListEntities(listOptions, requestOptions);
        }

        public object Get(string id, EnvironmentRetrieveOptions retrieveOptions = null,
            RequestOptions requestOptions = null)
        {
            return this.GetEntity(id, retrieveOptions, requestOptions);
        }

        public object Modify(string id, EnvironmentUpdateOptions updateOptions,
            RequestOptions requestOptions = null)
        {
            return this.UpdateEntity(id, updateOptions, requestOptions);
        }
    }
}