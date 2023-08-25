namespace Locker
{
    public interface IUpdatable<TEntity, TOptions>
        where TEntity : ILockerEntity, IHasId
        where TOptions : BaseOptions, new()
    {
        TEntity? Update(string id, TOptions updateOptions, RequestOptions requestOptions = null);
    }
}