using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotNetEnv;

namespace tori.AppApi;

public class JsonDateTimeConverter : JsonConverter<DateTime>
{
    private const string Format = "yyyy-MM-dd HH:mm:ss.f";

    private static TimeZoneInfo? serverTimeZone;

    public JsonDateTimeConverter()
    {
        if (serverTimeZone == null)
        {
            var offset = Env.GetInt("APP_SERVER_TZ_OFFSET");
            serverTimeZone = TimeZoneInfo.CreateCustomTimeZone("App Server Time Zone", TimeSpan.FromHours(offset),
                "App Server Time Zone", "AST");
        }
    }

    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("Expected string token type.");

        var jsonString = reader.GetString();
        if (DateTime.TryParseExact(jsonString, Format, CultureInfo.InvariantCulture, DateTimeStyles.None,
                out var dateTime))
        {
            var withKind = DateTime.SpecifyKind(dateTime, DateTimeKind.Unspecified);
            return TimeZoneInfo.ConvertTimeToUtc(withKind, serverTimeZone!);
        }

        throw new JsonException($"Unable to parse date : {jsonString}");
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString(Format));
    }
}