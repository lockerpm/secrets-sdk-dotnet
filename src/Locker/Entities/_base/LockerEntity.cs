using System.Runtime.CompilerServices;
using Locker.Infrastructure;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Locker;

[JsonObject(MemberSerialization.OptIn)]
[JsonConverter(typeof(LockerEntityConverter))]
public abstract class LockerEntity : ILockerEntity
{
    [JsonIgnore]
    public JObject? RawJObject { get; protected set; }

    [JsonIgnore]
    public LockerResponse? LockerResponse { get; set; }

    public static IHasObject? FromJson(string value) =>
        JsonUtils.DeserializeObject<IHasObject>(value, LockerConfiguration.SerializerSettings);

    public static T FromJson<T>(string value)
    {
        try
        {
            return JsonUtils.DeserializeObject<T>(value, LockerConfiguration.SerializerSettings);
        }
        catch (JsonException) when (
            typeof(T).IsGenericType
            && typeof(T).GetGenericTypeDefinition() == typeof(LockerList<>))
        {
            return JsonUtils.DeserializeObject<T>(
                $"{{\"data\":{value}}}",
                LockerConfiguration.SerializerSettings);
        }
    }

    internal void SetRawJObject(JObject rawJObject) => RawJObject = rawJObject;

    public override string ToString() =>
        $"<{GetType().FullName}@{RuntimeHelpers.GetHashCode(this)} id={GetIdString()}> JSON: {ToJson()}";

    public string ToJson() =>
        JsonUtils.SerializeObject(this, Formatting.Indented, LockerConfiguration.SerializerSettings);

    protected static ExpandableField<T> SetExpandableFieldId<T>(
        string id,
        ExpandableField<T>? expandable)
        where T : IHasId
    {
        expandable ??= new ExpandableField<T>();
        if (expandable.Id != id)
        {
            expandable.ExpandedObject = default;
            expandable.Id = id;
        }

        return expandable;
    }

    protected static ExpandableField<T> SetExpandableFieldObject<T>(
        T obj,
        ExpandableField<T>? expandable)
        where T : IHasId
    {
        expandable ??= new ExpandableField<T>();
        expandable.ExpandedObject = obj;
        return expandable;
    }

    protected static List<ExpandableField<T>>? SetExpandableArrayIds<T>(List<string>? ids)
        where T : IHasId =>
        ids?.Select(id => new ExpandableField<T> { Id = id }).ToList();

    protected static List<ExpandableField<T>>? SetExpandableArrayObjects<T>(List<T>? objects)
        where T : IHasId =>
        objects?.Select(obj => new ExpandableField<T> { ExpandedObject = obj }).ToList();

    private object? GetIdString() =>
        GetType().GetProperty("Id")?.GetValue(this);
}

public abstract class LockerEntity<T> : LockerEntity
    where T : LockerEntity<T>
{
    public new static T FromJson(string value) => LockerEntity.FromJson<T>(value);
}
