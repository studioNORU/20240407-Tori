using System.Text.Json.Serialization;
using Tori.Controllers.Data;

namespace tori.AppApi.Model;

public record RoomInfo(
    int RoomId,
    int PlayerCount,
    string Type,
    [property: JsonConverter(typeof(JsonStringConverter<GoodsInfo>))] GoodsInfo GoodsInfo,
    [property: JsonConverter(typeof(JsonDateTimeConverter))] DateTime BeginRunningTime,
    [property: JsonConverter(typeof(JsonDateTimeConverter))] DateTime EndRunningTime)
{
    public GameReward.RewardTypes ToEnum() => this.Type.ToUpper() switch
    {
        "I" => GameReward.RewardTypes.Internal,
        "E" => GameReward.RewardTypes.External,
        _ => throw new NotImplementedException()
    };
}