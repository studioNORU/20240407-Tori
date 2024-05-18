using System.Text.Json;

namespace tori.AppApi;

public class JsonCamelToSnakePolicy : JsonNamingPolicy
{
    public override string ConvertName(string name)
    {
        return string.Concat(
            name.Select((ch, idx) => 0 < idx && char.IsUpper(ch)
                ? "_" + ch.ToString().ToLower()
                : ch.ToString().ToLower()));
    }
}