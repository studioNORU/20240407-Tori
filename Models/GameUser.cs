using Microsoft.EntityFrameworkCore;

namespace tori.Models;

public enum PlayStatus
{
    Ready,
    Playing,
    Quit,
    Disconnected,
    Done
}

[PrimaryKey(nameof(RoomId), nameof(UserId))]
public class GameUser
{
    public int RoomId { get; set; }
    
    public int UserId { get; set; }
    
    public PlayStatus Status { get; set; }
    
    public DateTime? JoinedAt { get; set; }
    
    public DateTime? LeavedAt { get; set; }

    public GamePlayData? PlayData { get; set; }
}