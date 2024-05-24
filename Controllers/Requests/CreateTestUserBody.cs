using Swashbuckle.AspNetCore.Annotations;

namespace Tori.Controllers.Requests;

#if DEBUG || DEV
[SwaggerSchema("테스트를 위한 유저 정보 등록을 위한 데이터입니다.")]
public record struct CreateTestUserBody
{
    [SwaggerSchema("테스트를 위한 유저의 유저 ID입니다. 이후 다른 API에서 이 유저 ID를 사용할 경우, 앱 서버를 거치지 않고 테스트를 위한 유저 정보를 사용합니다.", Nullable = false)]
    public string UserId { get; init; }
    
    [SwaggerSchema("테스트를 위한 유저의 닉네임입니다.", Nullable = false)]
    public string UserNickname { get; set; }
    
    [SwaggerSchema("테스트를 위한 유저의 우승 횟수입니다.", Nullable = false)]
    public int WinnerCount { get; set; }

    [SwaggerSchema("테스트를 위한 유저의 아이템 소지 정보입니다. 키는 아이템 ID, 값은 수량입니다.", Nullable = false)]
    public Dictionary<string, int> Items { get; set; }

    [SwaggerSchema("테스트를 위한 유저의 에너지 보유량입니다.", Nullable = false)]
    public int Energy { get; set; }
    
    [SwaggerSchema("이 테스트를 위한 유저 정보가 만료될 시간입니다. 이 시간 이후로는 이 정보가 게임 서버에서 사용되지 않습니다.", Nullable = false)]
    public long ExpireAtUtc { get; init; }
}
#endif