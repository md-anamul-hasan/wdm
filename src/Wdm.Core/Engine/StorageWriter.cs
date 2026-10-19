using Microsoft.Win32.SafeHandles;

namespace Wdm.Core.Engine;

/// <summary>
/// High-performance concurrent direct-offset file writer.
/// Multiple workers write directly into their disjoint byte ranges in the destination file,
/// completely avoiding IDM's legacy post-download file assembly phase.
/// </summary>
public class StorageWriter : IAsyncDisposable
{
    private readonly string _filePath;
    private readonly SafeFileHandle _fileHandle;
    private readonly long _totalBytes;
    private bool _disposed;

    public string FilePath => _filePath;
    public long TotalBytes => _totalBytes;

    public StorageWriter(string filePath, long totalBytes)
    {
        _filePath = filePath;
        _totalBytes = totalBytes;

        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // Open handle with asynchronous flag, allowing concurrent non-overlapping random-access writes
        _fileHandle = File.OpenHandle(
            filePath,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.ReadWrite,
            FileOptions.Asynchronous | FileOptions.RandomAccess
        );

        // Pre-allocate file size if known to prevent NTFS fragmentation and reserve space upfront
        if (totalBytes > 0)
        {
            RandomAccess.SetLength(_fileHandle, totalBytes);
        }
    }

    /// <summary>
    /// Writes a memory buffer directly at the specified absolute file offset.
    /// Thread-safe for non-overlapping byte offsets.
    /// </summary>
    public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, long fileOffset, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return RandomAccess.WriteAsync(_fileHandle, buffer, fileOffset, cancellationToken);
    }

    /// <summary>
    /// Flushes all OS buffers to disk.
    /// </summary>
    public void Flush()
    {
        if (!_disposed && !_fileHandle.IsClosed)
        {
            RandomAccess.FlushToDisk(_fileHandle);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (!_fileHandle.IsClosed)
        {
            RandomAccess.FlushToDisk(_fileHandle);
            _fileHandle.Dispose();
        }

        await ValueTask.CompletedTask;
    }
}
