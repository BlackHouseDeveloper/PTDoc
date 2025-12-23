using System.ComponentModel.DataAnnotations;

namespace PTDoc.Models;

public class AppState
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Key { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Value { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    public DateTime? LastModifiedDate { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }
}
