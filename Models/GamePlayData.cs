using Microsoft.EntityFrameworkCore;

namespace tori.Models;

[PrimaryKey(nameof(RoomId), nameof(UserId))]
public class GamePlayData
{
    public int RoomId { get; set; }
    
    public int UserId { get; set; }
    
    public GameUser GameUser { get; set; } = default!;

    public string UseItems { get; set; } = default!;
    
    public DateTime TimeStamp { get; set; }
}