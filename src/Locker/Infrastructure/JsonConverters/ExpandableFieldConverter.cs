using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Locker.Infrastructure;

public class ExpandableFieldConverter<T> : JsonConverter
    where T : IHasId
{
    public override bool CanWrite => true;

    public override bool CanConvert(Type objectType) =>
        objectType == typeof(ExpandableField<T>);

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        if (value is null)
        {
            serializer.Serialize(writer, null);
            return;
        }

        if (value is not ExpandableField<T> expandable)
        {
            throw new JsonSerializationException("Expected an expandable field.");
        }

        serializer.Serialize(
            writer,
            expandable.IsExpanded ? expandable.ExpandedObject : expandable.Id);
    }

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

        var token = JToken.Load(reader);
        var value = new ExpandableField<T>();
        if (token.Type == JTokenType.String)
        {
            value.Id = token.Value<string>();
        }
        else if (token.Type == JTokenType.Object)
        {
            value.ExpandedObject = token.ToObject<T>(serializer);
        }
        else
        {
            throw new JsonSerializationException("Expandable field must be a string or object.");
        }

        return value;
    }
}
