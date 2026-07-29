using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Locker.Infrastructure;

public class LockerObjectConverter : JsonConverter
{
    public override bool CanWrite => false;

    public override bool CanConvert(Type objectType) =>
        typeof(IHasObject).IsAssignableFrom(objectType);

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

        return raw.ToObject(concreteType, serializer);
    }
}
