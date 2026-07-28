namespace Locker
{
    /// <summary>
    /// Interface that identifies all entities returned by Locker
    /// </summary>
    public interface ILockerEntity
    {
        LockerResponse? LockerResponse { get; set; }
    }
}
