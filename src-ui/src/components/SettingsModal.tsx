import React, { useState } from 'react';
import { X, Settings, Folder, Gauge, Cpu } from 'lucide-react';
import { AppSettings } from '../types';

interface SettingsModalProps {
  isOpen: boolean;
  settings: AppSettings;
  onClose: () => void;
  onSave: (settings: AppSettings) => void;
  onBrowseDirectory: () => Promise<string | null>;
}

export const SettingsModal: React.FC<SettingsModalProps> = ({
  isOpen,
  settings,
  onClose,
  onSave,
  onBrowseDirectory,
}) => {
  const [activeTab, setActiveTab] = useState<'general' | 'speed' | 'engine'>('general');

  const [dir, setDir] = useState(settings.DefaultDownloadDirectory);
  const [connections, setConnections] = useState(settings.DefaultConnections);
  const [maxConcurrent, setMaxConcurrent] = useState(settings.MaxConcurrentDownloads);
  const [userAgent, setUserAgent] = useState(settings.UserAgent);
  const [autoOpen, setAutoOpen] = useState(settings.AutoOpenFileOnComplete);
  const [confirmDelete, setConfirmDelete] = useState(settings.ConfirmBeforeDelete);
  const [dynamicSplit, setDynamicSplit] = useState(settings.EnableDynamicMiddleSplit);
  const [showStartDialog, setShowStartDialog] = useState(settings.ShowStartDownloadDialog);

  const [speedLimitEnabled, setSpeedLimitEnabled] = useState(settings.IsSpeedLimiterEnabled);
  const [speedLimitKb, setSpeedLimitKb] = useState(settings.DefaultSpeedLimitKb);

  if (!isOpen) return null;

  const handleBrowse = async () => {
    const selected = await onBrowseDirectory();
    if (selected) {
      setDir(selected);
    }
  };

  const handleSave = () => {
    onSave({
      ...settings,
      DefaultDownloadDirectory: dir,
      DefaultConnections: connections,
      MaxConcurrentDownloads: maxConcurrent,
      UserAgent: userAgent,
      AutoOpenFileOnComplete: autoOpen,
      ConfirmBeforeDelete: confirmDelete,
      EnableDynamicMiddleSplit: dynamicSplit,
      ShowStartDownloadDialog: showStartDialog,
      IsSpeedLimiterEnabled: speedLimitEnabled,
      DefaultSpeedLimitKb: speedLimitKb,
    });
    onClose();
  };

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal-card" role="dialog" aria-modal="true" aria-label="Settings" onClick={(e) => e.stopPropagation()} style={{ width: '560px' }}>
        {/* Header */}
        <div style={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          padding: '14px 18px',
          borderBottom: '1px solid var(--border)',
        }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: '8px', fontWeight: 600, fontSize: '14px' }}>
            <Settings size={18} color="var(--accent)" />
            <span>Options & Preferences</span>
          </div>
          <button className="btn-icon" onClick={onClose} aria-label="Close" title="Close">
            <X size={16} />
          </button>
        </div>

        {/* Tab Bar */}
        <div style={{
          display: 'flex',
          borderBottom: '1px solid var(--border)',
          background: 'var(--bg-surface-alt)',
          padding: '0 18px',
          gap: '8px',
        }}>
          <button
            onClick={() => setActiveTab('general')}
            style={{
              padding: '10px 14px',
              borderRadius: 0,
              borderBottom: activeTab === 'general' ? '2px solid var(--accent)' : '2px solid transparent',
              color: activeTab === 'general' ? 'var(--accent)' : 'var(--text-secondary)',
              fontWeight: activeTab === 'general' ? 600 : 400,
              background: 'transparent',
            }}
          >
            General
          </button>

          <button
            onClick={() => setActiveTab('speed')}
            style={{
              padding: '10px 14px',
              borderRadius: 0,
              borderBottom: activeTab === 'speed' ? '2px solid var(--accent)' : '2px solid transparent',
              color: activeTab === 'speed' ? 'var(--accent)' : 'var(--text-secondary)',
              fontWeight: activeTab === 'speed' ? 600 : 400,
              background: 'transparent',
            }}
          >
            Bandwidth Limiter
          </button>

          <button
            onClick={() => setActiveTab('engine')}
            style={{
              padding: '10px 14px',
              borderRadius: 0,
              borderBottom: activeTab === 'engine' ? '2px solid var(--accent)' : '2px solid transparent',
              color: activeTab === 'engine' ? 'var(--accent)' : 'var(--text-secondary)',
              fontWeight: activeTab === 'engine' ? 600 : 400,
              background: 'transparent',
            }}
          >
            Engine & Network
          </button>
        </div>

        {/* Body Content */}
        <div style={{ padding: '20px 18px', display: 'flex', flexDirection: 'column', gap: '16px', minHeight: '260px' }}>
          {activeTab === 'general' && (
            <>
              <div>
                <label style={{ display: 'block', fontSize: '12px', fontWeight: 600, marginBottom: '6px', color: 'var(--text-secondary)' }}>
                  Default Download Directory
                </label>
                <div style={{ display: 'flex', gap: '8px' }}>
                  <input aria-label="Default Download Directory"
                    type="text"
                    value={dir}
                    onChange={(e) => setDir(e.target.value)}
                    style={{ flex: 1 }}
                  />
                  <button className="btn-secondary" onClick={handleBrowse} type="button">
                    <Folder size={14} />
                    <span>Browse</span>
                  </button>
                </div>
              </div>

              <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '14px' }}>
                <div>
                  <label style={{ display: 'block', fontSize: '12px', fontWeight: 600, marginBottom: '6px', color: 'var(--text-secondary)' }}>
                    Default Connections per Download
                  </label>
                  <select aria-label="Default Connections per Download"
                    value={connections}
                    onChange={(e) => setConnections(Number(e.target.value))}
                    style={{ width: '100%' }}
                  >
                    {[1, 2, 4, 8, 16, 24, 32].map((c) => (
                      <option key={c} value={c}>{c} dynamic streams</option>
                    ))}
                  </select>
                </div>

                <div>
                  <label style={{ display: 'block', fontSize: '12px', fontWeight: 600, marginBottom: '6px', color: 'var(--text-secondary)' }}>
                    Maximum Concurrent Downloads
                  </label>
                  <select aria-label="Maximum Concurrent Downloads"
                    value={maxConcurrent}
                    onChange={(e) => setMaxConcurrent(Number(e.target.value))}
                    style={{ width: '100%' }}
                  >
                    {[1, 2, 3, 4, 5, 8, 10].map((c) => (
                      <option key={c} value={c}>{c} simultaneous</option>
                    ))}
                  </select>
                </div>
              </div>

              <div style={{ display: 'flex', flexDirection: 'column', gap: '10px', paddingTop: '4px' }}>
                <label style={{ display: 'flex', alignItems: 'center', gap: '8px', cursor: 'pointer', fontSize: '12.5px' }}>
                  <input
                    type="checkbox"
                    checked={autoOpen}
                    onChange={(e) => setAutoOpen(e.target.checked)}
                  />
                  <span>Automatically open files upon download completion</span>
                </label>

                <label style={{ display: 'flex', alignItems: 'center', gap: '8px', cursor: 'pointer', fontSize: '12.5px' }}>
                  <input
                    type="checkbox"
                    checked={confirmDelete}
                    onChange={(e) => setConfirmDelete(e.target.checked)}
                  />
                  <span>Prompt for confirmation before removing downloads</span>
                </label>

                <label style={{ display: 'flex', alignItems: 'center', gap: '8px', cursor: 'pointer', fontSize: '12.5px' }}>
                  <input
                    type="checkbox"
                    checked={showStartDialog}
                    onChange={(e) => setShowStartDialog(e.target.checked)}
                  />
                  <span>Always show Add Download dialog when browser sends a download</span>
                </label>
              </div>
            </>
          )}

          {activeTab === 'speed' && (
            <div style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
              <label style={{
                display: 'flex',
                alignItems: 'center',
                gap: '10px',
                cursor: 'pointer',
                padding: '12px 14px',
                background: 'var(--bg-surface-alt)',
                borderRadius: 'var(--radius-md)',
                border: '1px solid var(--border)',
              }}>
                <input
                  type="checkbox"
                  checked={speedLimitEnabled}
                  onChange={(e) => setSpeedLimitEnabled(e.target.checked)}
                  style={{ width: '16px', height: '16px' }}
                />
                <div>
                  <div style={{ fontWeight: 600, fontSize: '13px', display: 'flex', alignItems: 'center', gap: '6px' }}>
                    <Gauge size={16} color="var(--accent)" />
                    <span>Global Bandwidth Speed Limiter</span>
                  </div>
                  <div style={{ fontSize: '11.5px', color: 'var(--text-muted)' }}>
                    Throttles network usage across all active downloads combined
                  </div>
                </div>
              </label>

              <div style={{ opacity: speedLimitEnabled ? 1 : 0.4, pointerEvents: speedLimitEnabled ? 'auto' : 'none' }}>
                <label style={{ display: 'block', fontSize: '12px', fontWeight: 600, marginBottom: '6px', color: 'var(--text-secondary)' }}>
                  Maximum Bandwidth Limit (KB/s)
                </label>
                <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
                  <input aria-label="Maximum Bandwidth Limit (KB/s)"
                    type="number"
                    min={128}
                    step={128}
                    value={speedLimitKb}
                    onChange={(e) => setSpeedLimitKb(Number(e.target.value))}
                    style={{ width: '180px' }}
                  />
                  <span style={{ fontSize: '13px', color: 'var(--text-secondary)' }}>
                    ({(speedLimitKb / 1024).toFixed(2)} MB/s)
                  </span>
                </div>
              </div>
            </div>
          )}

          {activeTab === 'engine' && (
            <div style={{ display: 'flex', flexDirection: 'column', gap: '14px' }}>
              <label style={{
                display: 'flex',
                alignItems: 'center',
                gap: '10px',
                cursor: 'pointer',
                padding: '12px 14px',
                background: 'var(--bg-surface-alt)',
                borderRadius: 'var(--radius-md)',
                border: '1px solid var(--border)',
              }}>
                <input
                  type="checkbox"
                  checked={dynamicSplit}
                  onChange={(e) => setDynamicSplit(e.target.checked)}
                  style={{ width: '16px', height: '16px' }}
                />
                <div>
                  <div style={{ fontWeight: 600, fontSize: '13px', display: 'flex', alignItems: 'center', gap: '6px' }}>
                    <Cpu size={16} color="var(--accent)" />
                    <span>Middle-Split Dynamic Stream Rebalancing</span>
                  </div>
                  <div style={{ fontSize: '11.5px', color: 'var(--text-muted)' }}>
                    Dynamically splits remaining largest chunks to accelerate slow servers
                  </div>
                </div>
              </label>

              <div>
                <label style={{ display: 'block', fontSize: '12px', fontWeight: 600, marginBottom: '6px', color: 'var(--text-secondary)' }}>
                  HTTP User-Agent Header
                </label>
                <input aria-label="HTTP User-Agent Header"
                  type="text"
                  value={userAgent}
                  onChange={(e) => setUserAgent(e.target.value)}
                  style={{ width: '100%', fontSize: '11.5px', fontFamily: 'monospace' }}
                />
              </div>

              <div style={{
                padding: '10px 12px',
                background: 'var(--bg-surface-alt)',
                borderRadius: 'var(--radius-md)',
                border: '1px solid var(--border)',
                fontSize: '11.5px',
                color: 'var(--text-muted)',
                lineHeight: 1.5,
              }}>
                <div style={{ fontWeight: 600, color: 'var(--text-primary)', marginBottom: '2px' }}>
                  Zero-Merge Storage Writer
                </div>
                Partial downloads write directly into <code>*.wdm-part</code> at their true byte offsets. When completed, files are atomically verified by SHA-256 and renamed.
              </div>
            </div>
          )}
        </div>

        {/* Footer */}
        <div style={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'flex-end',
          padding: '12px 18px',
          background: 'var(--bg-surface-alt)',
          borderTop: '1px solid var(--border)',
          gap: '8px',
        }}>
          <button className="btn-secondary" onClick={onClose}>
            Cancel
          </button>
          <button className="btn-primary" onClick={handleSave}>
            Save Preferences
          </button>
        </div>
      </div>
    </div>
  );
};
