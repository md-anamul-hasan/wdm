use std::path::PathBuf;

use serde::{Deserialize, Serialize};

pub const DEFAULT_USER_AGENT: &str =
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/133.0.0.0 Safari/537.36";

fn user_profile() -> PathBuf {
    std::env::var_os("USERPROFILE")
        .or_else(|| std::env::var_os("HOME"))
        .map(PathBuf::from)
        .unwrap_or_default()
}

fn default_download_dir() -> String {
    user_profile()
        .join("Downloads")
        .to_string_lossy()
        .into_owned()
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "PascalCase", default)]
pub struct AppSettings {
    pub default_download_directory: String,
    pub default_connections: i32,
    pub max_concurrent_downloads: i32,
    pub default_speed_limit_kb: i32,
    pub is_speed_limiter_enabled: bool,
    pub auto_open_file_on_complete: bool,
    pub confirm_before_delete: bool,
    pub enable_dynamic_middle_split: bool,
    pub show_start_download_dialog: bool,
    pub network_timeout_seconds: i32,
    pub user_agent: String,

    // Scheduler settings
    pub scheduler_enabled: bool,
    pub scheduler_start_time: String,
    pub scheduler_stop_time: String,
    pub scheduler_shutdown_on_complete: bool,
    pub scheduler_stop_queue_at_end_time: bool,

    // Theme & UI settings
    pub is_dark_mode: bool,
}

impl Default for AppSettings {
    fn default() -> Self {
        Self {
            default_download_directory: default_download_dir(),
            default_connections: 8,
            max_concurrent_downloads: 3,
            default_speed_limit_kb: 4096,
            is_speed_limiter_enabled: false,
            auto_open_file_on_complete: false,
            confirm_before_delete: true,
            enable_dynamic_middle_split: true,
            show_start_download_dialog: true,
            network_timeout_seconds: 30,
            user_agent: DEFAULT_USER_AGENT.to_string(),
            scheduler_enabled: false,
            scheduler_start_time: "02:00".into(),
            scheduler_stop_time: "06:00".into(),
            scheduler_shutdown_on_complete: false,
            scheduler_stop_queue_at_end_time: true,
            is_dark_mode: false,
        }
    }
}

impl AppSettings {
    /// `%APPDATA%\WDM`, or the folder named by `WDM_DATA_DIR` (portable/test use).
    pub fn data_directory() -> PathBuf {
        match std::env::var("WDM_DATA_DIR") {
            Ok(custom) if !custom.is_empty() => PathBuf::from(custom),
            _ => std::env::var_os("APPDATA")
                .map(PathBuf::from)
                .unwrap_or_else(|| user_profile().join(".config"))
                .join("WDM"),
        }
    }

    fn settings_path() -> PathBuf {
        Self::data_directory().join("settings.json")
    }

    pub fn load() -> Self {
        Self::load_from(&Self::settings_path())
    }

    pub fn load_from(path: &std::path::Path) -> Self {
        let Ok(json) = std::fs::read_to_string(path) else {
            return Self::default();
        };
        match serde_json::from_str::<AppSettings>(json.trim_start_matches('\u{feff}')) {
            Ok(mut s) => {
                s.normalize();
                s
            }
            Err(_) => Self::default(),
        }
    }

    /// Clamps hand-edited or stale values to what the engine and scheduler can handle.
    pub fn normalize(&mut self) {
        self.default_connections = self.default_connections.clamp(1, 32);
        self.max_concurrent_downloads = self.max_concurrent_downloads.clamp(1, 10);
        self.default_speed_limit_kb = self.default_speed_limit_kb.max(1);
        self.network_timeout_seconds = self.network_timeout_seconds.clamp(5, 120);
        if self.default_download_directory.trim().is_empty() {
            self.default_download_directory = default_download_dir();
        }
        if self.user_agent.trim().is_empty() {
            self.user_agent = DEFAULT_USER_AGENT.to_string();
        }
        self.scheduler_start_time = Self::normalize_time(Some(&self.scheduler_start_time), "02:00");
        self.scheduler_stop_time = Self::normalize_time(Some(&self.scheduler_stop_time), "06:00");
    }

    /// Returns `value` as zero-padded HH:mm, or `fallback` if it is not a time of day.
    pub fn normalize_time(value: Option<&str>, fallback: &str) -> String {
        let parsed = value.and_then(|v| {
            let (h, m) = v.trim().split_once(':')?;
            let ok = |s: &str, max_len: usize| {
                (1..=max_len).contains(&s.len()) && s.bytes().all(|b| b.is_ascii_digit())
            };
            if !ok(h, 2) || m.len() != 2 || !ok(m, 2) {
                return None;
            }
            let (h, m): (u32, u32) = (h.parse().ok()?, m.parse().ok()?);
            (h < 24 && m < 60).then_some((h, m))
        });
        match parsed {
            Some((h, m)) => format!("{h:02}:{m:02}"),
            None => fallback.to_string(),
        }
    }

    /// Writes the settings atomically. Returns false when the disk refused it, so the caller can tell the user.
    pub fn save(&self) -> bool {
        self.save_to(&Self::settings_path())
    }

    pub fn save_to(&self, path: &std::path::Path) -> bool {
        let write = || -> std::io::Result<()> {
            if let Some(dir) = path.parent() {
                std::fs::create_dir_all(dir)?;
            }
            let json = serde_json::to_string_pretty(self).map_err(std::io::Error::other)?;
            let mut temp = path.as_os_str().to_owned();
            temp.push(".tmp");
            std::fs::write(&temp, json)?;
            std::fs::rename(&temp, path)
        };
        write().is_ok()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn normalize_clamps_values() {
        let mut s = AppSettings {
            default_connections: 500,
            max_concurrent_downloads: 0,
            default_speed_limit_kb: -4,
            network_timeout_seconds: 1,
            user_agent: "  ".into(),
            default_download_directory: "".into(),
            scheduler_start_time: "7:5".into(),
            scheduler_stop_time: "6:30".into(),
            ..Default::default()
        };
        s.normalize();
        assert_eq!(s.default_connections, 32);
        assert_eq!(s.max_concurrent_downloads, 1);
        assert_eq!(s.default_speed_limit_kb, 1);
        assert_eq!(s.network_timeout_seconds, 5);
        assert_eq!(s.user_agent, DEFAULT_USER_AGENT);
        assert!(!s.default_download_directory.is_empty());
        assert_eq!(s.scheduler_start_time, "02:00"); // "7:5" is not a time of day
        assert_eq!(s.scheduler_stop_time, "06:30");
    }

    #[test]
    fn normalize_time_accepts_h_mm_and_rejects_garbage() {
        assert_eq!(AppSettings::normalize_time(Some("9:05"), "x"), "09:05");
        assert_eq!(AppSettings::normalize_time(Some(" 23:59 "), "x"), "23:59");
        for bad in ["24:00", "12:60", "12", "ab:cd", "1:2:3", "-1:00", ""] {
            assert_eq!(AppSettings::normalize_time(Some(bad), "fb"), "fb", "{bad}");
        }
        assert_eq!(AppSettings::normalize_time(None, "fb"), "fb");
    }

    #[test]
    fn round_trips_and_reads_partial_files() {
        let dir = tempfile::tempdir().unwrap();
        let path = dir.path().join("sub").join("settings.json");
        let s = AppSettings {
            default_connections: 12,
            is_dark_mode: true,
            ..Default::default()
        };
        assert!(s.save_to(&path));
        assert_eq!(AppSettings::load_from(&path), s);

        std::fs::write(&path, "{\"IsDarkMode\": true, \"DefaultConnections\": 99}").unwrap();
        let loaded = AppSettings::load_from(&path);
        assert!(loaded.is_dark_mode);
        assert_eq!(loaded.default_connections, 32);
        assert_eq!(loaded.max_concurrent_downloads, 3);
    }

    #[test]
    fn corrupt_file_yields_defaults() {
        let dir = tempfile::tempdir().unwrap();
        let path = dir.path().join("settings.json");
        std::fs::write(&path, "{ not json").unwrap();
        assert_eq!(AppSettings::load_from(&path), AppSettings::default());
    }
}
