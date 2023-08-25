namespace Locker
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    public interface INestedListable<TEntity, TOptions>
        where TEntity : ILockerEntity, IHasId
        where TOptions : ListOptions, new()
    {
        LockerList<TEntity> List(string parentId, TOptions listOptions = null, RequestOptions requestOptions = null);


        IEnumerable<TEntity> ListAutoPaging(string parentId, TOptions listOptions = null,
            RequestOptions requestOptions = null);
    }
}