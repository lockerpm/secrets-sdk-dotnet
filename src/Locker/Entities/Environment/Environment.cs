namespace Locker
{
    using Newtonsoft.Json;

    public class Environment : LockerEntity<Environment>, IHasId, IHasObject
    {
        /// <summary>
        /// Unique identifier for the object.
        /// </summary>
        [JsonProperty("id")]
        public string Id { get; set; }

        /// <summary>
        /// String representing the object's type. Objects of the same type share the same value.
        /// </summary>
        [JsonProperty("object")]
        public string Object { get; set; }

        /// <summary>
        /// Number representing the object's creation date.
        /// </summary>
        [JsonProperty("creation_date")]
        public float CreationDate { get; set; }

        /// <summary>
        /// Number representing the object's revision date.
        /// </summary>
        [JsonProperty("revision_date")]
        public float RevisionDate { get; set; }

        /// <summary>
        /// Number representing the object's update date.
        /// </summary>
        [JsonProperty("updated_date")]
        public float UpdatedDate { get; set; }

        /// <summary>
        /// String representing the object's name.
        /// </summary>
        [JsonProperty("name")]
        public string Name { get; set; }

        /// <summary>
        /// String representing the object's external url.
        /// </summary>
        [JsonProperty("external_url")]
        public string ExternalUrl { get; set; }

        /// <summary>
        /// String representing the object's description.
        /// </summary>
        [JsonProperty("description")]
        public string Description { get; set; }

        /// <summary>
        /// String representing the object's hash.
        /// </summary>
        [JsonProperty("hash")]
        public string Hash { get; set; }
    }
}