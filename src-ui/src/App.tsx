import React, { useState, useEffect, useMemo } from 'react';
import { invoke } from '@tauri-apps/api/core';
import { listen } from '@tauri-apps/api/event';
import { open as openDialog } from '@tauri-apps/plugin-dialog';

import { 
  AppSettings, CategoryFilter, DownloadProgress, DownloadRecord, 
  ExternalDownloadRequest, GrabMediaItem, StatusFilter 
} from './types';
import { CommandBar } from './components/CommandBar';
import { Sidebar } from './components/Sidebar';
import { DownloadTable } from './components/DownloadTable';
import { StreamVisualizer } from './components/StreamVisualizer';
import { AddDownloadModal } from './components/AddDownloadModal';
import { BatchGrabberModal } from './components/BatchGrabberModal';
import { SchedulerModal } from './components/SchedulerModal';
import { SettingsModal } from './components/SettingsModal';
import { ConfirmDeleteModal } from './components/ConfirmDeleteModal';
import { getCategoryFromFileName } from './utils/format';

export const App: React.FC = () => {
  const [downloads, setDownloads] = useState<DownloadRecord[]>([]);
  const [settings, setSettings] = useState<AppSettings | null>(null);
  const [activeProgress, setActiveProgress] = useState<Record<string, DownloadProgress>>({});
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [notice, setNotice] = useState<string | null>(null);
  
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [statusFilter, setStatusFilter] = useState<StatusFilter>('All');
  const [categoryFilter, setCategoryFilter] = useState<CategoryFilter>('All');
  const [searchText, setSearchText] = useState<string>('');

  // Modals state
  const [isAddOpen, setIsAddOpen] = useState(false);
  const [addInitialUrl, setAddInitialUrl] = useState('');
  const [isGrabberOpen, setIsGrabberOpen] = useState(false);
  const [isSchedulerOpen, setIsSchedulerOpen] = useState(false);
  const [isSettingsOpen, setIsSettingsOpen] = useState(false);
  const [deleteTarget, setDeleteTarget] = useState<DownloadRecord | null>(null);

  // Load initial settings and downloads
  useEffect(() => {
    invoke<AppSettings>('get_settings').then((s) => {
      setSettings(s);
      document.documentElement.setAttribute('data-theme', s.IsDarkMode ? 'dark' : 'light');
    });

    invoke<DownloadRecord[]>('get_downloads').then((list) => {
      setDownloads(list);
    });
  }, []);

  // Listen to Tauri events
  useEffect(() => {
    const unlistenProgress = listen<{ id: string; progress: DownloadProgress }>(
      'download-progress',
      (event) => {
        setActiveProgress((prev) => ({
          ...prev,
          [event.payload.id]: event.payload.progress,
        }));
        // Data is flowing, so "Connecting" is over even if its own status event was overtaken
        setDownloads((prev) =>
          prev.map((d) => (d.Id === event.payload.id && d.Status === 'Connecting' ? { ...d, Status: 'Downloading' } : d))
        );
      }
    );

    const unlistenStatus = listen<{ id: string; status: string; message?: string | null }>(
      'download-status',
      (event) => {
        setDownloads((prev) => {
          if (!prev.some((d) => d.Id === event.payload.id)) {
            // A row the window has not loaded yet (added by the browser or a second launch)
            invoke<DownloadRecord[]>('get_downloads').then(setDownloads);
            return prev;
          }
          return prev.map((d) => (d.Id === event.payload.id ? { ...d, Status: event.payload.status } : d));
        });
        setErrors((prev) => {
          const next = { ...prev };
          if (event.payload.status === 'Error' && event.payload.message) next[event.payload.id] = event.payload.message;
          else delete next[event.payload.id];
          return next;
        });
        // The backend fills in name, size and finish time when a download ends
        if (event.payload.status === 'Completed') {
          invoke<DownloadRecord[]>('get_downloads').then(setDownloads);
        }
        if (['Completed', 'Paused', 'Error', 'Queued'].includes(event.payload.status)) {
          setActiveProgress((prev) => {
            const next = { ...prev };
            delete next[event.payload.id];
            return next;
          });
        }
      }
    );

    const unlistenExternal = listen<ExternalDownloadRequest>(
      'external-download',
      (event) => {
        // A single address that needs the user's confirmation
        const req = event.payload;
        if (req.Urls && req.Urls.length > 0) {
          setAddInitialUrl(req.Urls[0]);
          setIsAddOpen(true);
        }
      }
    );

    // Downloads were added by the browser or the command line
    const unlistenChanged = listen('downloads-changed', () => {
      invoke<DownloadRecord[]>('get_downloads').then(setDownloads);
    });

    return () => {
      unlistenProgress.then((f) => f());
      unlistenStatus.then((f) => f());
      unlistenExternal.then((f) => f());
      unlistenChanged.then((f) => f());
    };
  }, []);

  // Keyboard shortcuts
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'n') {
        e.preventDefault();
        setAddInitialUrl('');
        setIsAddOpen(true);
      } else if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'b') {
        e.preventDefault();
        setIsGrabberOpen(true);
      } else if (e.key === 'Delete' && selectedId) {
        e.preventDefault();
        const d = downloads.find((item) => item.Id === selectedId);
        if (d) setDeleteTarget(d);
      }
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [selectedId, downloads]);

  // Actions
  const handleAddSubmit = async (req: ExternalDownloadRequest) => {
    try {
      const record = await invoke<DownloadRecord>('add_download', { request: req });
      // The returned row is a snapshot from before the download started; status events may have
      // arrived since, so show the backend's current list
      setDownloads(await invoke<DownloadRecord[]>('get_downloads'));
      setSelectedId(record.Id);
    } catch (err) {
      setNotice(`Could not add the download: ${err}`);
    }
  };

  const handleResume = async (id: string) => {
    try {
      await invoke('start_download', { id });
      setDownloads((prev) =>
        prev.map((d) => (d.Id === id ? { ...d, Status: 'Connecting' } : d))
      );
    } catch (err) {
      setNotice(`Could not start the download: ${err}`);
    }
  };

  const handlePause = async (id: string) => {
    try {
      await invoke('pause_download', { id });
      setDownloads((prev) =>
        prev.map((d) => (d.Id === id ? { ...d, Status: 'Paused' } : d))
      );
    } catch (err) {
      setNotice(`Could not pause the download: ${err}`);
    }
  };

  const handleDelete = (id: string) => {
    const d = downloads.find((item) => item.Id === id);
    if (!d) return;
    if (settings?.ConfirmBeforeDelete) {
      setDeleteTarget(d);
    } else {
      executeDelete(id, false);
    }
  };

  const executeDelete = async (id: string, deleteFile: boolean) => {
    try {
      await invoke('delete_download', { id, deleteFile });
      setDownloads((prev) => prev.filter((d) => d.Id !== id));
      if (selectedId === id) setSelectedId(null);
    } catch (err) {
      setNotice(`Could not remove the download: ${err}`);
    }
  };

  const handleStartQueue = async () => {
    await invoke('start_all');
  };

  const handleStopQueue = async () => {
    await invoke('pause_all');
  };

  const handleBrowseDirectory = async (): Promise<string | null> => {
    try {
      const selected = await openDialog({
        directory: true,
        multiple: false,
      });
      return typeof selected === 'string' ? selected : null;
    } catch {
      return null;
    }
  };

  const handleToggleTheme = () => {
    if (!settings) return;
    const nextTheme = !settings.IsDarkMode;
    const updated = { ...settings, IsDarkMode: nextTheme };
    setSettings(updated);
    document.documentElement.setAttribute('data-theme', nextTheme ? 'dark' : 'light');
    invoke('save_settings', { settings: updated });
  };

  const handleSaveSettings = (newSettings: AppSettings) => {
    setSettings(newSettings);
    document.documentElement.setAttribute('data-theme', newSettings.IsDarkMode ? 'dark' : 'light');
    invoke('save_settings', { settings: newSettings });
  };

  const handleBatchFromClipboard = async () => {
    try {
      const text = await navigator.clipboard.readText();
      const urls = text
        .split(/[\r\n\s]+/)
        .map((u) => u.trim())
        .filter((u) => u.startsWith('http://') || u.startsWith('https://'));

      if (urls.length === 0) {
        setNotice('No web addresses found on the clipboard.');
        return;
      }

      for (const u of urls) {
        await invoke('add_download', {
          request: {
            Urls: [u],
            QueueOnly: true,
            Silent: true,
          },
        });
      }
      invoke<DownloadRecord[]>('get_downloads').then(setDownloads);
    } catch {
      setNotice('Could not read the clipboard.');
    }
  };

  // Selected item state
  const selectedDownload = useMemo(() => {
    return downloads.find((d) => d.Id === selectedId) || null;
  }, [downloads, selectedId]);

  const canResumeSelected = selectedDownload ? ['Paused', 'Error', 'Queued'].includes(selectedDownload.Status) : false;
  const canPauseSelected = selectedDownload ? ['Downloading', 'Connecting'].includes(selectedDownload.Status) : false;

  // Filtered downloads
  const filteredDownloads = useMemo(() => {
    return downloads.filter((d) => {
      // Status filter
      if (statusFilter === 'InProgress' && !['Connecting', 'Downloading'].includes(d.Status)) return false;
      if (statusFilter === 'Completed' && d.Status !== 'Completed') return false;
      if (statusFilter === 'Queued' && d.Status !== 'Queued') return false;
      if (statusFilter === 'Paused' && d.Status !== 'Paused') return false;
      if (statusFilter === 'Error' && d.Status !== 'Error') return false;

      // Category filter
      if (categoryFilter !== 'All') {
        const cat = getCategoryFromFileName(d.FileName);
        if (cat !== categoryFilter) return false;
      }

      // Search text
      if (searchText.trim()) {
        const q = searchText.toLowerCase();
        if (!d.FileName.toLowerCase().includes(q) && !d.Url.toLowerCase().includes(q)) return false;
      }

      return true;
    });
  }, [downloads, statusFilter, categoryFilter, searchText]);

  return (
    <div style={{ display: 'flex', flexDirection: 'column', height: '100vh', width: '100vw', overflow: 'hidden' }}>
      {/* Top Command Bar */}
      <CommandBar
        canResume={canResumeSelected}
        canPause={canPauseSelected}
        hasSelection={!!selectedId}
        isDarkMode={settings?.IsDarkMode || false}
        searchText={searchText}
        onSearchChange={setSearchText}
        onAddUrl={() => {
          setAddInitialUrl('');
          setIsAddOpen(true);
        }}
        onResume={() => selectedId && handleResume(selectedId)}
        onPause={() => selectedId && handlePause(selectedId)}
        onDelete={() => selectedId && handleDelete(selectedId)}
        onStartQueue={handleStartQueue}
        onStopQueue={handleStopQueue}
        onOpenGrabber={() => setIsGrabberOpen(true)}
        onOpenScheduler={() => setIsSchedulerOpen(true)}
        onToggleTheme={handleToggleTheme}
        onOpenSettings={() => setIsSettingsOpen(true)}
        onBatchFromClipboard={handleBatchFromClipboard}
        onBatchDownload={() => setIsGrabberOpen(true)}
      />

      {/* Main Content (Sidebar + Table) */}
      <div style={{ display: 'flex', flex: 1, overflow: 'hidden' }}>
        <Sidebar
          statusFilter={statusFilter}
          categoryFilter={categoryFilter}
          downloads={downloads}
          onSelectStatus={setStatusFilter}
          onSelectCategory={setCategoryFilter}
        />

        <div style={{ display: 'flex', flexDirection: 'column', flex: 1, overflow: 'hidden' }}>
          <DownloadTable
            downloads={filteredDownloads}
            selectedId={selectedId}
            activeProgress={activeProgress}
            errors={errors}
            onSelect={setSelectedId}
            onResume={handleResume}
            onPause={handlePause}
            onDelete={handleDelete}
            onOpenFile={(path) => invoke('open_file', { path }).catch(() => setNotice('That file is not available yet.'))}
            onOpenFolder={(path) => invoke('open_folder', { path }).catch((err) => setNotice(`${err}`))}
          />

          {/* Bottom Stream Visualizer for Active Selected Download */}
          {selectedDownload && (
            <StreamVisualizer
              _download={selectedDownload}
              progress={activeProgress[selectedDownload.Id]}
            />
          )}
        </div>
      </div>

      {notice && (
        <div role="status" style={{ position: 'fixed', bottom: 16, left: '50%', transform: 'translateX(-50%)', zIndex: 2000, display: 'flex', gap: 12, alignItems: 'center', padding: '10px 14px', background: 'var(--bg-surface)', color: 'var(--text-primary)', border: '1px solid var(--border)', borderRadius: 'var(--radius-md)', boxShadow: '0 6px 24px rgba(0,0,0,.25)', fontSize: 12.5 }}>
          <span>{notice}</span>
          <button className="btn-secondary" onClick={() => setNotice(null)}>Dismiss</button>
        </div>
      )}

      {/* Modals */}
      {settings && (
        <>
          <AddDownloadModal
            initialUrl={addInitialUrl}
            defaultDirectory={settings.DefaultDownloadDirectory}
            defaultConnections={settings.DefaultConnections}
            isOpen={isAddOpen}
            onClose={() => setIsAddOpen(false)}
            onSubmit={handleAddSubmit}
            onBrowseDirectory={handleBrowseDirectory}
          />

          <BatchGrabberModal
            isOpen={isGrabberOpen}
            defaultDirectory={settings.DefaultDownloadDirectory}
            onClose={() => setIsGrabberOpen(false)}
            onGrab={(u) => invoke<GrabMediaItem[]>('grab_links', { url: u })}
            onSubmitBatch={(items, dir) => {
              invoke('add_batch_downloads', { items, destination: dir }).then(() => {
                invoke<DownloadRecord[]>('get_downloads').then(setDownloads);
              });
            }}
            onBrowseDirectory={handleBrowseDirectory}
          />

          <SchedulerModal
            isOpen={isSchedulerOpen}
            settings={settings}
            onClose={() => setIsSchedulerOpen(false)}
            onSave={handleSaveSettings}
          />

          <SettingsModal
            isOpen={isSettingsOpen}
            settings={settings}
            onClose={() => setIsSettingsOpen(false)}
            onSave={handleSaveSettings}
            onBrowseDirectory={handleBrowseDirectory}
          />

          <ConfirmDeleteModal
            isOpen={!!deleteTarget}
            download={deleteTarget}
            onClose={() => setDeleteTarget(null)}
            onConfirm={executeDelete}
          />
        </>
      )}
    </div>
  );
};
