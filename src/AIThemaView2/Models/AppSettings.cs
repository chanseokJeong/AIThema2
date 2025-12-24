using System;

namespace AIThemaView2.Models
{
    public class AppSettings
    {
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }

    public class DataSourceStatus
    {
        public int Id { get; set; }
        public string SourceName { get; set; } = string.Empty;
        public DateTime? LastSuccessfulFetch { get; set; }
        public DateTime? LastAttempt { get; set; }
        public bool IsEnabled { get; set; } = true;
        public int ErrorCount { get; set; }
        public string? LastError { get; set; }
    }
}
