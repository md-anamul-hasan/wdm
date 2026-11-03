import React, { useState } from 'react';
import { X, Clock, Power } from 'lucide-react';
import { AppSettings } from '../types';

interface SchedulerModalProps {
  isOpen: boolean;
  settings: AppSettings;
  onClose: () => void;
  onSave: (settings: AppSettings) => void;
}

export const SchedulerModal: React.FC<SchedulerModalProps> = ({
  isOpen,
  settings,
  onClose,
  onSave,
}) => {
  const [enabled, setEnabled] = useState(settings.SchedulerEnabled);
  const [startTime, setStartTime] = useState(settings.SchedulerStartTime || '02:00');
  const [stopTime, setStopTime] = useState(settings.SchedulerStopTime || '06:00');
  const [stopQueue, setStopQueue] = useState(settings.SchedulerStopQueueAtEndTime);
  const [shutdown, setShutdown] = useState(settings.SchedulerShutdownOnComplete);

  if (!isOpen) return null;

  const handleSave = () => {
    onSave({
      ...settings,
      SchedulerEnabled: enabled,
      SchedulerStartTime: startTime,
      SchedulerStopTime: stopTime,
      SchedulerStopQueueAtEndTime: stopQueue,
      SchedulerShutdownOnComplete: shutdown,
    });
    onClose();
  };

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal-card" role="dialog" aria-modal="true" aria-label="Scheduler" onClick={(e) => e.stopPropagation()} style={{ width: '460px' }}>
        {/* Header */}
        <div style={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          padding: '14px 18px',
          borderBottom: '1px solid var(--border)',
        }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: '8px', fontWeight: 600, fontSize: '14px' }}>
            <Clock size={18} color="var(--accent)" />
            <span>Download Scheduler</span>
          </div>
          <button className="btn-icon" onClick={onClose} aria-label="Close" title="Close">
            <X size={16} />
          </button>
        </div>

        {/* Form Body */}
        <div style={{ padding: '20px 18px', display: 'flex', flexDirection: 'column', gap: '16px' }}>
          {/* Main Toggle */}
          <label style={{
            display: 'flex',
            alignItems: 'center',
            gap: '10px',
            cursor: 'pointer',
            padding: '10px 12px',
            background: 'var(--bg-surface-alt)',
            borderRadius: 'var(--radius-md)',
            border: '1px solid var(--border)',
          }}>
            <input
              type="checkbox"
              checked={enabled}
              onChange={(e) => setEnabled(e.target.checked)}
              style={{ width: '16px', height: '16px' }}
            />
            <div>
              <div style={{ fontWeight: 600, fontSize: '13px' }}>Enable automated daily scheduler</div>
              <div style={{ fontSize: '11.5px', color: 'var(--text-muted)' }}>
                Start and stop the download queue automatically at set times
              </div>
            </div>
          </label>

          {/* Time pickers */}
          <div style={{
            display: 'grid',
            gridTemplateColumns: '1fr 1fr',
            gap: '12px',
            opacity: enabled ? 1 : 0.5,
            pointerEvents: enabled ? 'auto' : 'none',
          }}>
            <div>
              <label style={{ display: 'block', fontSize: '12px', fontWeight: 600, marginBottom: '6px', color: 'var(--text-secondary)' }}>
                Start Download Queue
              </label>
              <input aria-label="Start Download Queue"
                type="time"
                value={startTime}
                onChange={(e) => setStartTime(e.target.value)}
                style={{ width: '100%', height: '34px' }}
              />
            </div>

            <div>
              <label style={{ display: 'block', fontSize: '12px', fontWeight: 600, marginBottom: '6px', color: 'var(--text-secondary)' }}>
                Stop Download Queue
              </label>
              <input aria-label="Stop Download Queue"
                type="time"
                value={stopTime}
                onChange={(e) => setStopTime(e.target.value)}
                style={{ width: '100%', height: '34px' }}
              />
            </div>
          </div>

          {/* Options */}
          <div style={{
            display: 'flex',
            flexDirection: 'column',
            gap: '10px',
            opacity: enabled ? 1 : 0.5,
            pointerEvents: enabled ? 'auto' : 'none',
          }}>
            <label style={{ display: 'flex', alignItems: 'center', gap: '8px', cursor: 'pointer', fontSize: '12.5px' }}>
              <input
                type="checkbox"
                checked={stopQueue}
                onChange={(e) => setStopQueue(e.target.checked)}
              />
              <span>Pause all downloading streams when stop time is reached</span>
            </label>

            <label style={{ display: 'flex', alignItems: 'center', gap: '8px', cursor: 'pointer', fontSize: '12.5px' }}>
              <input
                type="checkbox"
                checked={shutdown}
                onChange={(e) => setShutdown(e.target.checked)}
              />
              <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
                <Power size={14} color="#d83b01" />
                <span>Shut down computer after queue completion</span>
              </div>
            </label>
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
          <button className="btn-secondary" onClick={onClose}>
            Cancel
          </button>
          <button className="btn-primary" onClick={handleSave}>
            Save Schedule
          </button>
        </div>
      </div>
    </div>
  );
};
