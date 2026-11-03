import React, { useState } from 'react';
import { X, Globe, Loader2, CheckSquare, Square, Folder } from 'lucide-react';
import { GrabMediaItem } from '../types';

interface BatchGrabberModalProps {
  isOpen: boolean;
  defaultDirectory: string;
  onClose: () => void;
  onGrab: (url: string) => Promise<GrabMediaItem[]>;
  onSubmitBatch: (items: GrabMediaItem[], directory?: string) => void;
  onBrowseDirectory: () => Promise<string | null>;
}

export const BatchGrabberModal: React.FC<BatchGrabberModalProps> = ({
  isOpen,
  defaultDirectory,
  onClose,
  onGrab,
  onSubmitBatch,
  onBrowseDirectory,
}) => {
  const [url, setUrl] = useState('');
  const [directory, setDirectory] = useState(defaultDirectory);
  const [loading, setLoading] = useState(false);
  const [items, setItems] = useState<GrabMediaItem[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [categoryFilter, setCategoryFilter] = useState<string>('All');

  if (!isOpen) return null;

  const handleCrawl = async () => {
    if (!url.trim()) return;
    setLoading(true);
    setError(null);
    try {
      const results = await onGrab(url.trim());
      setItems(results);
      if (results.length === 0) {
        setError('No downloadable media, links, or files found on this page.');
      }
    } catch (err: any) {
      setError(String(err));
    } finally {
      setLoading(false);
    }
  };

  const handleBrowse = async () => {
    const selected = await onBrowseDirectory();
    if (selected) {
      setDirectory(selected);
    }
  };

  const toggleSelect = (index: number) => {
    setItems((prev) =>
      prev.map((item, i) => (i === index ? { ...item, IsSelected: !item.IsSelected } : item))
    );
  };

  const selectAll = (selected: boolean) => {
    setItems((prev) => prev.map((item) => ({ ...item, IsSelected: selected })));
  };

  const handleSubmit = () => {
    const selected = items.filter((i) => i.IsSelected);
    if (selected.length === 0) return;
    onSubmitBatch(selected, directory.trim() || undefined);
    onClose();
  };

  const categories = ['All', ...Array.from(new Set(items.map((i) => i.Category)))];
  const filteredItems = items.filter(
    (item) => categoryFilter === 'All' || item.Category === categoryFilter
  );
  const selectedCount = items.filter((i) => i.IsSelected).length;

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal-card" onClick={(e) => e.stopPropagation()} style={{ width: '700px', maxHeight: '88vh' }}>
        {/* Header */}
        <div style={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          padding: '14px 18px',
          borderBottom: '1px solid var(--border)',
        }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: '8px', fontWeight: 600, fontSize: '14px' }}>
            <Globe size={18} color="var(--accent)" />
            <span>Site & Media Grabber</span>
          </div>
          <button className="btn-icon" onClick={onClose}>
            <X size={16} />
          </button>
        </div>

        {/* Input Bar */}
        <div style={{ padding: '16px 18px 10px', display: 'flex', flexDirection: 'column', gap: '10px' }}>
          <div style={{ display: 'flex', gap: '8px' }}>
            <input
              type="text"
              placeholder="Enter web page URL to crawl (e.g. https://archive.org/details/...)"
              value={url}
              onChange={(e) => setUrl(e.target.value)}
              onKeyDown={(e) => e.key === 'Enter' && handleCrawl()}
              style={{ flex: 1 }}
              autoFocus
            />
            <button className="btn-primary" onClick={handleCrawl} disabled={loading || !url.trim()}>
              {loading ? <Loader2 size={15} className="spin" /> : <Globe size={15} />}
              <span>{loading ? 'Crawling...' : 'Crawl Page'}</span>
            </button>
          </div>

          <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
            <span style={{ fontSize: '12px', color: 'var(--text-secondary)' }}>Save To:</span>
            <input
              type="text"
              value={directory}
              onChange={(e) => setDirectory(e.target.value)}
              style={{ flex: 1, height: '28px' }}
            />
            <button className="btn-secondary" onClick={handleBrowse} style={{ padding: '4px 8px' }}>
              <Folder size={14} />
              <span>Browse</span>
            </button>
          </div>

          {error && (
            <div style={{
              padding: '8px 12px',
              borderRadius: 'var(--radius-md)',
              background: 'var(--status-error-bg)',
              color: 'var(--status-error)',
              fontSize: '12px',
            }}>
              {error}
            </div>
          )}

          {items.length > 0 && (
            <div style={{
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'space-between',
              paddingTop: '6px',
            }}>
              <div style={{ display: 'flex', gap: '6px' }}>
                {categories.map((cat) => (
                  <button
                    key={cat}
                    onClick={() => setCategoryFilter(cat)}
                    style={{
                      padding: '3px 8px',
                      fontSize: '11px',
                      borderRadius: 'var(--radius-full)',
                      background: categoryFilter === cat ? 'var(--accent)' : 'var(--bg-surface-alt)',
                      color: categoryFilter === cat ? 'var(--on-accent)' : 'var(--text-secondary)',
                      border: '1px solid var(--border)',
                    }}
                  >
                    {cat}
                  </button>
                ))}
              </div>

              <div style={{ display: 'flex', gap: '8px', fontSize: '11.5px' }}>
                <button className="btn-icon" onClick={() => selectAll(true)} style={{ padding: '2px 6px' }}>
                  <CheckSquare size={13} />
                  <span>All</span>
                </button>
                <button className="btn-icon" onClick={() => selectAll(false)} style={{ padding: '2px 6px' }}>
                  <Square size={13} />
                  <span>None</span>
                </button>
              </div>
            </div>
          )}
        </div>

        {/* Links Table */}
        <div style={{ flex: 1, overflowY: 'auto', borderTop: '1px solid var(--border)', borderBottom: '1px solid var(--border)' }}>
          {items.length === 0 ? (
            <div style={{ padding: '40px', textAlign: 'center', color: 'var(--text-muted)' }}>
              {loading ? 'Analyzing page HTML and resolving media...' : 'Enter a web page URL and click Crawl Page'}
            </div>
          ) : (
            filteredItems.map((item, idx) => (
              <div
                key={idx}
                onClick={() => toggleSelect(idx)}
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  padding: '8px 18px',
                  borderBottom: '1px solid var(--border)',
                  cursor: 'pointer',
                  gap: '12px',
                  background: item.IsSelected ? 'var(--bg-surface-selected)' : 'transparent',
                }}
              >
                <input
                  type="checkbox"
                  checked={item.IsSelected}
                  onChange={() => toggleSelect(idx)}
                  onClick={(e) => e.stopPropagation()}
                />
                <div style={{ flex: 1, overflow: 'hidden' }}>
                  <div style={{ fontWeight: 500, fontSize: '12.5px', textOverflow: 'ellipsis', overflow: 'hidden', whiteSpace: 'nowrap' }}>
                    {item.FileName}
                  </div>
                  <div style={{ fontSize: '11px', color: 'var(--text-muted)', textOverflow: 'ellipsis', overflow: 'hidden', whiteSpace: 'nowrap' }}>
                    {item.Url}
                  </div>
                </div>
                <span style={{
                  padding: '2px 8px',
                  borderRadius: 'var(--radius-full)',
                  fontSize: '11px',
                  background: 'var(--bg-surface-alt)',
                  color: 'var(--text-secondary)',
                  border: '1px solid var(--border)',
                }}>
                  {item.Category}
                </span>
              </div>
            ))
          )}
        </div>

        {/* Footer */}
        <div style={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          padding: '12px 18px',
          background: 'var(--bg-surface-alt)',
        }}>
          <span style={{ fontSize: '12px', color: 'var(--text-secondary)' }}>
            Selected: <strong>{selectedCount}</strong> of {items.length} files
          </span>

          <div style={{ display: 'flex', gap: '8px' }}>
            <button className="btn-secondary" onClick={onClose}>
              Cancel
            </button>
            <button className="btn-primary" onClick={handleSubmit} disabled={selectedCount === 0}>
              Download Selected ({selectedCount})
            </button>
          </div>
        </div>
      </div>
    </div>
  );
};
