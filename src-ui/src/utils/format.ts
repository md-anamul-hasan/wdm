export function formatBytes(bytes: number): string {
  if (bytes < 0 || isNaN(bytes)) return 'Unknown';
  if (bytes === 0) return '0 B';
  const units = ['B', 'KB', 'MB', 'GB', 'TB'];
  const i = Math.floor(Math.log(bytes) / Math.log(1024));
  const val = bytes / Math.pow(1024, i);
  return `${val.toFixed(i === 0 ? 0 : i === 1 ? 1 : 2)} ${units[i]}`;
}

export function formatSpeed(bps: number): string {
  if (!bps || bps <= 0 || !isFinite(bps)) return '0 KB/s';
  if (bps < 1024 * 1024) {
    return `${(bps / 1024).toFixed(1)} KB/s`;
  }
  return `${(bps / (1024 * 1024)).toFixed(2)} MB/s`;
}

export function formatEta(seconds?: number): string {
  if (!seconds || seconds <= 0 || !isFinite(seconds)) return '--:--';
  const s = Math.floor(seconds);
  const m = Math.floor(s / 60);
  const remSec = s % 60;
  if (m < 60) {
    return `${String(m).padStart(2, '0')}:${String(remSec).padStart(2, '0')}`;
  }
  const h = Math.floor(m / 60);
  const remMin = m % 60;
  return `${String(h).padStart(2, '0')}:${String(remMin).padStart(2, '0')}:${String(remSec).padStart(2, '0')}`;
}

export function formatDate(dateStr: string): string {
  if (!dateStr) return '';
  try {
    const d = new Date(dateStr);
    return d.toLocaleDateString(undefined, {
      month: 'short',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    });
  } catch {
    return dateStr;
  }
}

export function getCategoryFromFileName(fileName: string): string {
  const ext = fileName.split('.').pop()?.toLowerCase() || '';
  const videoExts = ['mp4', 'mkv', 'avi', 'webm', 'mov', 'm4v', 'wmv', 'flv', 'mpg', 'mpeg', '3gp', 'ogv', 'm3u8', 'ts'];
  const musicExts = ['mp3', 'wav', 'flac', 'aac', 'ogg', 'm4a', 'wma'];
  const compExts = ['zip', 'rar', '7z', 'tar', 'gz', 'bz2', 'xz', 'iso'];
  const docExts = ['pdf', 'doc', 'docx', 'txt', 'rtf', 'csv', 'xls', 'xlsx', 'ppt', 'pptx', 'epub'];
  const progExts = ['exe', 'msi', 'apk', 'bat', 'cmd', 'pkg'];
  const imgExts = ['png', 'jpg', 'jpeg', 'webp', 'gif', 'svg'];

  if (videoExts.includes(ext)) return 'Video';
  if (musicExts.includes(ext)) return 'Music';
  if (compExts.includes(ext)) return 'Compressed';
  if (docExts.includes(ext)) return 'Documents';
  if (progExts.includes(ext)) return 'Programs';
  if (imgExts.includes(ext)) return 'Images';
  return 'Other';
}
