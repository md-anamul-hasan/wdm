use std::collections::HashMap;
use std::path::{Path, PathBuf};
use std::sync::atomic::AtomicBool;
use std::sync::{Arc, Mutex};
use chrono::Local;
use tauri::{AppHandle, Emitter, Manager};
use tokio_util::sync::CancellationToken;

use wdm_core::engine::{DownloadEngine, DownloadRecord, DownloadStore, SpeedLimiter};
use wdm_core::ipc::ExternalDownloadRequest;
use wdm_core::models::{AppSettings, DownloadStatus, ScheduleTrigger};

pub struct AppState {
    pub settings: Mutex<AppSettings>,
    pub store: DownloadStore,
    pub records: Mutex<Vec<DownloadRecord>>,
    pub speed_limiter: Arc<SpeedLimiter>,
    pub active_engines: Mutex<HashMap<String, Arc<DownloadEngine>>>,
    pub cancellations: Mutex<HashMap<String, CancellationToken>>,
    pub is_first_hide: AtomicBool,
}

impl AppState {
    pub fn new() -> Self {
        let settings = AppSettings::load();
        let store = DownloadStore::new(None);
        let records = store.load();
        let limit_bps = if settings.is_speed_limiter_enabled {
            settings.default_speed_limit_kb as i64 * 1024
        } else {
            0
        };
        let speed_limiter = Arc::new(SpeedLimiter::new(limit_bps));

        Self {
            settings: Mutex::new(settings),
            store,
            records: Mutex::new(records),
            speed_limiter,
            active_engines: Mutex::new(HashMap::new()),
            cancellations: Mutex::new(HashMap::new()),
            is_first_hide: AtomicBool::new(true),
        }
    }

    pub fn get_downloads(&self) -> Vec<DownloadRecord> {
        self.records.lock().unwrap().clone()
    }

    pub fn get_settings(&self) -> AppSettings {
        self.settings.lock().unwrap().clone()
    }

    pub fn save_settings(&self, new_settings: AppSettings) {
        let mut s = new_settings;
        s.normalize();
        let _ = s.save();

        if s.is_speed_limiter_enabled {
            self.speed_limiter.set_max_bytes_per_second(s.default_speed_limit_kb as i64 * 1024);
        } else {
            self.speed_limiter.set_max_bytes_per_second(0);
        }

        *self.settings.lock().unwrap() = s;
    }

    pub fn add_download(
        &self,
        request: ExternalDownloadRequest,
        app_handle: &AppHandle,
    ) -> Result<DownloadRecord, String> {
        if request.urls.is_empty() {
            return Err("No URL provided".into());
        }

        let url = request.urls[0].clone();
        let options = request.to_options();
        let settings = self.get_settings();
        let target_dir = request
            .destination_directory
            .clone()
            .unwrap_or(settings.default_download_directory);

        let file_name = request.file_name.unwrap_or_else(|| {
            Path::new(&url)
                .file_name()
                .and_then(|n| n.to_str())
                .unwrap_or("download.dat")
                .to_string()
        });

        let target_path = PathBuf::from(&target_dir).join(&file_name).to_string_lossy().into_owned();

        let record = DownloadRecord {
            id: uuid::Uuid::new_v4().simple().to_string(),
            url,
            file_name,
            target_file_path: target_path,
            status: if request.queue_only { "Queued".into() } else { "Connecting".into() },
            options: Some(options),
            ..Default::default()
        };

        {
            let mut recs = self.records.lock().unwrap();
            recs.retain(|r| r.url != record.url);
            recs.insert(0, record.clone());
            self.store.save(&recs);
        }

        let should_start = !request.queue_only && (request.silent || !settings.show_start_download_dialog);
        if should_start {
            let id = record.id.clone();
            let _ = self.start_download(&id, app_handle);
        }

        Ok(record)
    }

    pub fn start_download(&self, id: &str, app_handle: &AppHandle) -> Result<(), String> {
        let record = {
            let recs = self.records.lock().unwrap();
            recs.iter().find(|r| r.id == id).cloned()
        };

        let Some(record) = record else {
            return Err(format!("Download '{id}' not found"));
        };

        if self.active_engines.lock().unwrap().contains_key(id) {
            return Ok(());
        }

        let settings = self.get_settings();
        let connections = settings.default_connections;
        let limit_bps = if settings.is_speed_limiter_enabled {
            settings.default_speed_limit_kb as i64 * 1024
        } else {
            0
        };

        let engine = DownloadEngine::new(None, limit_bps, Some(self.speed_limiter.clone()))
            .map_err(|e| e.to_string())?;

        let ct = CancellationToken::new();
        self.cancellations.lock().unwrap().insert(id.to_string(), ct.clone());
        self.active_engines.lock().unwrap().insert(id.to_string(), engine.clone());

        {
            let mut recs = self.records.lock().unwrap();
            if let Some(r) = recs.iter_mut().find(|r| r.id == id) {
                r.status = "Connecting".into();
            }
            self.store.save(&recs);
        }

        let handle_for_progress = app_handle.clone();
        let id_for_progress = id.to_string();
        engine.set_on_progress(move |mut prog| {
            prog.status = DownloadStatus::Downloading;
            let _ = handle_for_progress.emit(
                "download-progress",
                serde_json::json!({
                    "id": id_for_progress,
                    "progress": prog
                }),
            );
        });

        let handle_for_status = app_handle.clone();
        let id_for_status = id.to_string();
        engine.set_on_status(move |status| {
            let _ = handle_for_status.emit(
                "download-status",
                serde_json::json!({
                    "id": id_for_status,
                    "status": status
                }),
            );
        });

        let app_handle_clone = app_handle.clone();
        let id_clone = id.to_string();
        let url = record.url.clone();
        let dest_dir = Path::new(&record.target_file_path).parent().map(Path::to_path_buf);
        let options = record.options.clone();

        tokio::spawn(async move {
            let res = engine
                .start(&url, dest_dir.as_deref(), connections, ct, options)
                .await;

            let final_status_str = match res {
                Ok(()) => "Completed",
                Err(wdm_core::Error::Canceled) => "Paused",
                Err(_) => "Error",
            };

            if let Some(state) = app_handle_clone.try_state::<AppState>() {
                state.active_engines.lock().unwrap().remove(&id_clone);
                state.cancellations.lock().unwrap().remove(&id_clone);

                {
                    let mut recs = state.records.lock().unwrap();
                    if let Some(r) = recs.iter_mut().find(|r| r.id == id_clone) {
                        r.status = final_status_str.into();
                        if let Some(meta) = engine.metadata() {
                            r.file_name = meta.file_name;
                            r.target_file_path = meta.target_file_path.clone();
                            r.total_bytes = meta.total_bytes;
                            if final_status_str == "Completed" {
                                r.downloaded_bytes = meta.total_bytes;
                                r.completed_at = Some(meta.created_at);
                            }
                        }
                    }
                    state.store.save(&recs);
                }

                if final_status_str == "Completed" && state.get_settings().auto_open_file_on_complete {
                    if let Some(meta) = engine.metadata() {
                        let _ = open::that(&meta.target_file_path);
                    }
                }

                state.process_queue(&app_handle_clone);
            }

            let _ = app_handle_clone.emit(
                "download-status",
                serde_json::json!({
                    "id": id_clone,
                    "status": final_status_str
                }),
            );
        });

        Ok(())
    }

    pub fn pause_download(&self, id: &str) -> Result<(), String> {
        if let Some(engine) = self.active_engines.lock().unwrap().remove(id) {
            engine.pause();
        }
        if let Some(ct) = self.cancellations.lock().unwrap().remove(id) {
            ct.cancel();
        }

        let mut recs = self.records.lock().unwrap();
        if let Some(r) = recs.iter_mut().find(|r| r.id == id) {
            r.status = "Paused".into();
        }
        self.store.save(&recs);
        Ok(())
    }

    pub fn delete_download(&self, id: &str, delete_file: bool) -> Result<(), String> {
        let _ = self.pause_download(id);

        let mut recs = self.records.lock().unwrap();
        if let Some(record) = recs.iter().find(|r| r.id == id).cloned() {
            if delete_file {
                let path = Path::new(&record.target_file_path);
                if path.is_file() {
                    let _ = std::fs::remove_file(path);
                }
                let part_file = format!("{}.wdm-part", record.target_file_path);
                if Path::new(&part_file).is_file() {
                    let _ = std::fs::remove_file(&part_file);
                }
            }
        }
        recs.retain(|r| r.id != id);
        self.store.save(&recs);
        Ok(())
    }

    pub fn pause_all(&self) {
        let active_ids: Vec<String> = self.active_engines.lock().unwrap().keys().cloned().collect();
        for id in active_ids {
            let _ = self.pause_download(&id);
        }
    }

    pub fn start_all(&self, app_handle: &AppHandle) {
        let queued_ids: Vec<String> = {
            let recs = self.records.lock().unwrap();
            recs.iter()
                .filter(|r| r.status != "Completed")
                .map(|r| r.id.clone())
                .collect()
        };

        for id in queued_ids {
            let _ = self.start_download(&id, app_handle);
        }
    }

    pub fn process_queue(&self, app_handle: &AppHandle) {
        let settings = self.get_settings();
        let max_concurrent = settings.max_concurrent_downloads as usize;

        let active_count = self.active_engines.lock().unwrap().len();
        if active_count >= max_concurrent {
            return;
        }

        let needed = max_concurrent - active_count;
        let queued_ids: Vec<String> = {
            let recs = self.records.lock().unwrap();
            recs.iter()
                .filter(|r| r.status == "Queued")
                .take(needed)
                .map(|r| r.id.clone())
                .collect()
        };

        for id in queued_ids {
            let _ = self.start_download(&id, app_handle);
        }
    }

    pub fn check_scheduler(&self, previous_time: chrono::DateTime<Local>, now: chrono::DateTime<Local>, app_handle: &AppHandle) {
        let settings = self.get_settings();
        if !settings.scheduler_enabled {
            return;
        }

        if ScheduleTrigger::crossed(previous_time, now, &settings.scheduler_start_time, None) {
            println!("[WDM Scheduler] Start time reached. Starting queue...");
            self.start_all(app_handle);
        }

        if ScheduleTrigger::crossed(previous_time, now, &settings.scheduler_stop_time, None) {
            if settings.scheduler_stop_queue_at_end_time {
                println!("[WDM Scheduler] Stop time reached. Pausing all downloads...");
                self.pause_all();
            }

            if settings.scheduler_shutdown_on_complete {
                println!("[WDM Scheduler] Shutting down computer...");
                #[cfg(windows)]
                let _ = std::process::Command::new("shutdown").args(["/s", "/t", "60"]).spawn();
            }
        }
    }
}
