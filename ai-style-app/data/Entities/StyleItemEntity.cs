using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AiStyleApp.Data.Entities;

[Table("style_items")]
public class StyleItemEntity
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("user_id")]
    [MaxLength(128)]
    public required string UserId { get; set; }

    [Column("name")]
    [MaxLength(200)]
    public required string Name { get; set; }

    [Column("description")]
    [MaxLength(2000)]
    public required string Description { get; set; }

    [Column("prompt")]
    [MaxLength(4000)]
    public required string Prompt { get; set; }

    [Column("created_at_utc")]
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    [Column("updated_at_utc")]
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<StyleJobEntity> Jobs { get; set; } = new List<StyleJobEntity>();
}
