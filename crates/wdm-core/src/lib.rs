//! WDM core: the download engine (HTTP range segmentation, resume state, persistence).
//!
//! No UI references. The on-disk formats (`settings.json`, `downloads.json`, `*.wdm-state`,
//! `*.wdm-part`) are byte-compatible with the C# implementation so existing user data keeps working.

pub mod engine;
pub mod error;
pub mod ipc;
pub mod models;

pub use error::{Error, Result};
