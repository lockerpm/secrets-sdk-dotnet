namespace Locker
{
    using System.Collections;
    using Locker.Infrastructure;
    using Newtonsoft.Json;

    public class LockerList<T> : LockerEntity<LockerList<T>>, IEnumerable<T>
    {
        /// <summary>
        /// A list containing the actual response elements.
        /// </summary>
        [JsonProperty("data", ItemConverterType = typeof(LockerObjectConverter))]
        public List<T> Data { get; set; } = new();

        public IEnumerator<T> GetEnumerator()
        {
            return this.Data.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return this.Data.GetEnumerator();
        }
    }
}
