using Swashbuckle.AspNetCore.Annotations;

namespace Tori.Controllers.Requests;

[SwaggerSchema("게임 진입을 위해 필요한 값입니다.")]
public record struct LoadingBody
{
    [SwaggerSchema("플레이어 ID", Nullable = false)]
    public string UserId { get; init; }
    
    [SwaggerSchema("플레이어 닉네임", Nullable = false)]
    public string UserNickname { get; init; }
    
    public int RoomId { get; init; }
}