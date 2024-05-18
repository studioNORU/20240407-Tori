using Swashbuckle.AspNetCore.Annotations;

namespace Tori.Controllers.Requests;

[SwaggerSchema("게임 플레이 정보를 입력해야 합니다. 인증을 위한 게임 토큰을 요구합니다.")]
public class PlayDataBody : IAuthBody
{
    [SwaggerSchema("게임 토큰", Nullable = false)]
    public string Token { get; init; }
    
    [SwaggerSchema("아이템별 사용 횟수", Nullable = false)]
    public Dictionary<string, int> UsedItems { get; init; }
}