using Newtonsoft.Json;

namespace Locker
{
    public class Secret : LockerEntity<Secret>, IHasId, IHasObject
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
        public float? RevisionDate { get; set; }

        /// <summary>
        /// Number representing the object's update date.
        /// </summary>
        [JsonProperty("updated_date")]
        public float? UpdatedDate { get; set; }

        /// <summary>
        /// Number representing the object's deleted date.
        /// </summary>
        [JsonProperty("deleted_date")]
        public float? DeletedDate { get; set; }

        /// <summary>
        /// Number representing the object's last use date.
        /// </summary>
        [JsonProperty("last_use_date")]
        public float? LastUseDate { get; set; }

        /// <summary>
        /// Nested object representing the object's data.
        /// </summary>
        [JsonProperty("data")]
        public SecretData Data { get; set; }

        /// <summary>
        /// String representing the object's environment id.
        /// </summary>
        [JsonProperty("environment_id")]
        public string EnvironmentId { get; set; }

        /// <summary>
        /// String representing the object's environment name.
        /// </summary>
        [JsonProperty("environment_name")]
        public string EnvironmentName { get; set; }

        /// <summary>
        /// String representing the object's project id.
        /// </summary>
        [JsonProperty("project_id")]
        public string ProjectId { get; set; }

        /// <summary>
        /// String representing the object's key.
        /// </summary>
        [JsonProperty("key")]
        public string Key { get; set; }

        /// <summary>
        /// String representing the object's value.
        /// </summary>
        [JsonProperty("value")]
        public string Value { get; set; }

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