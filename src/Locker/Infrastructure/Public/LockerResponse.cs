namespace Locker
{
    /// <summary>
    /// Represents a buffered textual response from Locker
    /// </summary>
    public class LockerResponse
    {
        public LockerResponse(string content)
        {
            this.Content = content;
        }

        public string Content { get; }
    }
}