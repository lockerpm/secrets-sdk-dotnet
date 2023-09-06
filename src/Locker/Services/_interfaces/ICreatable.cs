namespace Locker
{
    public interface ICreatable<TEntity, TOptions>
        where TEntity : ILockerEntity
        where TOptions : BaseOptions, new()
    {
        object Create(TOptions createOptions, RequestOptions requestOptions = null);
    }
}