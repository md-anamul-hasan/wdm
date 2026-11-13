use crate::models::DownloadRequestOptions;
use serde::{Deserialize, Serialize};
use url::Url;

/// A download handed to the running WDM instance by another process
/// (native messaging host, command line, shell integration).
#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub struct ExternalDownloadRequest {
    #[serde(default)]
    pub urls: Vec<String>,
    pub referer: Option<String>,
    pub cookies: Option<String>,
    pub cookie_host: Option<String>,
    pub user_agent: Option<String>,
    pub file_name: Option<String>,
    pub destination_directory: Option<String>,
    pub sha256: Option<String>,
    #[serde(default)]
    pub silent: bool,
    #[serde(default)]
    pub queue_only: bool,
    #[serde(default)]
    pub activate_only: bool,
    /// Connections for this download (the add dialog's choice); the setting's default when absent.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub connections: Option<i32>,
}

impl ExternalDownloadRequest {
    pub fn is_empty(&self) -> bool {
        self.urls.is_empty()
    }

    pub fn to_options(&self) -> DownloadRequestOptions {
        let single = self.urls.len() == 1;
        DownloadRequestOptions {
            referer: self.referer.clone(),
            cookies: self.cookies.clone(),
            cookie_host: self.cookie_host.clone(),
            user_agent: self.user_agent.clone(),
            file_name: if single { self.file_name.clone() } else { None },
            expected_sha256: if single { self.sha256.clone() } else { None },
            connections: self.connections,
            ..Default::default()
        }
    }

    pub fn is_http_url(value: &str) -> bool {
        if let Ok(u) = Url::parse(value) {
            u.scheme() == "http" || u.scheme() == "https"
        } else {
            false
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_serialization_compatibility() {
        let req = ExternalDownloadRequest {
            urls: vec!["https://example.com/file.zip".into()],
            referer: Some("https://example.com/".into()),
            cookies: Some("session=123".into()),
            cookie_host: Some("example.com".into()),
            user_agent: Some("CustomAgent".into()),
            file_name: Some("file.zip".into()),
            destination_directory: Some("C:\\Downloads".into()),
            sha256: Some("abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890".into()),
            silent: true,
            queue_only: false,
            activate_only: false,
            connections: Some(4),
        };

        let json = serde_json::to_string(&req).unwrap();
        assert!(json.contains("\"Urls\":[\"https://example.com/file.zip\"]"));
        assert!(json.contains("\"Silent\":true"));
        assert!(json.contains("\"CookieHost\":\"example.com\""));
        assert!(json.contains("\"Connections\":4"));
        assert!(!serde_json::to_string(&ExternalDownloadRequest::default())
            .unwrap()
            .contains("Connections"));

        let deserialized: ExternalDownloadRequest = serde_json::from_str(&json).unwrap();
        assert_eq!(req, deserialized);
        assert!(!deserialized.is_empty());
        let opts = deserialized.to_options();
        assert_eq!(opts.file_name, Some("file.zip".into()));
    }
}
