using Locker.Infrastructure;

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


        protected object CreateEntity(BaseOptions options, RequestOptions requestOptions)
        {
            String cli = $"{BaseCli} create";
            String option = options.BuildOptions();
            cli += option;
            return this.Call(
                cli_: cli,
                options: options,
                requestOptions: requestOptions
            );
        }


        protected object ListEntities(ListOptions options, RequestOptions requestOptions)
        {
            requestOptions = requestOptions ?? new RequestOptions();
            if (requestOptions.IsJson)
            {
                return this.Call<List<TEntityReturned>>(
                    cli_: $"{BaseCli} list",
                    options: options,
                    requestOptions: requestOptions
                );
            }

            return this.Call<string>(
                cli_: $"{BaseCli} list",
                options: options,
                requestOptions: requestOptions
            );
        }


        protected object Call(string cli_, RequestOptions requestOptions, BaseOptions options = null)
        {
            requestOptions = requestOptions ?? new RequestOptions();
            if (requestOptions.IsJson)
            {
                return Call<TEntityReturned>(
                    cli_: cli_,
                    requestOptions: requestOptions,
                    options: options
                );
            }

            return Call<string>(
                cli_: cli_,
                requestOptions: requestOptions,
                options: options
            );
        }

        protected T Call<T>(
            string cli_,
            RequestOptions requestOptions,
            BaseOptions options = null)
        {
            BinaryAdapter binaryExecutor = new BinaryAdapter(
                accessKeyId: requestOptions.AccessKeyId,
                secretAccessKey: requestOptions.SecretAccessKey,
                apiBase: requestOptions.ApiBase,
                apiVersion: requestOptions.ApiVersion,
                isJson: requestOptions.IsJson
            );
            string resData = binaryExecutor.Call(cli: cli_, timeout: requestOptions.Timeout,
                options: options);
            if (typeof(ILockerEntity).IsAssignableFrom(typeof(T)))
            {
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

            if (typeof(List<TEntityReturned>).IsAssignableFrom(typeof(T)))
            {
                T result;
                result = JsonUtils.DeserializeObject<T>(resData, LockerConfiguration.SerializerSettings);
                return result;
            }

            if (typeof(T) == typeof(string))
            {
                return (T)(object)resData;
            }

            return (T)(object)null;
        }
    }
}