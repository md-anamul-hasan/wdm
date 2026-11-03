import React, { useState } from 'react';
import { 
  Download, Plus, Play, Pause, Trash2, 
  PlayCircle, StopCircle, Search, X, 
  Globe, Clock, Moon, Sun, Settings, MoreVertical, Layers, ListFilter
} from 'lucide-react';

interface CommandBarProps {
  canResume: boolean;
  canPause: boolean;
  hasSelection: boolean;
  isDarkMode: boolean;
  searchText: string;
  onSearchChange: (text: string) => void;
  onAddUrl: () => void;
  onResume: () => void;
  onPause: () => void;
  onDelete: () => void;
  onStartQueue: () => void;
  onStopQueue: () => void;
  onOpenGrabber: () => void;
  onOpenScheduler: () => void;
  onToggleTheme: () => void;
  onOpenSettings: () => void;
  onBatchFromClipboard: () => void;
  onBatchDownload: () => void;
}

export const CommandBar: React.FC<CommandBarProps> = ({
  canResume,
  canPause,
  hasSelection,
  isDarkMode,
  searchText,
  onSearchChange,
  onAddUrl,
  onResume,
  onPause,
  onDelete,
  onStartQueue,
  onStopQueue,
  onOpenGrabber,
  onOpenScheduler,
  onToggleTheme,
  onOpenSettings,
  onBatchFromClipboard,
  onBatchDownload,
}) => {
  const [showMore, setShowMore] = useState(false);

  return (
    <header style={{
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'space-between',
      padding: '10px 16px',
      background: 'var(--bg-surface)',
      borderBottom: '1px solid var(--border)',
      gap: '12px',
      position: 'relative',
      zIndex: 100,
    }}>
      {/* Brand & Primary Actions */}
      <div style={{ display: 'flex', alignItems: 'center', gap: '16px' }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
          <div style={{
            width: '32px',
            height: '32px',
            borderRadius: 'var(--radius-md)',
            background: 'var(--accent)',
            color: 'var(--on-accent)',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
          }}>
            <Download size={18} strokeWidth={2.5} />
          </div>
          <span style={{ fontSize: '16px', fontWeight: 'bold', letterSpacing: '-0.2px' }}>WDM</span>
        </div>

        <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
          <button className="btn-primary" onClick={onAddUrl} title="Add a download (Ctrl+N)">
            <Plus size={16} strokeWidth={2.5} />
            <span>Add URL</span>
          </button>

          <button 
            className="btn-secondary" 
            onClick={onResume} 
            disabled={!canResume}
            title="Resume selected download"
          >
            <Play size={14} fill="currentColor" />
            <span>Resume</span>
          </button>

          <button 
            className="btn-secondary" 
            onClick={onPause} 
            disabled={!canPause}
            title="Pause selected download"
          >
            <Pause size={14} fill="currentColor" />
            <span>Pause</span>
          </button>

          <button 
            className="btn-secondary" 
            onClick={onDelete} 
            disabled={!hasSelection}
            title="Delete selected download (Del)"
          >
            <Trash2 size={14} />
            <span>Delete</span>
          </button>

          <div style={{ width: '1px', height: '22px', background: 'var(--border)', margin: '0 4px' }} />

          <button className="btn-icon" onClick={onStartQueue} title="Start queue - run unfinished downloads">
            <PlayCircle size={18} />
          </button>

          <button className="btn-icon" onClick={onStopQueue} title="Stop queue - pause all active downloads">
            <StopCircle size={18} />
          </button>
        </div>
      </div>

      {/* Search Input */}
      <div style={{
        position: 'relative',
        display: 'flex',
        alignItems: 'center',
        width: '260px',
      }}>
        <Search 
          size={14} 
          style={{ position: 'absolute', left: '10px', color: 'var(--text-muted)' }} 
        />
        <input
          type="text"
          placeholder="Search downloads..."
          value={searchText}
          onChange={(e) => onSearchChange(e.target.value)}
          style={{
            width: '100%',
            paddingLeft: '32px',
            paddingRight: searchText ? '28px' : '10px',
            height: '32px',
            borderRadius: 'var(--radius-full)',
            background: 'var(--bg-surface-alt)',
          }}
        />
        {searchText && (
          <button
            onClick={() => onSearchChange('')}
            style={{
              position: 'absolute',
              right: '6px',
              padding: '2px',
              background: 'transparent',
              color: 'var(--text-muted)',
            }}
          >
            <X size={14} />
          </button>
        )}
      </div>

      {/* Tools & Settings */}
      <div style={{ display: 'flex', alignItems: 'center', gap: '4px' }}>
        <button className="btn-icon" onClick={onOpenGrabber} title="Site grabber - crawl links on a page">
          <Globe size={18} />
        </button>

        <button className="btn-icon" onClick={onOpenScheduler} title="Scheduler">
          <Clock size={18} />
        </button>

        <button className="btn-icon" onClick={onToggleTheme} title="Switch theme">
          {isDarkMode ? <Sun size={18} /> : <Moon size={18} />}
        </button>

        <button className="btn-icon" onClick={onOpenSettings} title="Settings & Options">
          <Settings size={18} />
        </button>

        {/* More Dropdown */}
        <div style={{ position: 'relative' }}>
          <button 
            className="btn-icon" 
            onClick={() => setShowMore(!showMore)}
            title="More actions"
          >
            <MoreVertical size={18} />
          </button>

          {showMore && (
            <div 
              style={{
                position: 'absolute',
                right: 0,
                top: '100%',
                marginTop: '6px',
                background: 'var(--bg-surface)',
                border: '1px solid var(--border)',
                borderRadius: 'var(--radius-md)',
                boxShadow: 'var(--shadow-md)',
                minWidth: '210px',
                padding: '4px 0',
                zIndex: 200,
              }}
              onMouseLeave={() => setShowMore(false)}
            >
              <div 
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  gap: '8px',
                  padding: '8px 14px',
                  cursor: 'pointer',
                  fontSize: '13px',
                }}
                onClick={() => {
                  setShowMore(false);
                  onBatchFromClipboard();
                }}
                onMouseEnter={(e) => (e.currentTarget.style.background = 'var(--bg-surface-hover)')}
                onMouseLeave={(e) => (e.currentTarget.style.background = 'transparent')}
              >
                <Layers size={15} />
                <span>Add from clipboard...</span>
              </div>

              <div 
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  gap: '8px',
                  padding: '8px 14px',
                  cursor: 'pointer',
                  fontSize: '13px',
                }}
                onClick={() => {
                  setShowMore(false);
                  onBatchDownload();
                }}
                onMouseEnter={(e) => (e.currentTarget.style.background = 'var(--bg-surface-hover)')}
                onMouseLeave={(e) => (e.currentTarget.style.background = 'transparent')}
              >
                <ListFilter size={15} />
                <span>Batch download...</span>
              </div>
            </div>
          )}
        </div>
      </div>
    </header>
  );
};
