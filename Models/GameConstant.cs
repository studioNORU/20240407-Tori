using System.ComponentModel.DataAnnotations;

namespace tori.Models;

public class GameConstant
{
    [Key]
    public string Key { get; set; } = default!;
    
    [Required]
    public int Value { get; set; }
}