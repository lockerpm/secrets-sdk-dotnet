namespace Locker
{
    using System.Threading;
    using System.Threading.Tasks;

    public interface ICreatable<TEntity, TOptions>
        where TEntity : ILockerEntity
        where TOptions : BaseOptions, new()
    {
        TEntity? Create(TOptions createOptions, RequestOptions requestOptions = null);
    }
}