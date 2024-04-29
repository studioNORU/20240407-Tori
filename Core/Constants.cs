namespace tori.Core;

public static class Constants
{
    /// <summary>
    /// 첫 gameend 요청 이후 랭킹 집계 강제 마감까지 대기할 시간입니다. (초 단위)
    /// </summary>
    public const int SessionForceEndSeconds = 5;

    /// <summary>
    /// 참가자 입장 대기 시간입니다. (초 단위)
    /// </summary>
    public const int SessionWaitingEntrySeconds = 60;
}