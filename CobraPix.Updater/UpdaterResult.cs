using System;

namespace CobraPix.Updater
{
    public class UpdaterResult
    {
        public bool Success { get; set; }
        public bool RollbackPerformed { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? BackupDirectory { get; set; }
        public string? StagingDirectory { get; set; }
        public Exception? Exception { get; set; }

        public static UpdaterResult Ok(string message) => new()
        {
            Success = true,
            Message = message
        };

        public static UpdaterResult Fail(string message, bool rollbackPerformed = false, Exception? ex = null) => new()
        {
            Success = false,
            RollbackPerformed = rollbackPerformed,
            Message = message,
            Exception = ex
        };
    }
}
