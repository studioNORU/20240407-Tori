using System.Text.Json.Serialization;

namespace tori.AppApi.Model;

public record ItemInfo(
    [property: JsonPropertyName("item_no")] int ItemNo,
    [property: JsonPropertyName("item_count")] int ItemCount);