using Swashbuckle.AspNetCore.Annotations;
using Tori.Controllers.Data;
using tori.Core;

namespace tori.Controllers.Responses;

[SwaggerSchema("dev-check API 응답")]
public record DevCheckResponse : BaseResponse
{
    public int RoomId { get; set; }
    public string RequestedKst { get; set; } = default!;
    public string GameStartKst { get; set; } = default!;
    public string GameEndKst { get; set; } = default!;
    public GameReward GameReward { get; set; } = default!;
    public ResultCode ResultCode { get; set; }
    public string Message { get; set; } = default!;
}