namespace Locker
{
    /// <summary>Abstract base class for all services.</summary>
    /// <typeparam name="TEntityReturned">
    /// The type of <see cref="ILockerEntity"/> that this service returns.
    /// </typeparam>
    public abstract class Service<TEntityReturned>
        where TEntityReturned : ILockerEntity
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Service{EntityReturned}"/> class.
        /// </summary>
        protected Service()
        {
        }

        protected virtual string BaseCli => "";


        protected TEntityReturned CreateEntity(BaseOptions options, RequestOptions requestOptions)
        {
            return this.Call(
                cli_: $"{BaseCli} create",
                options: options,
                requestOptions: requestOptions);
        }


        protected TEntityReturned? GetEntity(string id, BaseOptions options, RequestOptions requestOptions)
        {
            return this.Call(
                cli_: $"{BaseCli} get --id {id}",
                options: options,
                requestOptions: requestOptions);
        }

        protected LockerList<TEntityReturned>? ListEntities(ListOptions options, RequestOptions requestOptions)
        {
            return this.Call<LockerList<TEntityReturned>>(
                cli_: $"{BaseCli} list",
                options: options,
                requestOptions: requestOptions);
        }

        protected TEntityReturned? UpdateEntity(string id, BaseOptions options, RequestOptions requestOptions)
        {
            return this.Call(
                cli_: $"{BaseCli} update --id {id}",
                options: options,
                requestOptions: requestOptions);
        }

        protected TEntityReturned Call(string cli_, RequestOptions requestOptions, BaseOptions options = null)
        {
            return Call<TEntityReturned>(cli_: cli_,
                requestOptions: requestOptions,
                options: options);
        }

        protected T Call<T>(
            string cli_,
            RequestOptions requestOptions,
            BaseOptions options = null)
            where T : ILockerEntity
        {
            requestOptions = requestOptions ?? new RequestOptions();
            BinaryAdapter binaryExecutor = new BinaryAdapter(
                accessKey: requestOptions.AccessKey,
                apiBase: requestOptions.ApiBase,
                apiVersion: requestOptions.ApiVersion);
            string resData = binaryExecutor.Call(cli: cli_, timeout: requestOptions.Timeout,
                options: options);
            T obj;
            try
            {
                obj = LockerEntity.FromJson<T>(resData);
            }
            catch (Newtonsoft.Json.JsonException jsonException)
            {
                Console.WriteLine(jsonException);
                throw;
            }

            return obj;
        }
    }
}