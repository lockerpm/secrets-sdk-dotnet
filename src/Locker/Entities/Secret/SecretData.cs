using Newtonsoft.Json;

namespace Locker
{
    public class SecretData : LockerEntity<SecretData>
    {
        /// <summary>
        /// String representing the object's key.
        /// </summary>
        [JsonProperty("key")]
        public string Key { get; set; } = string.Empty;

        /// <summary>
        /// String representing the object's value.
        /// </summary>
        [JsonProperty("value")]
        public string Value { get; set; } = string.Empty;

        /// <summary>
        /// String representing the object's description.
        /// </summary>
        [JsonProperty("description")]
        public string Description { get; set; } = string.Empty;
    }
}
