use serde::{Deserialize, Serialize};

/// Serialized by name; the C# app wrote enums as numbers, so both forms are read.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub enum DownloadStatus {
    #[default]
    Queued,
    Connecting,
    Downloading,
    Paused,
    Completed,
    Error,
    Canceled,
}

impl DownloadStatus {
    const ALL: [DownloadStatus; 7] = [
        DownloadStatus::Queued,
        DownloadStatus::Connecting,
        DownloadStatus::Downloading,
        DownloadStatus::Paused,
        DownloadStatus::Completed,
        DownloadStatus::Error,
        DownloadStatus::Canceled,
    ];

    pub fn as_str(self) -> &'static str {
        match self {
            Self::Queued => "Queued",
            Self::Connecting => "Connecting",
            Self::Downloading => "Downloading",
            Self::Paused => "Paused",
            Self::Completed => "Completed",
            Self::Error => "Error",
            Self::Canceled => "Canceled",
        }
    }
}

impl Serialize for DownloadStatus {
    fn serialize<S: serde::Serializer>(&self, s: S) -> Result<S::Ok, S::Error> {
        // The C# app wrote the enum as its numeric value; keep that so older readers still work.
        s.serialize_u8(*self as u8)
    }
}

impl<'de> Deserialize<'de> for DownloadStatus {
    fn deserialize<D: serde::Deserializer<'de>>(d: D) -> Result<Self, D::Error> {
        #[derive(Deserialize)]
        #[serde(untagged)]
        enum Repr {
            Num(u8),
            Name(String),
        }
        match Repr::deserialize(d)? {
            Repr::Num(n) => Self::ALL
                .get(n as usize)
                .copied()
                .ok_or_else(|| serde::de::Error::custom("unknown download status")),
            Repr::Name(s) => Self::ALL
                .iter()
                .copied()
                .find(|v| v.as_str().eq_ignore_ascii_case(&s))
                .ok_or_else(|| serde::de::Error::custom("unknown download status")),
        }
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
#[repr(u8)]
pub enum SegmentState {
    #[default]
    Idle = 0,
    Connecting = 1,
    Downloading = 2,
    Completed = 3,
    Error = 4,
}

impl SegmentState {
    pub fn from_u8(v: u8) -> Self {
        match v {
            1 => Self::Connecting,
            2 => Self::Downloading,
            3 => Self::Completed,
            4 => Self::Error,
            _ => Self::Idle,
        }
    }
}
