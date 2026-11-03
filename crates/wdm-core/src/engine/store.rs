use std::io;
use std::path::{Path, PathBuf};
use std::sync::Mutex;

use chrono::{DateTime, FixedOffset, Local, NaiveDateTime, TimeZone};
use serde::{Deserialize, Deserializer, Serialize};

use crate::models::{secret, AppSettings, DownloadRequestOptions};

/// .NET wrote `DateTime` values with or without an offset; accept both.
mod flex_datetime {
    use super::*;

    pub fn parse(s: &str) -> Option<DateTime<FixedOffset>> {
        DateTime::parse_from_rfc3339(s).ok().or_else(|| {
            let naive = NaiveDateTime::parse_from_str(s, "%Y-%m-%dT%H:%M:%S%.f").ok()?;
            Some(Local.from_local_datetime(&naive).earliest()?.fixed_offset())
        })
    }

    pub fn deserialize<'de, D: Deserializer<'de>>(d: D) -> Result<DateTime<FixedOffset>, D::Error> {
        let s = String::deserialize(d)?;
        parse(&s).ok_or_else(|| serde::de::Error::custom("invalid date"))
    }

    pub mod opt {
        use super::*;
        pub fn deserialize<'de, D: Deserializer<'de>>(d: D) -> Result<Option<DateTime<FixedOffset>>, D::Error> {
            match Option::<String>::deserialize(d)? {
                None => Ok(None),
                Some(s) => parse(&s).map(Some).ok_or_else(|| serde::de::Error::custom("invalid date")),
            }
        }
    }
}

fn new_id() -> String {
    uuid::Uuid::new_v4().simple().to_string()
}

fn now() -> DateTime<FixedOffset> {
    Local::now().fixed_offset()
}

fn paused() -> String {
    "Paused".into()
}

fn unknown_size() -> i64 {
    -1
}

/// One persisted row of the download list.
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub struct DownloadRecord {
    #[serde(default = "new_id")]
    pub id: String,
    #[serde(default)]
    pub url: String,
    #[serde(default)]
    pub file_name: String,
    #[serde(default)]
    pub target_file_path: String,
    #[serde(default = "paused")]
    pub status: String,
    #[serde(default = "unknown_size")]
    pub total_bytes: i64,
    #[serde(default)]
    pub downloaded_bytes: i64,
    #[serde(default = "now", deserialize_with = "flex_datetime::deserialize")]
    pub added_at: DateTime<FixedOffset>,
    #[serde(default, deserialize_with = "flex_datetime::opt::deserialize")]
    pub completed_at: Option<DateTime<FixedOffset>>,
    #[serde(default)]
    pub options: Option<DownloadRequestOptions>,
}

impl Default for DownloadRecord {
    fn default() -> Self {
        Self {
            id: new_id(),
            url: String::new(),
            file_name: String::new(),
            target_file_path: String::new(),
            status: paused(),
            total_bytes: -1,
            downloaded_bytes: 0,
            added_at: now(),
            completed_at: None,
            options: None,
        }
    }
}

impl DownloadRecord {
    /// What is persisted: a finished download no longer needs its cookies or credentials.
    fn for_storage(&self) -> Self {
        let mut copy = self.clone();
        if self.status == "Completed" {
            if let Some(o) = &self.options {
                if o.has_secrets() {
                    copy.options = Some(o.without_secrets());
                }
            }
        }
        copy
    }

    fn has_plaintext_or_finished_secrets(&self) -> bool {
        let Some(o) = &self.options else { return false };
        secret::is_legacy_plaintext(o.cookies.as_deref())
            || secret::is_legacy_plaintext(o.username.as_deref())
            || secret::is_legacy_plaintext(o.password.as_deref())
            || (self.status == "Completed" && o.has_secrets())
    }
}

/// Persists the download list to `%APPDATA%\WDM\downloads.json` (atomic replace).
pub struct DownloadStore {
    path: PathBuf,
    lock: Mutex<()>,
}

impl DownloadStore {
    pub fn new(path: Option<PathBuf>) -> Self {
        Self { path: path.unwrap_or_else(|| AppSettings::data_directory().join("downloads.json")), lock: Mutex::new(()) }
    }

    pub fn path(&self) -> &Path {
        &self.path
    }

    pub fn load(&self) -> Vec<DownloadRecord> {
        let json = match std::fs::read_to_string(&self.path) {
            Ok(j) => j,
            Err(_) => return Vec::new(),
        };
        match serde_json::from_str::<Vec<DownloadRecord>>(json.trim_start_matches('\u{feff}')) {
            Ok(records) => {
                // Migrate: lists written by older versions hold cookies/passwords in clear (and secrets of finished downloads)
                if records.iter().any(DownloadRecord::has_plaintext_or_finished_secrets) {
                    self.save(&records);
                }
                records
            }
            Err(_) => {
                // Keep the unreadable list so the next save does not destroy it
                let mut corrupt = self.path.as_os_str().to_owned();
                corrupt.push(".corrupt");
                let _ = std::fs::rename(&self.path, corrupt);
                Vec::new()
            }
        }
    }

    /// Writes the list atomically. Returns false when the disk refused it, so the caller can tell the user.
    pub fn save(&self, records: &[DownloadRecord]) -> bool {
        let _guard = self.lock.lock().unwrap();
        let write = || -> io::Result<()> {
            if let Some(dir) = self.path.parent() {
                std::fs::create_dir_all(dir)?;
            }
            let stored: Vec<DownloadRecord> = records.iter().map(DownloadRecord::for_storage).collect();
            let json = serde_json::to_string_pretty(&stored).map_err(io::Error::other)?;
            let mut temp = self.path.as_os_str().to_owned();
            temp.push(".tmp");
            std::fs::write(&temp, json)?;
            std::fs::rename(&temp, &self.path)
        };
        write().is_ok()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    struct XorCipher;
    impl secret::SecretCipher for XorCipher {
        fn protect(&self, p: &[u8]) -> Option<Vec<u8>> {
            Some(p.iter().map(|b| b ^ 0x5a).collect())
        }
        fn unprotect(&self, p: &[u8]) -> Option<Vec<u8>> {
            self.protect(p)
        }
    }

    /// The cipher is process-global, so tests touching secrets share one lock.
    fn cipher_lock() -> std::sync::MutexGuard<'static, ()> {
        static L: Mutex<()> = Mutex::new(());
        let g = L.lock().unwrap_or_else(|e| e.into_inner());
        secret::set_cipher(Some(Box::new(XorCipher)));
        g
    }

    fn record(status: &str) -> DownloadRecord {
        DownloadRecord {
            url: "https://e.com/f.zip".into(),
            status: status.into(),
            options: Some(DownloadRequestOptions {
                cookies: Some("sid=SECRETCOOKIE".into()),
                password: Some("hunter2".into()),
                referer: Some("https://e.com/".into()),
                ..Default::default()
            }),
            ..Default::default()
        }
    }

    #[test]
    fn secrets_are_encrypted_on_disk_and_round_trip() {
        let _g = cipher_lock();
        let dir = tempfile::tempdir().unwrap();
        let store = DownloadStore::new(Some(dir.path().join("downloads.json")));
        assert!(store.save(&[record("Paused")]));

        let raw = std::fs::read_to_string(store.path()).unwrap();
        assert!(!raw.contains("SECRETCOOKIE") && !raw.contains("hunter2"));
        assert!(raw.contains("dpapi1:"));

        let loaded = store.load();
        let o = loaded[0].options.as_ref().unwrap();
        assert_eq!(o.cookies.as_deref(), Some("sid=SECRETCOOKIE"));
        assert_eq!(o.password.as_deref(), Some("hunter2"));
    }

    #[test]
    fn completed_downloads_drop_their_secrets() {
        let _g = cipher_lock();
        let dir = tempfile::tempdir().unwrap();
        let store = DownloadStore::new(Some(dir.path().join("downloads.json")));
        store.save(&[record("Completed")]);
        let o = store.load()[0].options.clone().unwrap();
        assert!(o.cookies.is_none() && o.password.is_none());
        assert_eq!(o.referer.as_deref(), Some("https://e.com/"));
    }

    #[test]
    fn legacy_plaintext_is_migrated_on_load() {
        let _g = cipher_lock();
        let dir = tempfile::tempdir().unwrap();
        let store = DownloadStore::new(Some(dir.path().join("downloads.json")));
        std::fs::write(
            store.path(),
            r#"[{"Id":"a","Url":"u","FileName":"f","TargetFilePath":"t","Status":"Paused","TotalBytes":5,"DownloadedBytes":1,
                 "AddedAt":"2025-03-04T05:06:07.1234567","CompletedAt":null,
                 "Options":{"Cookies":"plainsecret"}}]"#,
        )
        .unwrap();
        let loaded = store.load();
        assert_eq!(loaded[0].options.as_ref().unwrap().cookies.as_deref(), Some("plainsecret"));
        let raw = std::fs::read_to_string(store.path()).unwrap();
        assert!(!raw.contains("plainsecret"), "file should have been rewritten protected");
    }

    #[test]
    fn corrupt_list_is_kept_aside() {
        let dir = tempfile::tempdir().unwrap();
        let store = DownloadStore::new(Some(dir.path().join("downloads.json")));
        std::fs::write(store.path(), "[{ broken").unwrap();
        assert!(store.load().is_empty());
        assert!(dir.path().join("downloads.json.corrupt").exists());
        assert!(!store.path().exists());
    }

    #[test]
    fn missing_file_is_an_empty_list() {
        let dir = tempfile::tempdir().unwrap();
        assert!(DownloadStore::new(Some(dir.path().join("nope.json"))).load().is_empty());
    }
}

