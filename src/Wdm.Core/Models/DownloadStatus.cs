namespace Wdm.Core.Models;

public enum DownloadStatus
{
    Queued,
    Connecting,
    Downloading,
    Paused,
    Completed,
    Error,
    Canceled
}

public enum SegmentState
{
    Idle,
    Connecting,
    Downloading,
    Completed,
    Error
}
