namespace tori.AppApi.Model;

public record RoomInfo(
    int RoomId,
    int PlayerCount,
    GoodsInfo GoodsInfo,
    DateTime ExposureDay,
    DateTime BeginRunningTime,
    DateTime EndRunningTime,
    int PlayTime);