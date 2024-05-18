﻿using Swashbuckle.AspNetCore.Annotations;
using Tori.Controllers.Data;

namespace tori.Controllers.Responses;

[SwaggerSchema("loading API 응답")]
public record LoadingResponse : BaseResponse
{
    [SwaggerSchema("API 사용에 필요한 토큰입니다. 사용자를 구분하기 위한 값입니다.", Nullable = false)]
    public string Token { get; init; } = default!;
        
    [SwaggerSchema("게임에 관련된 상수들입니다.", Nullable = false)]
    public Dictionary<string, int> Constants { get; init; } = default!;

    [SwaggerSchema("플레이어가 속한 방 번호입니다.", Nullable = false)]
    public int RoomId { get; init; }
    
    [SwaggerSchema("플레이어가 속한 스테이지의 ID입니다.", Nullable = false)]
    public string StageId { get; init; } = default!;

    [SwaggerSchema("유저의 닉네임입니다.", Nullable = false)]
    public string UserNickname { get; init; } = default!;
    
    [SwaggerSchema("유저가 보유한 에너지 총량입니다.", Nullable = false)]
    public int Energy { get; init; }
    
    [SwaggerSchema("유저가 지금까지 우승한 횟수입니다.", Nullable = false)]
    public int WinnerCount { get; init; }
    
    [SwaggerSchema("유저가 보유한 아이템 정보입니다.", Nullable = false)]
    public Dictionary<string, int> Items { get; init; } = default!;

    [SwaggerSchema("게임 경품 정보", Nullable = false)]
    public GameReward GameReward { get; init; } = default!;

    [SwaggerSchema("게임 시작 시간에 대한 C# DateTime.Ticks입니다.", Nullable = false)]
    public long GameStartUtc { get; init; }
    
    [SwaggerSchema("게임 종료 시간에 대한 C# DateTime.Ticks입니다.", Nullable = false)]
    public long GameEndUtc { get; init; }
}