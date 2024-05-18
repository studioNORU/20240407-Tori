using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace tori.Models;

[Index(nameof(RoomId), nameof(UserId))]
public class GamePlayData
{
    [Key]
    public int Id { get; set; }
    
    public int RoomId { get; set; }
    
    public int UserId { get; set; }
    
    public string UseItems { get; set; } = default!;
    
    public DateTime TimeStamp { get; set; }
    
    public GameUser GameUser { get; set; } = default!;
}