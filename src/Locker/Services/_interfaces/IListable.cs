namespace Locker
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    public interface IListable<TEntity, TOptions>
        where TEntity : ILockerEntity, IHasId
        where TOptions : ListOptions, new()
    {
        LockerList<TEntity>? List(TOptions listOptions = null, RequestOptions requestOptions = null);
    }
}