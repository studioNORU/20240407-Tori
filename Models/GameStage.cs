using System.ComponentModel.DataAnnotations;

namespace tori.Models;

public class GameStage
{
    [Key]
    public string StageId { get; set; } = default!;
    
    [Required]
    public int MaxPlayer { get; set; }
    
    [Required]
    public int Time { get; set; }

    [Required]
    public string AiPoolId { get; set; } = default!;
}