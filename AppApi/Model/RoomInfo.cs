using System.Text.Json.Serialization;

namespace tori.AppApi.Model;

public record RoomInfo(
    int RoomId,
    int PlayerCount,
    [property: JsonConverter(typeof(JsonStringConverter<GoodsInfo>))] GoodsInfo GoodsInfo,
    [property: JsonConverter(typeof(JsonDateTimeConverter))] DateTime BeginRunningTime,
    [property: JsonConverter(typeof(JsonDateTimeConverter))] DateTime EndRunningTime);