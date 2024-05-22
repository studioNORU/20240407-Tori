using System.Text.Json;
using System.Text.Json.Serialization;

namespace tori.AppApi;

public class JsonStringConverter<T> : JsonConverter<T> where T : class
{
    private readonly JsonSerializerOptions options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseUpper,
        PropertyNameCaseInsensitive = true
    };
    
    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("Expected string token type.");

        var jsonString = reader.GetString()!;
        return JsonSerializer.Deserialize<T>(jsonString, this.options);
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(JsonSerializer.Serialize(value, options));
    }
}