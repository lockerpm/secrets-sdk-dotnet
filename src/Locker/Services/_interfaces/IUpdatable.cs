namespace Locker
{
    public interface IUpdatable<TEntity, TOptions>
        where TEntity : ILockerEntity, IHasId
        where TOptions : BaseOptions, new()
    {
        object Modify(string id, TOptions updateOptions, RequestOptions requestOptions = null);
    }
}