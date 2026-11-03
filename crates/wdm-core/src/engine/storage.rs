use std::fs::File;
use std::io;
use std::path::{Path, PathBuf};
use std::sync::{Arc, Mutex};

use bytes::Bytes;

/// Concurrent direct-offset file writer.
/// Multiple workers write directly into their disjoint byte ranges in the destination file,
/// avoiding any post-download file assembly phase.
pub struct StorageWriter {
    path: PathBuf,
    total_bytes: i64,
    file: Mutex<Option<Arc<File>>>,
}

#[cfg(windows)]
fn write_all_at(file: &File, mut buf: &[u8], mut offset: u64) -> io::Result<()> {
    use std::os::windows::fs::FileExt;
    while !buf.is_empty() {
        let n = file.seek_write(buf, offset)?;
        if n == 0 {
            return Err(io::ErrorKind::WriteZero.into());
        }
        buf = &buf[n..];
        offset += n as u64;
    }
    Ok(())
}

#[cfg(unix)]
fn write_all_at(file: &File, buf: &[u8], offset: u64) -> io::Result<()> {
    std::os::unix::fs::FileExt::write_all_at(file, buf, offset)
}

impl StorageWriter {
    pub fn new(path: impl Into<PathBuf>, total_bytes: i64) -> io::Result<Self> {
        let path = path.into();
        if let Some(dir) = path.parent() {
            if !dir.as_os_str().is_empty() {
                std::fs::create_dir_all(dir)?;
            }
        }

        let file = File::options().read(true).write(true).create(true).truncate(false).open(&path)?;
        // Pre-allocate when the size is known to prevent fragmentation and reserve space upfront.
        // Unknown size: start from an empty file (unknown-length downloads cannot resume).
        file.set_len(total_bytes.max(0) as u64)?;

        Ok(Self { path, total_bytes, file: Mutex::new(Some(Arc::new(file))) })
    }

    pub fn path(&self) -> &Path {
        &self.path
    }

    pub fn total_bytes(&self) -> i64 {
        self.total_bytes
    }

    fn handle(&self) -> io::Result<Arc<File>> {
        self.file
            .lock()
            .unwrap()
            .clone()
            .ok_or_else(|| io::Error::new(io::ErrorKind::BrokenPipe, "the storage writer is closed"))
    }

    /// Writes `data` at the absolute file offset. Safe for concurrent calls on non-overlapping ranges.
    pub async fn write_at(&self, data: Bytes, offset: i64) -> io::Result<()> {
        let file = self.handle()?;
        tokio::task::spawn_blocking(move || write_all_at(&file, &data, offset as u64))
            .await
            .map_err(io::Error::other)?
    }

    /// Sets the final file length (used when the size was only learned at EOF).
    pub fn set_len(&self, len: i64) -> io::Result<()> {
        self.handle()?.set_len(len.max(0) as u64)
    }

    /// Flushes all OS buffers to disk.
    pub fn flush(&self) -> io::Result<()> {
        match self.file.lock().unwrap().as_ref() {
            Some(f) => f.sync_all(),
            None => Ok(()),
        }
    }

    /// Flushes and closes the file (needed before renaming it on Windows). Idempotent.
    pub fn close(&self) -> io::Result<()> {
        let taken = self.file.lock().unwrap().take();
        match taken {
            Some(f) => f.sync_all(),
            None => Ok(()),
        }
    }
}

impl Drop for StorageWriter {
    fn drop(&mut self) {
        let _ = self.close();
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[tokio::test]
    async fn preallocates_and_writes_at_offsets() {
        let dir = tempfile::tempdir().unwrap();
        let p = dir.path().join("a").join("f.bin");
        let w = StorageWriter::new(&p, 10).unwrap();
        assert_eq!(std::fs::metadata(&p).unwrap().len(), 10);
        w.write_at(Bytes::from_static(b"WORLD"), 5).await.unwrap();
        w.write_at(Bytes::from_static(b"HELLO"), 0).await.unwrap();
        w.close().unwrap();
        assert_eq!(std::fs::read(&p).unwrap(), b"HELLOWORLD");
    }

    #[tokio::test]
    async fn unknown_size_starts_empty_and_can_be_trimmed() {
        let dir = tempfile::tempdir().unwrap();
        let p = dir.path().join("f.bin");
        std::fs::write(&p, b"stale stale stale").unwrap();
        let w = StorageWriter::new(&p, -1).unwrap();
        assert_eq!(std::fs::metadata(&p).unwrap().len(), 0);
        w.write_at(Bytes::from_static(b"abc"), 0).await.unwrap();
        w.set_len(2).unwrap();
        w.close().unwrap();
        assert_eq!(std::fs::read(&p).unwrap(), b"ab");
    }

    #[tokio::test]
    async fn writes_after_close_fail_and_close_is_idempotent() {
        let dir = tempfile::tempdir().unwrap();
        let w = StorageWriter::new(dir.path().join("f"), 4).unwrap();
        w.close().unwrap();
        w.close().unwrap();
        assert!(w.write_at(Bytes::from_static(b"x"), 0).await.is_err());
    }

    #[tokio::test]
    async fn concurrent_disjoint_writes() {
        let dir = tempfile::tempdir().unwrap();
        let p = dir.path().join("f");
        let w = Arc::new(StorageWriter::new(&p, 8 * 1000).unwrap());
        let mut tasks = Vec::new();
        for i in 0..8u8 {
            let w = w.clone();
            tasks.push(tokio::spawn(async move {
                w.write_at(Bytes::from(vec![i; 1000]), i as i64 * 1000).await.unwrap();
            }));
        }
        for t in tasks {
            t.await.unwrap();
        }
        w.close().unwrap();
        let data = std::fs::read(&p).unwrap();
        for i in 0..8usize {
            assert!(data[i * 1000..(i + 1) * 1000].iter().all(|&b| b == i as u8));
        }
    }
}

