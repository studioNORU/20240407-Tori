namespace tori.Core;

public static class Constants
{
    /// <summary>
    /// JWT 토큰의 유효 시간입니다. (분 단위)
    /// </summary>
    public const int JwtTokenDurationMinutes = 60;
    
    /// <summary>
    /// 첫 gameend 요청 이후 랭킹 집계 강제 마감까지 대기할 시간입니다. (초 단위)
    /// </summary>
    public const int SessionForceCloseSeconds = 5;

    /// <summary>
    /// 랭킹 집계 마감이 완료된 세션의 초기화 후 재활용까지 보류할 시간입니다. (초 단위)
    /// </summary>
    public const int SessionDeferRecycleSeconds = 30;
}