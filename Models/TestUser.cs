using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using tori.AppApi.Model;

namespace tori.Models;

#if !RELEASE
public record TestUserInfo(
        int UserId,
        string Nickname,
        int WinCount,
        ItemInfo[] Inventory,
        int Energy)
    : UserInfo(Nickname, WinCount, Inventory, Energy);

public class TestUser
{
    [Key]
    public int Id { get; set; }

    public string Nickname { get; set; } = default!;

    public int WinCount { get; set; }

    public string InventoryJson { get; set; } = default!;
    
    public int Energy { get; set; }
    
    public DateTime ExpireAt { get; set; }

    public TestUserInfo ToUserInfo() => new(
        this.Id,
        this.Nickname,
        this.WinCount,
        JsonSerializer.Deserialize<ItemInfo[]>(this.InventoryJson)!,
        this.Energy);
}
#endif