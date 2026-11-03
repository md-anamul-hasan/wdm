import React from 'react';
import { 
  Download, Play, CheckCircle2, Clock, PauseCircle, AlertCircle,
  FolderArchive, FileText, Music, Cpu, Film, Image, Folder
} from 'lucide-react';
import { CategoryFilter, DownloadRecord, StatusFilter } from '../types';
import { getCategoryFromFileName } from '../utils/format';

interface SidebarProps {
  statusFilter: StatusFilter;
  categoryFilter: CategoryFilter;
  downloads: DownloadRecord[];
  onSelectStatus: (status: StatusFilter) => void;
  onSelectCategory: (category: CategoryFilter) => void;
}

export const Sidebar: React.FC<SidebarProps> = ({
  statusFilter,
  categoryFilter,
  downloads,
  onSelectStatus,
  onSelectCategory,
}) => {
  const counts = {
    allStatus: downloads.length,
    inProgress: downloads.filter(d => ['Connecting', 'Downloading'].includes(d.Status)).length,
    completed: downloads.filter(d => d.Status === 'Completed').length,
    queued: downloads.filter(d => d.Status === 'Queued').length,
    paused: downloads.filter(d => d.Status === 'Paused').length,
    error: downloads.filter(d => d.Status === 'Error').length,

    allCategories: downloads.length,
    compressed: downloads.filter(d => getCategoryFromFileName(d.FileName) === 'Compressed').length,
    documents: downloads.filter(d => getCategoryFromFileName(d.FileName) === 'Documents').length,
    music: downloads.filter(d => getCategoryFromFileName(d.FileName) === 'Music').length,
    programs: downloads.filter(d => getCategoryFromFileName(d.FileName) === 'Programs').length,
    video: downloads.filter(d => getCategoryFromFileName(d.FileName) === 'Video').length,
    images: downloads.filter(d => getCategoryFromFileName(d.FileName) === 'Images').length,
    other: downloads.filter(d => getCategoryFromFileName(d.FileName) === 'Other').length,
  };

  const statusItems: { id: StatusFilter; label: string; icon: React.ReactNode; count: number }[] = [
    { id: 'All', label: 'All Downloads', icon: <Download size={15} />, count: counts.allStatus },
    { id: 'InProgress', label: 'In Progress', icon: <Play size={15} />, count: counts.inProgress },
    { id: 'Completed', label: 'Completed', icon: <CheckCircle2 size={15} />, count: counts.completed },
    { id: 'Queued', label: 'Queued', icon: <Clock size={15} />, count: counts.queued },
    { id: 'Paused', label: 'Paused', icon: <PauseCircle size={15} />, count: counts.paused },
    { id: 'Error', label: 'Error', icon: <AlertCircle size={15} />, count: counts.error },
  ];

  const categoryItems: { id: CategoryFilter; label: string; icon: React.ReactNode; count: number }[] = [
    { id: 'All', label: 'All Categories', icon: <Folder size={15} />, count: counts.allCategories },
    { id: 'Compressed', label: 'Compressed', icon: <FolderArchive size={15} />, count: counts.compressed },
    { id: 'Documents', label: 'Documents', icon: <FileText size={15} />, count: counts.documents },
    { id: 'Music', label: 'Music', icon: <Music size={15} />, count: counts.music },
    { id: 'Programs', label: 'Programs', icon: <Cpu size={15} />, count: counts.programs },
    { id: 'Video', label: 'Video', icon: <Film size={15} />, count: counts.video },
    { id: 'Images', label: 'Images', icon: <Image size={15} />, count: counts.images },
    { id: 'Other', label: 'Other', icon: <Folder size={15} />, count: counts.other },
  ];

  return (
    <aside style={{
      width: '210px',
      background: 'var(--bg-surface)',
      borderRight: '1px solid var(--border)',
      display: 'flex',
      flexDirection: 'column',
      padding: '12px 8px',
      gap: '16px',
      overflowY: 'auto',
    }}>
      {/* Status section */}
      <div>
        <div style={{
          fontSize: '11px',
          fontWeight: 700,
          color: 'var(--text-muted)',
          textTransform: 'uppercase',
          letterSpacing: '0.6px',
          padding: '4px 8px',
          marginBottom: '4px',
        }}>
          Tasks
        </div>
        <div style={{ display: 'flex', flexDirection: 'column', gap: '2px' }}>
          {statusItems.map((item) => {
            const active = statusFilter === item.id;
            return (
              <div
                key={item.id}
                onClick={() => onSelectStatus(item.id)}
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'space-between',
                  padding: '6px 10px',
                  borderRadius: 'var(--radius-md)',
                  cursor: 'pointer',
                  background: active ? 'var(--bg-surface-selected)' : 'transparent',
                  color: active ? 'var(--accent)' : 'var(--text-primary)',
                  fontWeight: active ? 600 : 400,
                  transition: 'background 0.12s',
                }}
                onMouseEnter={(e) => {
                  if (!active) e.currentTarget.style.background = 'var(--bg-surface-hover)';
                }}
                onMouseLeave={(e) => {
                  if (!active) e.currentTarget.style.background = 'transparent';
                }}
              >
                <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                  {item.icon}
                  <span>{item.label}</span>
                </div>
                {item.count > 0 && (
                  <span style={{
                    fontSize: '11px',
                    padding: '1px 6px',
                    borderRadius: 'var(--radius-full)',
                    background: active ? 'var(--accent)' : 'var(--bg-surface-alt)',
                    color: active ? 'var(--on-accent)' : 'var(--text-secondary)',
                  }}>
                    {item.count}
                  </span>
                )}
              </div>
            );
          })}
        </div>
      </div>

      {/* Category section */}
      <div>
        <div style={{
          fontSize: '11px',
          fontWeight: 700,
          color: 'var(--text-muted)',
          textTransform: 'uppercase',
          letterSpacing: '0.6px',
          padding: '4px 8px',
          marginBottom: '4px',
        }}>
          Categories
        </div>
        <div style={{ display: 'flex', flexDirection: 'column', gap: '2px' }}>
          {categoryItems.map((item) => {
            const active = categoryFilter === item.id;
            return (
              <div
                key={item.id}
                onClick={() => onSelectCategory(item.id)}
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'space-between',
                  padding: '6px 10px',
                  borderRadius: 'var(--radius-md)',
                  cursor: 'pointer',
                  background: active ? 'var(--bg-surface-selected)' : 'transparent',
                  color: active ? 'var(--accent)' : 'var(--text-primary)',
                  fontWeight: active ? 600 : 400,
                  transition: 'background 0.12s',
                }}
                onMouseEnter={(e) => {
                  if (!active) e.currentTarget.style.background = 'var(--bg-surface-hover)';
                }}
                onMouseLeave={(e) => {
                  if (!active) e.currentTarget.style.background = 'transparent';
                }}
              >
                <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                  {item.icon}
                  <span>{item.label}</span>
                </div>
                {item.count > 0 && (
                  <span style={{
                    fontSize: '11px',
                    padding: '1px 6px',
                    borderRadius: 'var(--radius-full)',
                    background: active ? 'var(--accent)' : 'var(--bg-surface-alt)',
                    color: active ? 'var(--on-accent)' : 'var(--text-secondary)',
                  }}>
                    {item.count}
                  </span>
                )}
              </div>
            );
          })}
        </div>
      </div>
    </aside>
  );
};
