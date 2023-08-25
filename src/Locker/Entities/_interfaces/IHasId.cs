namespace Locker
{
    /// <summary>
    /// Interface tht identifies entities returned by Locker that have an `id`
    /// </summary>
    public interface IHasId
    {
        /// <summary>
        /// Unique identifier for the object.
        /// </summary>
        string Id { get; }
    }
}