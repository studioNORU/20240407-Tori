namespace tori.Core;

public static class Constants
{
    /// <summary>
    /// JWT 토큰의 유효 시간입니다. (분 단위)
    /// </summary>
    public const int JwtTokenDurationMinutes = 60;

    /// <summary>
    /// 일정 시간 게임 기록 API의 호출이 없는 유저를 탐지할 시간 간격입니다. (초 단위)
    /// </summary>
    public const int UserHealthCheckIntervalSeconds = 30;

    /// <summary>
    /// 게임 기록 API 호출이 없는 유저를 게임에 유지시킬 시간입니다. (초 단위)
    /// </summary>
    public const int UserHealthCheckThresholdSeconds = 60;

    /// <summary>
    /// 집계가 종료된 게임의 결과를 보내는 주기입니다. (초 단위)
    /// </summary>
    public const int PostGameResultIntervalSeconds = 30;

    /// <summary>
    /// 게임 플레이 1분 당 소모되는 에너지의 양입니다.
    /// </summary>
    public const int EnergyCostPerMinutes = 10;
    
    /// <summary>
    /// 첫 gameend 요청 이후 랭킹 집계 강제 마감까지 대기할 시간입니다. (초 단위)
    /// </summary>
    public const int SessionForceCloseSeconds = 5;

    /// <summary>
    /// 랭킹 집계 마감이 완료된 세션의 초기화 후 재활용까지 보류할 시간입니다. (초 단위)
    /// </summary>
    public const int SessionDeferRecycleSeconds = 30;
}