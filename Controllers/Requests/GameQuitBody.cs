using System.Text.Json.Serialization;
using Swashbuckle.AspNetCore.Annotations;

namespace Tori.Controllers.Requests;

[SwaggerSchema("게임 포기를 위한 정보입니다. 인증을 위한 게임 토큰을 요구합니다.")]
public class GameQuitBody : IAuthBody
{
    [SwaggerSchema("클라이언트 앱 버전", Nullable = false)]
    public string ClientVersion { get; init; }
    
    [SwaggerSchema("게임 토큰", Nullable = false)]
    public string Token { get; init; } = default!;
    
    [SwaggerSchema("아이템별 사용 횟수", Nullable = false)]
    public Dictionary<string, int> UsedItems { get; init; } = default!;
    
    [JsonIgnore]
    public int ItemCount => this.UsedItems.Values.Sum();
    
    [JsonIgnore]
    public Dictionary<int, int> SpentItems => this.UsedItems.ToDictionary(
        it => int.Parse(it.Key),
        it => it.Value);
}