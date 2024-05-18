namespace tori.AppApi.Model;

public record UserInfo(
    string Nickname,
    int WinCount,
    ItemInfo[] Inventory,
    int Energy);