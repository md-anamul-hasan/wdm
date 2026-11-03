use thiserror::Error;

pub type Result<T> = std::result::Result<T, Error>;

#[derive(Debug, Error)]
pub enum Error {
    #[error("{0}")]
    InvalidArgument(String),

    #[error("{0}")]
    Io(#[from] std::io::Error),

    #[error("{0}")]
    Http(String),

    /// Failures that retrying the same request cannot fix.
    #[error("{0}")]
    NonRetryable(String),

    /// The server advertised byte ranges but answered a ranged request with the whole body.
    #[error("{0}")]
    RangeIgnored(String),

    #[error("SHA-256 mismatch: expected {expected}, got {actual}. The downloaded data was kept as '{kept_file}'; restart the download or correct the checksum.")]
    ChecksumMismatch {
        expected: String,
        actual: String,
        kept_file: String,
    },

    #[error("The download was canceled.")]
    Canceled,

    #[error("{0}")]
    Json(#[from] serde_json::Error),
}

impl From<reqwest::Error> for Error {
    fn from(e: reqwest::Error) -> Self {
        use std::error::Error as _;
        let mut msg = e.to_string();
        let mut source = e.source();
        while let Some(s) = source {
            msg.push_str(": ");
            msg.push_str(&s.to_string());
            source = s.source();
        }
        Error::Http(msg)
    }
}

impl Error {
    pub fn is_canceled(&self) -> bool {
        matches!(self, Error::Canceled)
    }
}

