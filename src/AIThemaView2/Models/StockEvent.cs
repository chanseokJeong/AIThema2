using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AIThemaView2.Models
{
    public class StockEvent
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        public DateTime EventTime { get; set; }

        [Required]
        [MaxLength(500)]
        public string Title { get; set; } = string.Empty;

        [MaxLength(2000)]
        public string? Description { get; set; }

        [Required]
        [MaxLength(100)]
        public string Source { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string? SourceUrl { get; set; }

        [Required]
        [MaxLength(50)]
        public string Category { get; set; } = string.Empty;

        public bool IsImportant { get; set; }

        [MaxLength(500)]
        public string? Tags { get; set; }

        [MaxLength(20)]
        public string? RelatedStockCode { get; set; }

        [MaxLength(200)]
        public string? RelatedStockName { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public DateTime? UpdatedAt { get; set; }

        [Required]
        [MaxLength(64)]
        public string Hash { get; set; } = string.Empty;

        // Computed properties for display
        [NotMapped]
        public string TimeDisplay => EventTime.ToString("HH:mm");

        [NotMapped]
        public string CategoryColor => Category switch
        {
            "공시" => "#FF5722",
            "이벤트" => "#2196F3",
            "뉴스" => "#4CAF50",
            "실적" => "#FF9800",
            "공모주" => "#9C27B0",
            "FOMC" => "#E91E63",
            "경제지표" => "#00BCD4",
            "휴장" => "#F44336",
            "의무보호해제" => "#FF6B6B",
            _ => "#757575"
        };

        [NotMapped]
        public System.Windows.FontWeight TitleFontWeight => IsImportant
            ? System.Windows.FontWeights.Bold
            : System.Windows.FontWeights.Normal;

        [NotMapped]
        public string TitleColor => IsImportant ? "#FFD700" : "#E0E0E0";
    }
}
