using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;

namespace tori.Models;

public class GameLog
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; init; }

    [NotNull]
    public string LogType { get; init; } = default!;

    public string? UserId { get; init; }
    
    public int? RoomId { get; init; }

    [NotNull]
    public string ClientVersion { get; init; } = default!;
    
    public string? Message { get; init; }
    
    public string? DataJson { get; init; }
    
    [NotNull]
    public DateTime CreatedAtKST { get; init; }
}