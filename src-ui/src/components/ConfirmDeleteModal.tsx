import React, { useState } from 'react';
import { AlertTriangle, X } from 'lucide-react';
import { DownloadRecord } from '../types';

interface ConfirmDeleteModalProps {
  isOpen: boolean;
  download: DownloadRecord | null;
  onClose: () => void;
  onConfirm: (id: string, deleteFile: boolean) => void;
}

export const ConfirmDeleteModal: React.FC<ConfirmDeleteModalProps> = ({
  isOpen,
  download,
  onClose,
  onConfirm,
}) => {
  const [deleteFile, setDeleteFile] = useState(false);

  if (!isOpen || !download) return null;

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal-card" onClick={(e) => e.stopPropagation()} style={{ width: '420px' }}>
        <div style={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          padding: '14px 18px',
          borderBottom: '1px solid var(--border)',
        }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: '8px', fontWeight: 600, fontSize: '14px' }}>
            <AlertTriangle size={18} color="#d83b01" />
            <span>Remove Download</span>
          </div>
          <button className="btn-icon" onClick={onClose}>
            <X size={16} />
          </button>
        </div>

        <div style={{ padding: '18px', display: 'flex', flexDirection: 'column', gap: '14px' }}>
          <p style={{ fontSize: '13px', lineHeight: 1.5 }}>
            Are you sure you want to remove <strong>{download.FileName}</strong> from the download list?
          </p>

          <label style={{
            display: 'flex',
            alignItems: 'center',
            gap: '8px',
            fontSize: '12.5px',
            cursor: 'pointer',
            padding: '8px 10px',
            background: 'var(--bg-surface-alt)',
            borderRadius: 'var(--radius-md)',
          }}>
            <input
              type="checkbox"
              checked={deleteFile}
              onChange={(e) => setDeleteFile(e.target.checked)}
            />
            <span>Also delete the downloaded file from disk</span>
          </label>
        </div>

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
          <button
            style={{
              background: '#d83b01',
              color: '#ffffff',
              fontWeight: 600,
            }}
            onClick={() => {
              onConfirm(download.Id, deleteFile);
              onClose();
            }}
          >
            Remove
          </button>
        </div>
      </div>
    </div>
  );
};
