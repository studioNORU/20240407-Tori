using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
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

    [JsonIgnore]
    public Dictionary<int, int> SpentItems => JsonSerializer.Deserialize<Dictionary<int, int>>(this.UseItems)!;
}