namespace tori.AppApi.Model;

public record GameResultFirst(
    int RoomId,
    Dictionary<int, int> SpentItems,
    float HostTime);

public record GameResult(
    int RoomId,
    int[] UserIds,
    GameResultFirst First);