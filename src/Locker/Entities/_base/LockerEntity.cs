namespace Locker
{
    using System.Diagnostics.CodeAnalysis;
    using System.Reflection;
    using System.Runtime.CompilerServices;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using Locker.Infrastructure;

    [JsonObject(MemberSerialization.OptIn)]
    [JsonConverter(typeof(LockerEntityConverter))]
    public abstract class LockerEntity : ILockerEntity
    {
        /// <summary>
        /// Gets the raw <see cref="JObject">JObject</see> exposed by the Newtonsoft.Json library.
        /// This can be used to access properties that are not directly exposed by Locker's .NET
        /// library.
        /// </summary>
        /// <remarks>
        /// You should always prefer using the standard property accessors whenever possible. This
        /// accessor is not considered fully stable and might change or be removed in future
        /// versions.
        /// </remarks>
        /// <returns>The raw <see cref="JObject">JObject</see>.</returns>
        [JsonIgnore]
        public JObject RawJObject { get; protected set; }

        [JsonIgnore] public LockerResponse LockerResponse { get; set; }

        public static IHasObject FromJson(string value)
        {
            return JsonUtils.DeserializeObject<IHasObject>(value, LockerConfiguration.SerializerSettings);
        }

        /// <summary>Deserializes the JSON to the specified Locker object type.</summary>
        /// <typeparam name="T">The type of the Locker object to deserialize to.</typeparam>
        /// <param name="value">The object to deserialize.</param>
        /// <returns>The deserialized Locker object from the JSON string.</returns>
        public static T FromJson<T>(string value)
            where T : ILockerEntity
        {
            T result;
            try
            {
                result = JsonUtils.DeserializeObject<T>(value, LockerConfiguration.SerializerSettings);
            }
            catch (Newtonsoft.Json.JsonException jsonException)
            {
                if (typeof(T).GetGenericTypeDefinition() == typeof(LockerList<>))
                {
                    value = $"\"data\":{value}";
                    value = "{" + value + "}";
                }

                result = JsonUtils.DeserializeObject<T>(value, LockerConfiguration.SerializerSettings);
            }

            return result;
        }

        internal void SetRawJObject(JObject rawJObject)
        {
            this.RawJObject = rawJObject;
        }

        /// <summary>Reports a Locker object as a string.</summary>
        /// <returns>
        /// A string representing the Locker object, including its JSON serialization.
        /// </returns>
        /// <seealso cref="ToJson"/>
        public override string ToString()
        {
            return string.Format(
                "<{0}@{1} id={2}> JSON: {3}",
                this.GetType().FullName,
                RuntimeHelpers.GetHashCode(this),
                this.GetIdString(),
                this.ToJson());
        }

        /// <summary>Serializes the Locker object as a JSON string.</summary>
        /// <returns>An indented JSON string representation of the object.</returns>
        public string ToJson()
        {
            return JsonUtils.SerializeObject(
                this,
                Formatting.Indented,
                LockerConfiguration.SerializerSettings);
        }

        /// <summary>
        /// Sets a string ID on an expandable field. If the expandable field does not exist,
        /// a new one is initialized. If the expandable field exists and already contains an
        /// expanded object, and the ID within the expanded object does not match the new string ID,
        /// expanded object is discarded.
        /// </summary>
        /// <typeparam name="T">Type of the expanded object.</typeparam>
        /// <param name="id">The string ID.</param>
        /// <param name="expandable">The expandable field.</param>
        /// <returns>The expandable field with its ID set to the provided string ID.</returns>
        protected static ExpandableField<T> SetExpandableFieldId<T>(
            string id,
            ExpandableField<T> expandable)
            where T : IHasId
        {
            if (expandable == null)
            {
                expandable = new ExpandableField<T>
                {
                    Id = id
                };
            }
            else if (expandable.Id != id)
            {
                expandable.ExpandedObject = default;
                expandable.Id = id;
            }

            return expandable;
        }

        /// <summary>
        /// Sets an expanded object on an expandable field. If the expandable field does not exist,
        /// a new one is initialized.
        /// </summary>
        /// <typeparam name="T">Type of the expanded object.</typeparam>
        /// <param name="obj">The expanded object.</param>
        /// <param name="expandable">The expandable field.</param>
        /// <returns>
        /// The expandable field with its expanded object set to the provided object.
        /// </returns>
        protected static ExpandableField<T> SetExpandableFieldObject<T>(
            T obj,
            ExpandableField<T> expandable)
            where T : IHasId
        {
            expandable ??= new ExpandableField<T>();

            expandable.ExpandedObject = obj;

            return expandable;
        }

        protected static List<ExpandableField<T>> SetExpandableArrayIds<T>(List<string> ids)
            where T : IHasId
        {
            return ids?.Select((id) =>
            {
                var expandable = new ExpandableField<T>();
                expandable.Id = id;
                return expandable;
            }).ToList();
        }

        protected static List<ExpandableField<T>> SetExpandableArrayObjects<T>(List<T> objects)
            where T : IHasId
        {
            return objects?.Select((obj) =>
            {
                var expandable = new ExpandableField<T>();
                expandable.Id = obj.Id;
                expandable.ExpandedObject = obj;
                return expandable;
            }).ToList();
        }

        private object GetIdString()
        {
            foreach (var property in this.GetType().GetTypeInfo().DeclaredProperties)
            {
                if (property.Name == "Id")
                {
                    return property.GetValue(this);
                }
            }

            return null;
        }
    }

    public abstract class LockerEntity<T> : LockerEntity
        where T : LockerEntity<T>
    {
        /// <summary>Deserializes the JSON to a Locker object type.</summary>
        /// <param name="value">The object to deserialize.</param>
        /// <returns>The deserialized Locker object from the JSON string.</returns>
        public static T FromJson(string value)
        {
            return LockerEntity.FromJson<T>(value);
        }
    }
}