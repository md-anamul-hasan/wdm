import React, { useState } from 'react';
import { X, Folder, Download } from 'lucide-react';
import { ExternalDownloadRequest } from '../types';

interface AddDownloadModalProps {
  initialUrl?: string;
  defaultDirectory: string;
  defaultConnections: number;
  isOpen: boolean;
  onClose: () => void;
  onSubmit: (request: ExternalDownloadRequest) => void;
  onBrowseDirectory: () => Promise<string | null>;
}

export const AddDownloadModal: React.FC<AddDownloadModalProps> = ({
  initialUrl = '',
  defaultDirectory,
  defaultConnections,
  isOpen,
  onClose,
  onSubmit,
  onBrowseDirectory,
}) => {
  const [url, setUrl] = useState(initialUrl);
  const [directory, setDirectory] = useState(defaultDirectory);
  const [fileName, setFileName] = useState('');
  const [connections, setConnections] = useState(defaultConnections);
  const [sha256, setSha256] = useState('');

  if (!isOpen) return null;

  const handleBrowse = async () => {
    const selected = await onBrowseDirectory();
    if (selected) {
      setDirectory(selected);
    }
  };

  const handleSubmit = (queueOnly: boolean) => {
    if (!url.trim()) return;
    onSubmit({
      Urls: [url.trim()],
      DestinationDirectory: directory.trim() || undefined,
      FileName: fileName.trim() || undefined,
      Sha256: sha256.trim() || undefined,
      QueueOnly: queueOnly,
      Silent: false,
    });
    onClose();
  };

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal-card" onClick={(e) => e.stopPropagation()} style={{ width: '500px' }}>
        {/* Header */}
        <div style={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          padding: '14px 18px',
          borderBottom: '1px solid var(--border)',
        }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: '8px', fontWeight: 600, fontSize: '14px' }}>
            <Download size={18} color="var(--accent)" />
            <span>Add Download</span>
          </div>
          <button className="btn-icon" onClick={onClose}>
            <X size={16} />
          </button>
        </div>

        {/* Form Body */}
        <div style={{ padding: '18px', display: 'flex', flexDirection: 'column', gap: '14px' }}>
          <div>
            <label style={{ display: 'block', fontSize: '12px', fontWeight: 600, marginBottom: '6px', color: 'var(--text-secondary)' }}>
              URL Address *
            </label>
            <input
              type="text"
              placeholder="https://example.com/file.zip"
              value={url}
              onChange={(e) => setUrl(e.target.value)}
              style={{ width: '100%' }}
              autoFocus
            />
          </div>

          <div>
            <label style={{ display: 'block', fontSize: '12px', fontWeight: 600, marginBottom: '6px', color: 'var(--text-secondary)' }}>
              Save To
            </label>
            <div style={{ display: 'flex', gap: '8px' }}>
              <input
                type="text"
                value={directory}
                onChange={(e) => setDirectory(e.target.value)}
                style={{ flex: 1 }}
              />
              <button className="btn-secondary" onClick={handleBrowse} type="button">
                <Folder size={15} />
                <span>Browse</span>
              </button>
            </div>
          </div>

          <div style={{ display: 'grid', gridTemplateColumns: '2fr 1fr', gap: '12px' }}>
            <div>
              <label style={{ display: 'block', fontSize: '12px', fontWeight: 600, marginBottom: '6px', color: 'var(--text-secondary)' }}>
                File Name (Optional)
              </label>
              <input
                type="text"
                placeholder="Auto-detected from server"
                value={fileName}
                onChange={(e) => setFileName(e.target.value)}
                style={{ width: '100%' }}
              />
            </div>

            <div>
              <label style={{ display: 'block', fontSize: '12px', fontWeight: 600, marginBottom: '6px', color: 'var(--text-secondary)' }}>
                Connections
              </label>
              <select
                value={connections}
                onChange={(e) => setConnections(Number(e.target.value))}
                style={{ width: '100%' }}
              >
                {[1, 2, 4, 8, 16, 24, 32].map((c) => (
                  <option key={c} value={c}>{c} streams</option>
                ))}
              </select>
            </div>
          </div>

          <div>
            <label style={{ display: 'block', fontSize: '12px', fontWeight: 600, marginBottom: '6px', color: 'var(--text-secondary)' }}>
              Expected SHA-256 (Optional Verification)
            </label>
            <input
              type="text"
              placeholder="64-character hexadecimal checksum"
              value={sha256}
              onChange={(e) => setSha256(e.target.value)}
              style={{ width: '100%', fontFamily: 'monospace', fontSize: '12px' }}
            />
          </div>
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
          <button className="btn-secondary" onClick={onClose} type="button">
            Cancel
          </button>
          <button 
            className="btn-secondary" 
            onClick={() => handleSubmit(true)} 
            disabled={!url.trim()}
            type="button"
          >
            Start Later
          </button>
          <button 
            className="btn-primary" 
            onClick={() => handleSubmit(false)} 
            disabled={!url.trim()}
            type="button"
          >
            Download Now
          </button>
        </div>
      </div>
    </div>
  );
};

