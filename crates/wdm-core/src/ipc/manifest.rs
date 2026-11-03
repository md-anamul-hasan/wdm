use serde::{Deserialize, Serialize};
use std::collections::HashSet;

pub const HOST_NAME: &str = "com.wdm.download";
pub const SIDELOADED_CHROME_EXTENSION_ID: &str = "kdjgcdbahccolkkagbckkchfpcchcfne";
pub const STORE_CHROME_EXTENSION_ID: &str = "flnbghmpkajibfdfhbcfhkcjkhlokfgi";
pub const FIREFOX_EXTENSION_ID: &str = "wdm@windowsdownloadmanager.com";

pub const CHROMIUM_REGISTRY_ROOTS: &[&str] = &[
    r"Software\Google\Chrome\NativeMessagingHosts",
    r"Software\Microsoft\Edge\NativeMessagingHosts",
    r"Software\BraveSoftware\Brave-Browser\NativeMessagingHosts",
    r"Software\Chromium\NativeMessagingHosts",
];

pub const FIREFOX_REGISTRY_ROOT: &str = r"Software\Mozilla\NativeMessagingHosts";

pub struct NativeHostManifest;

impl NativeHostManifest {
    pub fn is_chrome_extension_id(id: &str) -> bool {
        let trimmed = id.trim();
        trimmed.len() == 32 && trimmed.chars().all(|c| c.is_ascii_lowercase() && ('a'..='p').contains(&c))
    }

    pub fn allowed_origins<I, S>(additional_ids: I) -> Vec<String>
    where
        I: IntoIterator<Item = S>,
        S: AsRef<str>,
    {
        let mut origins = Vec::new();
        let mut seen = HashSet::new();

        let base_ids = [SIDELOADED_CHROME_EXTENSION_ID, STORE_CHROME_EXTENSION_ID];
        for id in base_ids {
            if seen.insert(id.to_string()) {
                origins.push(format!("chrome-extension://{id}/"));
            }
        }

        for id in additional_ids {
            let s = id.as_ref().trim();
            if Self::is_chrome_extension_id(s) && seen.insert(s.to_string()) {
                origins.push(format!("chrome-extension://{s}/"));
            }
        }

        origins
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum FindingLevel {
    Ok,
    Warning,
    Failure,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DiagnosticFinding {
    pub level: FindingLevel,
    pub message: String,
}

impl DiagnosticFinding {
    pub fn ok(msg: impl Into<String>) -> Self {
        Self { level: FindingLevel::Ok, message: msg.into() }
    }
    pub fn warning(msg: impl Into<String>) -> Self {
        Self { level: FindingLevel::Warning, message: msg.into() }
    }
    pub fn failure(msg: impl Into<String>) -> Self {
        Self { level: FindingLevel::Failure, message: msg.into() }
    }
}

pub struct NativeHostManifestInspector;

impl NativeHostManifestInspector {
    pub fn check_chromium<F>(json: &str, file_exists: F) -> Vec<DiagnosticFinding>
    where
        F: Fn(&str) -> bool,
    {
        let mut findings = Vec::new();
        let value: serde_json::Value = match serde_json::from_str(json) {
            Ok(v) => v,
            Err(e) => {
                findings.push(DiagnosticFinding::failure(format!(
                    "The manifest is not valid JSON ({e}). Re-run: wdm-nativehost --register"
                )));
                return findings;
            }
        };

        if value.get("name").and_then(|v| v.as_str()) != Some(HOST_NAME) {
            findings.push(DiagnosticFinding::failure(
                "Manifest name is not 'com.wdm.download'; browsers look the host up by this name.",
            ));
        }

        if value.get("type").and_then(|v| v.as_str()) != Some("stdio") {
            findings.push(DiagnosticFinding::failure("Manifest type must be 'stdio'."));
        }

        let path_opt = value.get("path").and_then(|v| v.as_str());
        match path_opt {
            None | Some("") => {
                findings.push(DiagnosticFinding::failure("Manifest has no 'path' to wdm-nativehost.exe."));
            }
            Some(path) => {
                if !file_exists(path) {
                    findings.push(DiagnosticFinding::failure(format!(
                        "Host executable was not found at '{path}' (WDM moved or uninstalled?). Re-run: wdm-nativehost --register"
                    )));
                } else {
                    findings.push(DiagnosticFinding::ok(format!("Host program found: {path}")));
                }
            }
        }

        let mut origins = HashSet::new();
        if let Some(arr) = value.get("allowed_origins").and_then(|v| v.as_array()) {
            for o in arr {
                if let Some(s) = o.as_str() {
                    origins.insert(s.to_string());
                }
            }
        }

        let checks = [
            ("Chrome Web Store", STORE_CHROME_EXTENSION_ID),
            ("sideloaded", SIDELOADED_CHROME_EXTENSION_ID),
        ];

        for (label, id) in checks {
            let target = format!("chrome-extension://{id}/");
            if origins.contains(&target) {
                findings.push(DiagnosticFinding::ok(format!("Extension allowed: {label} ({id})")));
            } else {
                findings.push(DiagnosticFinding::warning(format!(
                    "The {label} extension ({id}) is NOT allowed. Re-run: wdm-nativehost --register --extension-id {id}"
                )));
            }
        }

        if origins.iter().any(|o| o.contains('*')) {
            findings.push(DiagnosticFinding::warning(
                "Manifest uses a wildcard origin; WDM never writes one - something else edited this file.",
            ));
        }

        findings
    }

    pub fn check_firefox(json: &str) -> Vec<DiagnosticFinding> {
        let mut findings = Vec::new();
        let value: serde_json::Value = match serde_json::from_str(json) {
            Ok(v) => v,
            Err(e) => {
                findings.push(DiagnosticFinding::failure(format!("The Firefox manifest is not valid JSON ({e}).")));
                return findings;
            }
        };

        let mut ids = HashSet::new();
        if let Some(arr) = value.get("allowed_extensions").and_then(|v| v.as_array()) {
            for o in arr {
                if let Some(s) = o.as_str() {
                    ids.insert(s.to_string());
                }
            }
        }

        if ids.contains(FIREFOX_EXTENSION_ID) {
            findings.push(DiagnosticFinding::ok(format!("Extension allowed: Firefox ({FIREFOX_EXTENSION_ID})")));
        } else {
            findings.push(DiagnosticFinding::failure(format!(
                "Firefox extension '{FIREFOX_EXTENSION_ID}' is not allowed. Re-run: wdm-nativehost --register"
            )));
        }

        findings
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_is_chrome_extension_id() {
        assert!(NativeHostManifest::is_chrome_extension_id(STORE_CHROME_EXTENSION_ID));
        assert!(NativeHostManifest::is_chrome_extension_id(SIDELOADED_CHROME_EXTENSION_ID));
        assert!(!NativeHostManifest::is_chrome_extension_id("invalid-id"));
        assert!(!NativeHostManifest::is_chrome_extension_id("flnbghmpkajibfdfhbcfhkcjkhlokfgZ")); // capital Z
    }

    #[test]
    fn test_check_chromium() {
        let json = format!(
            r#"{{
                "name": "com.wdm.download",
                "description": "desc",
                "path": "C:\\Program Files\\WDM\\wdm-nativehost.exe",
                "type": "stdio",
                "allowed_origins": [
                    "chrome-extension://{SIDELOADED_CHROME_EXTENSION_ID}/",
                    "chrome-extension://{STORE_CHROME_EXTENSION_ID}/"
                ]
            }}"#
        );

        let findings = NativeHostManifestInspector::check_chromium(&json, |p| p.contains("wdm-nativehost"));
        assert!(!findings.is_empty());
        assert!(findings.iter().all(|f| f.level != FindingLevel::Failure));
    }
}
