namespace Locker
{
    using System.Threading;
    using System.Threading.Tasks;

    public interface IRetrievable<TEntity, TOptions>
        where TEntity : ILockerEntity, IHasId
        where TOptions : BaseOptions, new()
    {
        object Get(string name, TOptions retrieveOptions = null,
            RequestOptions requestOptions = null);
    }
}