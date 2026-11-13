use serde::{Deserialize, Serialize};

use super::secret;

/// Per-download HTTP context captured from the browser (or CLI): referer, cookies, user agent
/// and optional basic-auth credentials. Sent with every request of the download.
#[derive(Debug, Clone, Default, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub struct DownloadRequestOptions {
    pub referer: Option<String>,
    #[serde(default, with = "secret")]
    pub cookies: Option<String>,
    /// Host the cookies were captured for. Defaults to the download's own host.
    pub cookie_host: Option<String>,
    pub user_agent: Option<String>,
    #[serde(default, with = "secret")]
    pub username: Option<String>,
    #[serde(default, with = "secret")]
    pub password: Option<String>,
    /// Caller-chosen file name; overrides the name derived from the server response.
    pub file_name: Option<String>,
    /// Optional SHA-256 the finished file must match. Not a secret.
    pub expected_sha256: Option<String>,
    /// Connections for this download; the setting's default applies when absent. Left out of the file when unset.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub connections: Option<i32>,
}

impl DownloadRequestOptions {
    /// Copy without cookies and credentials (what is kept once a download has finished).
    pub fn without_secrets(&self) -> Self {
        Self {
            referer: self.referer.clone(),
            cookie_host: self.cookie_host.clone(),
            user_agent: self.user_agent.clone(),
            file_name: self.file_name.clone(),
            expected_sha256: self.expected_sha256.clone(),
            connections: self.connections,
            ..Default::default()
        }
    }

    pub fn has_secrets(&self) -> bool {
        [&self.cookies, &self.username, &self.password]
            .iter()
            .any(|v| v.as_deref().is_some_and(|s| !s.is_empty()))
    }
}
