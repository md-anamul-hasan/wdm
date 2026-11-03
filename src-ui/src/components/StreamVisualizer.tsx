import React from 'react';
import { DownloadProgress, DownloadRecord } from '../types';
import { formatBytes, formatSpeed } from '../utils/format';

interface StreamVisualizerProps {
  _download: DownloadRecord;
  progress?: DownloadProgress;
}

export const StreamVisualizer: React.FC<StreamVisualizerProps> = ({
  progress,
}) => {
  const segments = progress?.Segments || [];

  if (segments.length === 0) {
    return null;
  }

  return (
    <div style={{
      background: 'var(--bg-surface)',
      borderTop: '1px solid var(--border)',
      padding: '12px 16px',
      maxHeight: '200px',
      overflowY: 'auto',
    }}>
      <div style={{
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        marginBottom: '10px',
      }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
          <span style={{ fontWeight: 600, fontSize: '12.5px' }}>
            Active Dynamic Streams ({segments.length})
          </span>
          <span style={{ fontSize: '11.5px', color: 'var(--text-muted)' }}>
            Middle-Split Zero-Merge Pipeline
          </span>
        </div>
        <div style={{ fontSize: '12px', color: 'var(--text-secondary)' }}>
          Total Speed: <strong style={{ color: 'var(--accent)' }}>{formatSpeed(progress?.SpeedBytesPerSecond || 0)}</strong>
        </div>
      </div>

      <div style={{
        display: 'grid',
        gridTemplateColumns: 'repeat(auto-fill, minmax(260px, 1fr))',
        gap: '8px',
      }}>
        {segments.map((seg) => {
          const segTotal = seg.TotalBytes > 0 ? seg.TotalBytes : (seg.EndOffset ? seg.EndOffset - seg.StartOffset + 1 : 0);
          const segDownloaded = seg.DownloadedBytes;
          const segPct = segTotal > 0 ? Math.min(100, Math.max(0, (segDownloaded / segTotal) * 100)) : (seg.State === 'Completed' ? 100 : 0);

          let stateColor = 'var(--text-muted)';
          let stateLabel = seg.State;
          if (seg.State === 'Downloading') {
            stateColor = 'var(--status-downloading)';
          } else if (seg.State === 'Completed') {
            stateColor = 'var(--status-completed)';
          } else if (seg.State === 'Connecting') {
            stateColor = 'var(--status-queued)';
          }

          return (
            <div
              key={seg.SegmentId}
              style={{
                background: 'var(--bg-surface-alt)',
                border: '1px solid var(--border)',
                borderRadius: 'var(--radius-md)',
                padding: '8px 10px',
                display: 'flex',
                flexDirection: 'column',
                gap: '6px',
              }}
            >
              <div style={{
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'space-between',
                fontSize: '11.5px',
              }}>
                <span style={{ fontWeight: 600 }}>Stream #{String(seg.SegmentId).padStart(2, '0')}</span>
                <span style={{ color: stateColor, fontWeight: 600, fontSize: '11px' }}>{stateLabel}</span>
              </div>

              {/* Segment Progress Bar */}
              <div style={{
                width: '100%',
                height: '6px',
                background: 'var(--border)',
                borderRadius: 'var(--radius-full)',
                overflow: 'hidden',
              }}>
                <div style={{
                  width: `${segPct}%`,
                  height: '100%',
                  background: seg.State === 'Completed' ? 'var(--status-completed)' : 'var(--accent)',
                  borderRadius: 'var(--radius-full)',
                  transition: 'width 0.2s',
                }} />
              </div>

              <div style={{
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'space-between',
                fontSize: '11px',
                color: 'var(--text-secondary)',
              }}>
                <span>{segPct.toFixed(1)}% ({formatBytes(segDownloaded)})</span>
                <span>{formatSpeed(seg.SpeedBytesPerSecond)}</span>
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
};
