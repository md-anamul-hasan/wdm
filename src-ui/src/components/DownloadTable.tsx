import React, { useState } from 'react';
import { 
  FileText, Film, Music, FolderArchive, Cpu, Image, File,
  ExternalLink, FolderOpen, Play, Pause, Trash2, Copy
} from 'lucide-react';
import { DownloadProgress, DownloadRecord } from '../types';
import { formatBytes, formatDate, formatEta, formatSpeed, getCategoryFromFileName } from '../utils/format';

interface DownloadTableProps {
  downloads: DownloadRecord[];
  selectedId: string | null;
  activeProgress: Record<string, DownloadProgress>;
  onSelect: (id: string) => void;
  onResume: (id: string) => void;
  onPause: (id: string) => void;
  onDelete: (id: string) => void;
  onOpenFile: (path: string) => void;
  onOpenFolder: (path: string) => void;
}

export const DownloadTable: React.FC<DownloadTableProps> = ({
  downloads,
  selectedId,
  activeProgress,
  onSelect,
  onResume,
  onPause,
  onDelete,
  onOpenFile,
  onOpenFolder,
}) => {
  const [contextMenu, setContextMenu] = useState<{
    x: number;
    y: number;
    download: DownloadRecord;
  } | null>(null);

  const getFileIcon = (fileName: string) => {
    const category = getCategoryFromFileName(fileName);
    switch (category) {
      case 'Video': return <Film size={16} color="#d83b01" />;
      case 'Music': return <Music size={16} color="#e3008c" />;
      case 'Compressed': return <FolderArchive size={16} color="#ca5010" />;
      case 'Documents': return <FileText size={16} color="#0078d4" />;
      case 'Programs': return <Cpu size={16} color="#107c41" />;
      case 'Images': return <Image size={16} color="#8764b8" />;
      default: return <File size={16} color="var(--text-secondary)" />;
    }
  };

  const getStatusBadge = (status: string) => {
    let color = 'var(--text-secondary)';
    let bg = 'var(--bg-surface-alt)';

    switch (status) {
      case 'Downloading':
      case 'Connecting':
        color = 'var(--status-downloading)';
        bg = 'var(--status-downloading-bg)';
        break;
      case 'Completed':
        color = 'var(--status-completed)';
        bg = 'var(--status-completed-bg)';
        break;
      case 'Paused':
        color = 'var(--status-paused)';
        bg = 'var(--status-paused-bg)';
        break;
      case 'Queued':
        color = 'var(--status-queued)';
        bg = 'var(--status-queued-bg)';
        break;
      case 'Error':
        color = 'var(--status-error)';
        bg = 'var(--status-error-bg)';
        break;
    }

    return (
      <span style={{
        padding: '3px 8px',
        borderRadius: 'var(--radius-full)',
        fontSize: '11px',
        fontWeight: 600,
        color,
        background: bg,
        display: 'inline-block',
      }}>
        {status}
      </span>
    );
  };

  const handleContextMenu = (e: React.MouseEvent, download: DownloadRecord) => {
    e.preventDefault();
    onSelect(download.Id);
    setContextMenu({
      x: e.clientX,
      y: e.clientY,
      download,
    });
  };

  return (
    <div 
      style={{
        flex: 1,
        display: 'flex',
        flexDirection: 'column',
        overflow: 'hidden',
        background: 'var(--bg-window)',
        position: 'relative',
      }}
      onClick={() => setContextMenu(null)}
    >
      {/* Table Header */}
      <div style={{
        display: 'grid',
        gridTemplateColumns: 'minmax(220px, 3fr) 100px 110px 180px 100px 90px 130px',
        padding: '8px 16px',
        background: 'var(--bg-surface)',
        borderBottom: '1px solid var(--border)',
        fontSize: '12px',
        fontWeight: 600,
        color: 'var(--text-muted)',
      }}>
        <div>File Name</div>
        <div>Size</div>
        <div>Status</div>
        <div>Progress</div>
        <div>Speed</div>
        <div>Time Left</div>
        <div>Added</div>
      </div>

      {/* Table Body */}
      <div style={{ flex: 1, overflowY: 'auto' }}>
        {downloads.length === 0 ? (
          <div style={{
            display: 'flex',
            flexDirection: 'column',
            alignItems: 'center',
            justifyContent: 'center',
            height: '100%',
            color: 'var(--text-muted)',
            gap: '8px',
          }}>
            <File size={36} strokeWidth={1.5} />
            <p>No downloads match the current filter</p>
          </div>
        ) : (
          downloads.map((d) => {
            const isSelected = selectedId === d.Id;
            const liveProg = activeProgress[d.Id];

            const total = liveProg ? liveProg.TotalBytes : d.TotalBytes;
            const downloaded = liveProg ? liveProg.DownloadedBytes : d.DownloadedBytes;
            const speed = liveProg ? liveProg.SpeedBytesPerSecond : 0;
            const eta = liveProg ? liveProg.EstimatedSecondsRemaining : undefined;

            const percentage = total > 0 ? Math.min(100, Math.max(0, (downloaded / total) * 100)) : (d.Status === 'Completed' ? 100 : 0);

            return (
              <div
                key={d.Id}
                className="download-row"
                onClick={() => onSelect(d.Id)}
                onDoubleClick={() => onOpenFile(d.TargetFilePath)}
                onContextMenu={(e) => handleContextMenu(e, d)}
                style={{
                  display: 'grid',
                  gridTemplateColumns: 'minmax(220px, 3fr) 100px 110px 180px 100px 90px 130px',
                  alignItems: 'center',
                  padding: '9px 16px',
                  borderBottom: '1px solid var(--border)',
                  background: isSelected ? 'var(--bg-surface-selected)' : 'transparent',
                  cursor: 'pointer',
                  fontSize: '12.5px',
                  transition: 'background 0.1s',
                }}
                onMouseEnter={(e) => {
                  if (!isSelected) e.currentTarget.style.background = 'var(--bg-surface-hover)';
                }}
                onMouseLeave={(e) => {
                  if (!isSelected) e.currentTarget.style.background = 'transparent';
                }}
              >
                {/* File Name */}
                <div style={{
                  display: 'flex',
                  alignItems: 'center',
                  gap: '10px',
                  overflow: 'hidden',
                  textOverflow: 'ellipsis',
                  whiteSpace: 'nowrap',
                  paddingRight: '12px',
                }}>
                  {getFileIcon(d.FileName)}
                  <span style={{
                    fontWeight: 500,
                    overflow: 'hidden',
                    textOverflow: 'ellipsis',
                    whiteSpace: 'nowrap',
                  }} title={d.FileName}>
                    {d.FileName}
                  </span>
                </div>

                {/* Size */}
                <div style={{ color: 'var(--text-secondary)' }}>
                  {formatBytes(total)}
                </div>

                {/* Status */}
                <div>
                  {getStatusBadge(d.Status)}
                </div>

                {/* Progress */}
                <div style={{ paddingRight: '16px' }}>
                  <div style={{
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'space-between',
                    fontSize: '11px',
                    marginBottom: '3px',
                    color: 'var(--text-secondary)',
                  }}>
                    <span>{percentage.toFixed(1)}%</span>
                    {total > 0 && (
                      <span>{formatBytes(downloaded)} / {formatBytes(total)}</span>
                    )}
                  </div>
                  <div style={{
                    width: '100%',
                    height: '5px',
                    background: 'var(--border)',
                    borderRadius: 'var(--radius-full)',
                    overflow: 'hidden',
                  }}>
                    <div style={{
                      width: `${percentage}%`,
                      height: '100%',
                      background: d.Status === 'Completed' ? 'var(--status-completed)' : 'var(--accent)',
                      borderRadius: 'var(--radius-full)',
                      transition: 'width 0.2s linear',
                    }} />
                  </div>
                </div>

                {/* Speed */}
                <div style={{ color: 'var(--text-secondary)' }}>
                  {speed > 0 ? formatSpeed(speed) : '-'}
                </div>

                {/* Time Left */}
                <div style={{ color: 'var(--text-secondary)' }}>
                  {speed > 0 ? formatEta(eta) : '-'}
                </div>

                {/* Added */}
                <div style={{ color: 'var(--text-muted)', fontSize: '11.5px' }}>
                  {formatDate(d.AddedAt)}
                </div>
              </div>
            );
          })
        )}
      </div>

      {/* Context Menu */}
      {contextMenu && (
        <div 
          style={{
            position: 'fixed',
            left: contextMenu.x,
            top: contextMenu.y,
            background: 'var(--bg-surface)',
            border: '1px solid var(--border)',
            borderRadius: 'var(--radius-md)',
            boxShadow: 'var(--shadow-md)',
            padding: '4px 0',
            zIndex: 1000,
            minWidth: '180px',
          }}
          onClick={(e) => e.stopPropagation()}
        >
          {contextMenu.download.Status === 'Completed' ? (
            <div
              style={{ display: 'flex', alignItems: 'center', gap: '8px', padding: '7px 14px', cursor: 'pointer' }}
              onClick={() => {
                onOpenFile(contextMenu.download.TargetFilePath);
                setContextMenu(null);
              }}
              onMouseEnter={(e) => (e.currentTarget.style.background = 'var(--bg-surface-hover)')}
              onMouseLeave={(e) => (e.currentTarget.style.background = 'transparent')}
            >
              <ExternalLink size={14} />
              <span>Open File</span>
            </div>
          ) : (
            <>
              {['Downloading', 'Connecting'].includes(contextMenu.download.Status) ? (
                <div
                  style={{ display: 'flex', alignItems: 'center', gap: '8px', padding: '7px 14px', cursor: 'pointer' }}
                  onClick={() => {
                    onPause(contextMenu.download.Id);
                    setContextMenu(null);
                  }}
                  onMouseEnter={(e) => (e.currentTarget.style.background = 'var(--bg-surface-hover)')}
                  onMouseLeave={(e) => (e.currentTarget.style.background = 'transparent')}
                >
                  <Pause size={14} />
                  <span>Pause</span>
                </div>
              ) : (
                <div
                  style={{ display: 'flex', alignItems: 'center', gap: '8px', padding: '7px 14px', cursor: 'pointer' }}
                  onClick={() => {
                    onResume(contextMenu.download.Id);
                    setContextMenu(null);
                  }}
                  onMouseEnter={(e) => (e.currentTarget.style.background = 'var(--bg-surface-hover)')}
                  onMouseLeave={(e) => (e.currentTarget.style.background = 'transparent')}
                >
                  <Play size={14} />
                  <span>Resume</span>
                </div>
              )}
            </>
          )}

          <div
            style={{ display: 'flex', alignItems: 'center', gap: '8px', padding: '7px 14px', cursor: 'pointer' }}
            onClick={() => {
              onOpenFolder(contextMenu.download.TargetFilePath);
              setContextMenu(null);
            }}
            onMouseEnter={(e) => (e.currentTarget.style.background = 'var(--bg-surface-hover)')}
            onMouseLeave={(e) => (e.currentTarget.style.background = 'transparent')}
          >
            <FolderOpen size={14} />
            <span>Open Folder</span>
          </div>

          <div
            style={{ display: 'flex', alignItems: 'center', gap: '8px', padding: '7px 14px', cursor: 'pointer' }}
            onClick={() => {
              navigator.clipboard.writeText(contextMenu.download.Url);
              setContextMenu(null);
            }}
            onMouseEnter={(e) => (e.currentTarget.style.background = 'var(--bg-surface-hover)')}
            onMouseLeave={(e) => (e.currentTarget.style.background = 'transparent')}
          >
            <Copy size={14} />
            <span>Copy URL</span>
          </div>

          <div style={{ height: '1px', background: 'var(--border)', margin: '4px 0' }} />

          <div
            style={{ display: 'flex', alignItems: 'center', gap: '8px', padding: '7px 14px', cursor: 'pointer', color: 'var(--status-error)' }}
            onClick={() => {
              onDelete(contextMenu.download.Id);
              setContextMenu(null);
            }}
            onMouseEnter={(e) => (e.currentTarget.style.background = 'var(--bg-surface-hover)')}
            onMouseLeave={(e) => (e.currentTarget.style.background = 'transparent')}
          >
            <Trash2 size={14} />
            <span>Delete</span>
          </div>
        </div>
      )}
    </div>
  );
};

