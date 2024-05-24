namespace tori.AppApi.Model;

public record GameResultFirst(
    int UserId,
    Dictionary<int, int> SpentItems,
    float HostTime);

public record GameResult(
    int RoomId,
    int[] UserIds,
    GameResultFirst First);