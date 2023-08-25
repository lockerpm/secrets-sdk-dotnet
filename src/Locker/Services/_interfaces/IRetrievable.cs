namespace Locker
{
    using System.Threading;
    using System.Threading.Tasks;

    public interface IRetrievable<TEntity, TOptions>
        where TEntity : ILockerEntity, IHasId
        where TOptions : BaseOptions, new()
    {
        TEntity? Retrieve(string id, TOptions retrieveOptions = null,
            RequestOptions requestOptions = null);
    }
}