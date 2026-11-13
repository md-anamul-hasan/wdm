use std::collections::HashMap;
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex, MutexGuard};
use std::time::{Duration, Instant};

use chrono::Local;
use serde_json::{json, Value};
use tauri::{AppHandle, Emitter, Manager};
use tauri_plugin_dialog::{DialogExt, MessageDialogButtons, MessageDialogKind};
use tokio_util::sync::CancellationToken;

use wdm_core::engine::prober::{file_name_from_url, sanitize_file_name};
use wdm_core::engine::{DownloadEngine, DownloadRecord, DownloadStore, SpeedLimiter};
use wdm_core::ipc::ExternalDownloadRequest;
use wdm_core::models::{AppSettings, DownloadProgress, ScheduleTrigger};

pub struct AppState {
    pub settings: Mutex<AppSettings>,
    pub store: DownloadStore,
    pub records: Mutex<Vec<DownloadRecord>>,
    pub speed_limiter: Arc<SpeedLimiter>,
    pub active_engines: Mutex<HashMap<String, Arc<DownloadEngine>>>,
    pub cancellations: Mutex<HashMap<String, CancellationToken>>,
    pub is_first_hide: AtomicBool,
    pub shutdown_pending: AtomicBool,
    /// Failed downloads waiting for an automatic retry: id -> (attempts so far, time of the last failure).
    pub retries: Mutex<HashMap<String, (u32, Instant)>>,
    pub pipe_stop_tx: Mutex<Option<tokio::sync::watch::Sender<bool>>>,
}

/// A poisoned lock only means another thread panicked mid-update; the data is still the best we have.
fn lock<T>(m: &Mutex<T>) -> MutexGuard<'_, T> {
    m.lock().unwrap_or_else(|e| e.into_inner())
}

// ---- pure helpers (unit-tested below) ---------------------------------------------------------

/// File name and full path a request downloads to. A name supplied by the browser or the command
/// line is sanitised, so it cannot move the download out of the chosen folder.
pub fn resolve_target(
    dir: Option<&str>,
    default_dir: &str,
    url: &str,
    file_name: Option<&str>,
) -> (String, String) {
    let name = file_name
        .map(str::trim)
        .filter(|n| !n.is_empty())
        .map(sanitize_file_name)
        .or_else(|| file_name_from_url(url))
        .unwrap_or_else(|| "download.dat".into());
    let dir = dir
        .map(str::trim)
        .filter(|d| !d.is_empty())
        .unwrap_or(default_dir);
    let path = PathBuf::from(dir)
        .join(&name)
        .to_string_lossy()
        .into_owned();
    (name, path)
}

/// A download that was running when WDM stopped has no engine behind it any more: show it as paused.
pub fn reset_interrupted(records: &mut [DownloadRecord]) {
    for r in records
        .iter_mut()
        .filter(|r| matches!(r.status.as_str(), "Connecting" | "Downloading"))
    {
        r.status = "Paused".into();
    }
}

pub const MAX_AUTO_RETRIES: u32 = 5;
pub const RETRY_AFTER: Duration = Duration::from_secs(30);

/// Network and disk hiccups are worth another try; a wrong checksum, a refused request or a bad argument is not.
pub fn is_retryable(e: &wdm_core::Error) -> bool {
    matches!(e, wdm_core::Error::Http(_) | wdm_core::Error::Io(_))
}

/// An unfinished download of the same address, which a new request reuses instead of adding a row.
pub fn find_unfinished(records: &[DownloadRecord], url: &str) -> Option<usize> {
    records
        .iter()
        .position(|r| r.status != "Completed" && r.url.eq_ignore_ascii_case(url))
}

/// True when nothing is running or waiting: every row is finished, paused or failed.
pub fn queue_drained(records: &[DownloadRecord], active: usize) -> bool {
    active == 0
        && !records
            .iter()
            .any(|r| matches!(r.status.as_str(), "Queued" | "Connecting" | "Downloading"))
}

/// Progress as the UI reads it: the status as a name (the engine's own serde form is the legacy number)
/// and per-segment state, speed and byte counts that the on-disk checkpoint format leaves out.
pub fn progress_json(prog: &DownloadProgress) -> Value {
    let segments: Vec<Value> = prog
        .segments
        .iter()
        .map(|s| {
            json!({
                "SegmentId": s.segment_id,
                "StartOffset": s.start_offset,
                "CurrentOffset": s.current_offset,
                "EndOffset": s.end_offset,
                "State": segment_state_name(s),
                "SpeedBytesPerSecond": s.speed_bytes_per_second,
                "DownloadedBytes": s.downloaded_bytes(),
                "TotalBytes": s.total_bytes(),
            })
        })
        .collect();

    json!({
        "TotalBytes": prog.total_bytes,
        "DownloadedBytes": prog.downloaded_bytes,
        "SpeedBytesPerSecond": prog.speed_bytes_per_second,
        "EstimatedSecondsRemaining": prog.estimated_seconds_remaining,
        "Segments": segments,
        "Status": prog.status.as_str(),
    })
}

fn segment_state_name(s: &wdm_core::models::DownloadSegment) -> &'static str {
    use wdm_core::models::SegmentState::*;
    if s.is_completed() {
        return "Completed";
    }
    match s.state {
        Idle => "Idle",
        Connecting => "Connecting",
        Downloading => "Downloading",
        Completed => "Completed",
        Error => "Error",
    }
}

// ---- state ------------------------------------------------------------------------------------

impl AppState {
    pub fn new() -> Self {
        let settings = AppSettings::load();
        let store = DownloadStore::new(None);
        let mut records = store.load();
        reset_interrupted(&mut records);
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
            shutdown_pending: AtomicBool::new(false),
            retries: Mutex::new(HashMap::new()),
            pipe_stop_tx: Mutex::new(None),
        }
    }

    pub fn get_downloads(&self) -> Vec<DownloadRecord> {
        lock(&self.records).clone()
    }

    pub fn get_settings(&self) -> AppSettings {
        lock(&self.settings).clone()
    }

    /// True when `path` is the target of a listed download: the only files the UI may open.
    pub fn is_listed_path(&self, path: &str) -> bool {
        let want = Path::new(path);
        lock(&self.records)
            .iter()
            .any(|r| Path::new(&r.target_file_path) == want)
    }

    pub fn save_settings(&self, new_settings: AppSettings) {
        let mut s = new_settings;
        s.normalize();
        let _ = s.save();

        if s.is_speed_limiter_enabled {
            self.speed_limiter
                .set_max_bytes_per_second(s.default_speed_limit_kb as i64 * 1024);
        } else {
            self.speed_limiter.set_max_bytes_per_second(0);
        }

        *lock(&self.settings) = s;
    }

    pub fn add_download(
        &self,
        request: ExternalDownloadRequest,
        app_handle: &AppHandle,
    ) -> Result<DownloadRecord, String> {
        let Some(url) = request.urls.first().cloned() else {
            return Err("No URL provided".into());
        };
        let settings = self.get_settings();

        // Same address already listed and unfinished: reuse that row (resumes it unless queue-only)
        let existing = {
            let recs = lock(&self.records);
            find_unfinished(&recs, &url).map(|i| recs[i].clone())
        };
        if let Some(record) = existing {
            if !request.queue_only {
                let _ = self.start_download(&record.id, app_handle);
            }
            return Ok(record);
        }

        let single = request.urls.len() == 1;
        let (file_name, target_path) = resolve_target(
            request.destination_directory.as_deref(),
            &settings.default_download_directory,
            &url,
            if single {
                request.file_name.as_deref()
            } else {
                None
            },
        );

        let busy =
            lock(&self.active_engines).len() >= settings.max_concurrent_downloads.max(1) as usize;
        let queued = request.queue_only || busy;

        let record = DownloadRecord {
            id: uuid::Uuid::new_v4().simple().to_string(),
            url,
            file_name,
            target_file_path: target_path,
            status: if queued {
                "Queued".into()
            } else {
                "Connecting".into()
            },
            options: Some(request.to_options()),
            ..Default::default()
        };

        {
            let mut recs = lock(&self.records);
            recs.insert(0, record.clone());
            self.store.save(&recs);
        }

        if !queued {
            let _ = self.start_download(&record.id, app_handle);
        }

        Ok(record)
    }

    pub fn start_download(&self, id: &str, app_handle: &AppHandle) -> Result<(), String> {
        let record = lock(&self.records).iter().find(|r| r.id == id).cloned();
        let Some(record) = record else {
            return Err(format!("Download '{id}' not found"));
        };
        if record.status == "Completed" {
            return Ok(());
        }

        let ct = CancellationToken::new();
        let engine = {
            // One lock for the check and the insert: two quick starts must not both run
            let mut engines = lock(&self.active_engines);
            if engines.contains_key(id) {
                return Ok(());
            }
            let settings = self.get_settings();
            let limit_bps = if settings.is_speed_limiter_enabled {
                settings.default_speed_limit_kb as i64 * 1024
            } else {
                0
            };
            let engine = DownloadEngine::new(None, limit_bps, Some(self.speed_limiter.clone()))
                .map_err(|e| e.to_string())?;
            engine.set_enable_dynamic_split(settings.enable_dynamic_middle_split);
            engine.set_network_timeout_seconds(settings.network_timeout_seconds.max(1) as u32);
            engines.insert(id.to_string(), engine.clone());
            lock(&self.cancellations).insert(id.to_string(), ct.clone());
            engine
        };

        {
            let mut recs = lock(&self.records);
            if let Some(r) = recs.iter_mut().find(|r| r.id == id) {
                r.status = "Connecting".into();
            }
            self.store.save(&recs);
        }
        emit_status(app_handle, id, "Connecting", None);

        // Callbacks of a download that was paused or restarted meanwhile must not touch the row
        let (h, cb_id, cb_ct) = (app_handle.clone(), id.to_string(), ct.clone());
        engine.set_on_progress(move |prog| {
            if !cb_ct.is_cancelled() {
                let _ = h.emit(
                    "download-progress",
                    json!({ "id": cb_id, "progress": progress_json(&prog) }),
                );
            }
        });
        let (h, cb_id, cb_ct) = (app_handle.clone(), id.to_string(), ct.clone());
        engine.set_on_status(move |status| {
            if !cb_ct.is_cancelled() {
                emit_status(&h, &cb_id, status.as_str(), None);
            }
        });

        let settings = self.get_settings();
        let mut options = record.options.clone().unwrap_or_default();
        if options
            .user_agent
            .as_deref()
            .map_or(true, |u| u.trim().is_empty())
        {
            options.user_agent = Some(settings.user_agent.clone());
        }
        let connections = options
            .connections
            .unwrap_or(settings.default_connections)
            .clamp(1, 32);
        let url = record.url.clone();
        let dest_dir = Path::new(&record.target_file_path)
            .parent()
            .map(Path::to_path_buf);
        let (app, id_task, task_ct) = (app_handle.clone(), id.to_string(), ct.clone());

        tauri::async_runtime::spawn(async move {
            let res = engine
                .start(&url, dest_dir.as_deref(), connections, ct, Some(options))
                .await;
            // Pause and delete cancel the token first: they already own the row and the maps
            let superseded = task_ct.is_cancelled();

            let retryable = res.as_ref().err().is_some_and(is_retryable);
            let (status, message) = match res {
                Ok(()) => ("Completed", None),
                Err(wdm_core::Error::Canceled) => ("Paused", None),
                Err(e) => ("Error", Some(e.to_string())),
            };

            let Some(state) = app.try_state::<AppState>() else {
                return;
            };
            if !superseded {
                let mut retries = lock(&state.retries);
                if retryable {
                    let attempts = retries.get(&id_task).map_or(0, |r| r.0) + 1;
                    retries.insert(id_task.clone(), (attempts, Instant::now()));
                } else {
                    retries.remove(&id_task);
                }
            }
            if !superseded {
                lock(&state.active_engines).remove(&id_task);
                lock(&state.cancellations).remove(&id_task);
            }

            {
                let mut recs = lock(&state.records);
                if let Some(r) = recs.iter_mut().find(|r| r.id == id_task) {
                    if let Some(meta) = engine.metadata() {
                        r.file_name = meta.file_name;
                        r.target_file_path = meta.target_file_path;
                        r.total_bytes = meta.total_bytes;
                        if status == "Completed" && !superseded {
                            r.downloaded_bytes = meta.total_bytes;
                        }
                    }
                    if !superseded {
                        r.status = status.into();
                        if status == "Completed" {
                            r.completed_at = Some(Local::now().fixed_offset());
                        }
                    }
                }
                state.store.save(&recs);
            }

            if superseded {
                return;
            }
            emit_status(&app, &id_task, status, message.as_deref());

            if status == "Completed" {
                if state.get_settings().auto_open_file_on_complete {
                    if let Some(meta) = engine.metadata() {
                        let _ = open::that(&meta.target_file_path);
                    }
                }
                state.process_queue(&app);
                state.shutdown_if_finished(&app);
            } else {
                state.process_queue(&app);
            }
        });

        Ok(())
    }

    /// A start the user asked for: the automatic retry budget begins again.
    pub fn start_download_by_user(&self, id: &str, app_handle: &AppHandle) -> Result<(), String> {
        lock(&self.retries).remove(id);
        self.start_download(id, app_handle)
    }

    /// Restarts failed downloads that are due, at most `MAX_AUTO_RETRIES` times each.
    pub fn retry_failed(&self, app_handle: &AppHandle) {
        let due: Vec<String> = {
            let recs = lock(&self.records);
            lock(&self.retries)
                .iter()
                .filter(|(id, (n, at))| {
                    *n <= MAX_AUTO_RETRIES
                        && at.elapsed() >= RETRY_AFTER
                        && recs.iter().any(|r| &r.id == *id && r.status == "Error")
                })
                .map(|(id, _)| id.clone())
                .collect()
        };
        for id in due {
            let _ = self.start_download(&id, app_handle);
        }
    }

    pub fn pause_download(&self, id: &str, app_handle: &AppHandle) -> Result<(), String> {
        // Cancel the token first so the engine's own last callbacks are ignored
        if let Some(ct) = lock(&self.cancellations).remove(id) {
            ct.cancel();
        }
        if let Some(engine) = lock(&self.active_engines).remove(id) {
            engine.pause();
        }

        lock(&self.retries).remove(id);
        let mut recs = lock(&self.records);
        if let Some(r) = recs
            .iter_mut()
            .find(|r| r.id == id)
            .filter(|r| r.status != "Completed")
        {
            r.status = "Paused".into();
            emit_status(app_handle, id, "Paused", None);
        }
        self.store.save(&recs);
        Ok(())
    }

    pub fn delete_download(
        &self,
        id: &str,
        delete_file: bool,
        app_handle: &AppHandle,
    ) -> Result<(), String> {
        let _ = self.pause_download(id, app_handle);

        let mut recs = lock(&self.records);
        if let Some(record) = recs.iter().find(|r| r.id == id).cloned() {
            if delete_file {
                let path = &record.target_file_path;
                for p in [path.clone(), wdm_core::engine::resume::part_path(path)] {
                    if Path::new(&p).is_file() {
                        let _ = std::fs::remove_file(&p);
                    }
                }
                wdm_core::engine::resume::delete_checkpoint(path);
            }
        }
        recs.retain(|r| r.id != id);
        self.store.save(&recs);
        Ok(())
    }

    pub fn pause_all(&self, app_handle: &AppHandle) {
        let active_ids: Vec<String> = lock(&self.active_engines).keys().cloned().collect();
        for id in active_ids {
            let _ = self.pause_download(&id, app_handle);
        }
    }

    /// Queues every unfinished download and starts as many as the concurrency limit allows.
    pub fn start_all(&self, app_handle: &AppHandle) {
        {
            let active: Vec<String> = lock(&self.active_engines).keys().cloned().collect();
            let mut recs = lock(&self.records);
            for r in recs
                .iter_mut()
                .filter(|r| r.status != "Completed" && !active.contains(&r.id))
            {
                r.status = "Queued".into();
                emit_status(app_handle, &r.id, "Queued", None);
            }
            self.store.save(&recs);
        }
        self.process_queue(app_handle);
    }

    pub fn process_queue(&self, app_handle: &AppHandle) {
        let max_concurrent = self.get_settings().max_concurrent_downloads.max(1) as usize;
        let active_count = lock(&self.active_engines).len();
        if active_count >= max_concurrent {
            return;
        }

        let queued_ids: Vec<String> = lock(&self.records)
            .iter()
            .rev() // oldest first
            .filter(|r| r.status == "Queued")
            .take(max_concurrent - active_count)
            .map(|r| r.id.clone())
            .collect();

        for id in queued_ids {
            let _ = self.start_download(&id, app_handle);
        }
    }

    /// With "shut down when finished" set, powers off once nothing is left to run. Windows shows its own
    /// 60 second countdown; the dialog lets the user abort it.
    fn shutdown_if_finished(&self, app_handle: &AppHandle) {
        if !self.get_settings().scheduler_shutdown_on_complete {
            return;
        }
        if !queue_drained(&lock(&self.records), lock(&self.active_engines).len()) {
            return;
        }
        if self.shutdown_pending.swap(true, Ordering::SeqCst) {
            return;
        }

        #[cfg(windows)]
        {
            let started = std::process::Command::new("shutdown")
                .args(["/s", "/t", "60", "/c", "WDM: all downloads completed"])
                .spawn();
            if started.is_err() {
                self.shutdown_pending.store(false, Ordering::SeqCst);
                return;
            }
            let app = app_handle.clone();
            tauri::async_runtime::spawn_blocking(move || {
                let keep_going = app
                    .dialog()
                    .message("All downloads are complete. Windows will shut down in 60 seconds.\n\nClick Cancel to abort the shutdown.")
                    .title("WDM - Shutdown")
                    .kind(MessageDialogKind::Warning)
                    .buttons(MessageDialogButtons::OkCancelCustom("OK".into(), "Cancel".into()))
                    .blocking_show();
                if !keep_going {
                    let _ = std::process::Command::new("shutdown").arg("/a").spawn();
                }
                if let Some(state) = app.try_state::<AppState>() {
                    state.shutdown_pending.store(false, Ordering::SeqCst);
                }
            });
        }
        #[cfg(not(windows))]
        {
            let _ = app_handle;
            self.shutdown_pending.store(false, Ordering::SeqCst);
        }
    }

    pub fn check_scheduler(
        &self,
        previous_time: chrono::DateTime<Local>,
        now: chrono::DateTime<Local>,
        app_handle: &AppHandle,
    ) {
        let settings = self.get_settings();
        if !settings.scheduler_enabled {
            return;
        }

        if ScheduleTrigger::crossed(previous_time, now, &settings.scheduler_start_time, None) {
            self.start_all(app_handle);
        }

        if settings.scheduler_stop_queue_at_end_time
            && ScheduleTrigger::crossed(previous_time, now, &settings.scheduler_stop_time, None)
        {
            self.pause_all(app_handle);
        }
    }
}

fn emit_status(app: &AppHandle, id: &str, status: &str, message: Option<&str>) {
    let _ = app.emit(
        "download-status",
        json!({ "id": id, "status": status, "message": message }),
    );
}

#[cfg(test)]
mod tests {
    use super::*;
    use wdm_core::models::{DownloadSegment, DownloadStatus, SegmentState};

    fn rec(url: &str, status: &str) -> DownloadRecord {
        DownloadRecord {
            url: url.into(),
            status: status.into(),
            ..Default::default()
        }
    }

    #[test]
    fn target_name_cannot_leave_the_folder() {
        let (name, path) = resolve_target(
            Some("C:\\Dl"),
            "C:\\Default",
            "https://e.com/a.zip",
            Some("..\\..\\evil.exe"),
        );
        assert_eq!(name, "....evil.exe");
        assert_eq!(Path::new(&path).parent(), Some(Path::new("C:\\Dl")));
    }

    #[test]
    fn target_falls_back_to_url_name_and_default_folder() {
        let (name, path) = resolve_target(
            None,
            "C:\\Default",
            "https://e.com/dir/My%20File.zip?x=1",
            None,
        );
        assert_eq!(name, "My File.zip");
        assert_eq!(Path::new(&path), Path::new("C:\\Default\\My File.zip"));

        let (name, _) = resolve_target(Some("  "), "C:\\Default", "https://e.com/", Some("  "));
        assert_eq!(name, "download.dat");
    }

    #[test]
    fn interrupted_downloads_load_as_paused() {
        let mut v = vec![
            rec("a", "Downloading"),
            rec("b", "Connecting"),
            rec("c", "Queued"),
            rec("d", "Completed"),
            rec("e", "Error"),
        ];
        reset_interrupted(&mut v);
        let s: Vec<_> = v.iter().map(|r| r.status.as_str()).collect();
        assert_eq!(s, ["Paused", "Paused", "Queued", "Completed", "Error"]);
    }

    #[test]
    fn duplicate_url_reuses_only_unfinished_rows() {
        let v = vec![
            rec("https://e.com/A.zip", "Completed"),
            rec("https://e.com/A.zip", "Paused"),
        ];
        assert_eq!(find_unfinished(&v, "https://E.com/a.zip"), Some(1));
        assert_eq!(find_unfinished(&v[..1], "https://e.com/A.zip"), None);
    }

    #[test]
    fn only_network_and_disk_errors_are_retried() {
        use wdm_core::Error;
        assert!(is_retryable(&Error::Http("reset".into())));
        assert!(is_retryable(&Error::Io(std::io::Error::other("disk"))));
        assert!(!is_retryable(&Error::NonRetryable("404".into())));
        assert!(!is_retryable(&Error::ChecksumMismatch {
            expected: "a".into(),
            actual: "b".into(),
            kept_file: "f".into()
        }));
        assert!(!is_retryable(&Error::Canceled));
    }

    #[test]
    fn queue_is_drained_only_when_nothing_runs_or_waits() {
        assert!(queue_drained(
            &[rec("a", "Completed"), rec("b", "Paused"), rec("c", "Error")],
            0
        ));
        assert!(!queue_drained(&[rec("a", "Queued")], 0));
        assert!(!queue_drained(&[rec("a", "Completed")], 1));
        assert!(!queue_drained(&[rec("a", "Downloading")], 0));
    }

    #[test]
    fn progress_json_uses_names_and_carries_segment_details() {
        let mut seg = DownloadSegment::new(3, 100, 199);
        seg.current_offset = 150;
        seg.state = SegmentState::Downloading;
        seg.speed_bytes_per_second = 2048.0;
        let prog = DownloadProgress {
            total_bytes: 100,
            downloaded_bytes: 50,
            status: DownloadStatus::Downloading,
            segments: vec![seg],
            ..Default::default()
        };
        let v = progress_json(&prog);
        assert_eq!(v["Status"], "Downloading");
        let s = &v["Segments"][0];
        assert_eq!(s["State"], "Downloading");
        assert_eq!(s["DownloadedBytes"], 50);
        assert_eq!(s["TotalBytes"], 100);
        assert_eq!(s["SpeedBytesPerSecond"], 2048.0);
        assert_eq!(s["SegmentId"], 3);
    }
}
