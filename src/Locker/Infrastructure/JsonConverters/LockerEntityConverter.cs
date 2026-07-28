using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Locker.Infrastructure;

public class LockerEntityConverter : JsonConverter
{
    public override bool CanWrite => false;

    public override bool CanConvert(Type objectType) =>
        typeof(LockerEntity).IsAssignableFrom(objectType);

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer) =>
        throw new NotSupportedException();

    public override object? ReadJson(
        JsonReader reader,
        Type objectType,
        object? existingValue,
        JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
        {
            return null;
        }

        var raw = JObject.Load(reader);
        var concreteType = LockerTypeRegistry.GetConcreteType(
            objectType,
            raw["object"]?.Value<string>());
        if (concreteType is null)
        {
            return null;
        }

        var entity = Activator.CreateInstance(concreteType) as LockerEntity
            ?? throw new JsonSerializationException("Unable to create Locker entity.");
        using var objectReader = raw.CreateReader();
        serializer.Populate(objectReader, entity);
        entity.SetRawJObject(raw);
        return entity;
    }
}
