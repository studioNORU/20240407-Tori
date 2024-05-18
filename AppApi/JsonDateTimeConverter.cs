using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace tori.AppApi;

public class JsonDateTimeConverter : JsonConverter<DateTime>
{
    private const string Format = "yyyy-MM-dd HH:mm:ss.f";

    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("Expected string token type.");

        var jsonString = reader.GetString();
        if (DateTime.TryParseExact(jsonString, Format, CultureInfo.InvariantCulture, DateTimeStyles.None,
                out var dateTime))
            return dateTime;

        throw new JsonException($"Unable to parse date : {jsonString}");
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString(Format));
    }
}