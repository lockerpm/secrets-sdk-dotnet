using System.Globalization;
using System.Text;
using Newtonsoft.Json;

namespace Locker.Infrastructure;

internal static class JsonUtils
{
    private static readonly JsonSerializerSettings DefaultSerializerSettings = new()
    {
        MaxDepth = 128,
    };

    public static T DeserializeObject<T>(
        string value,
        JsonSerializerSettings? settings = null) =>
        (T)DeserializeObject(value, typeof(T), settings);

    public static object DeserializeObject(
        string value,
        Type type,
        JsonSerializerSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(type);
        var serializer = JsonSerializer.Create(settings ?? DefaultSerializerSettings);
        using var reader = new JsonTextReader(new StringReader(value));
        return serializer.Deserialize(reader, type)
            ?? throw new JsonSerializationException("JSON deserialized to null.");
    }

    public static string SerializeObject(
        object value,
        Formatting formatting = Formatting.None,
        JsonSerializerSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        var serializer = JsonSerializer.Create(settings);
        serializer.Formatting = formatting;
        var builder = new StringBuilder(256);
        using var stringWriter = new StringWriter(builder, CultureInfo.InvariantCulture);
        using var jsonWriter = new JsonTextWriter(stringWriter) { Formatting = formatting };
        serializer.Serialize(jsonWriter, value);
        return stringWriter.ToString();
    }
}
