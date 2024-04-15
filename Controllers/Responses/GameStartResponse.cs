using Swashbuckle.AspNetCore.Annotations;

namespace tori.Controllers.Responses;

[SwaggerSchema("gamestart API 응답")]
public record GameStartResponse : BaseResponse
{
    [SwaggerSchema("게임에 속한 플레이어들의 닉네임 목록입니다.", Nullable = false)]
    public string[] PlayerNicknames { get; init; } = default!;

}