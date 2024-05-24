using Swashbuckle.AspNetCore.Annotations;

namespace Tori.Controllers.Requests;

[SwaggerSchema("게임 진입을 위해 필요한 값입니다.")]
public record struct LoadingBody
{
    [SwaggerSchema("유저 ID", Nullable = false)]
    public string UserId { get; init; }
    
    [SwaggerSchema("플레이할 방 ID", Nullable = false)]
    public int RoomId { get; init; }
}