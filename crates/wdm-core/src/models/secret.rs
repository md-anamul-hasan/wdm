//! Reversible encryption for secrets stored on disk.
//!
//! Cookies and passwords are written as `dpapi1:<base64>` (Windows DPAPI, CurrentUser scope, entropy
//! `WDM.secret.v1`) - the same format as the C# app, so existing files stay readable. Values without
//! the prefix are legacy plaintext and are still read. If encryption fails the secret is dropped
//! rather than written in clear.

use std::sync::{OnceLock, RwLock};

use base64::{engine::general_purpose::STANDARD, Engine};
use serde::{Deserialize, Deserializer, Serializer};

pub const PREFIX: &str = "dpapi1:";

pub trait SecretCipher: Send + Sync {
    fn protect(&self, plain: &[u8]) -> Option<Vec<u8>>;
    fn unprotect(&self, protected: &[u8]) -> Option<Vec<u8>>;
}

fn slot() -> &'static RwLock<Option<Box<dyn SecretCipher>>> {
    static SLOT: OnceLock<RwLock<Option<Box<dyn SecretCipher>>>> = OnceLock::new();
    SLOT.get_or_init(|| {
        #[cfg(windows)]
        let default: Option<Box<dyn SecretCipher>> = Some(Box::new(DpapiCipher));
        #[cfg(not(windows))]
        let default: Option<Box<dyn SecretCipher>> = None;
        RwLock::new(default)
    })
}

/// Replaces the cipher (tests use a reversible fake so they run on any OS).
pub fn set_cipher(cipher: Option<Box<dyn SecretCipher>>) {
    *slot().write().unwrap() = cipher;
}

pub fn protect(plain: Option<&str>) -> Option<String> {
    let plain = plain?;
    if plain.is_empty() {
        return Some(String::new());
    }
    let guard = slot().read().unwrap();
    match guard.as_ref() {
        None => Some(plain.to_string()),
        Some(c) => c
            .protect(plain.as_bytes())
            .map(|b| format!("{PREFIX}{}", STANDARD.encode(b))),
    }
}

pub fn unprotect(stored: Option<&str>) -> Option<String> {
    let stored = stored?;
    let Some(rest) = stored.strip_prefix(PREFIX) else {
        return Some(stored.to_string()); // legacy plaintext (or empty)
    };
    let guard = slot().read().unwrap();
    let cipher = guard.as_ref()?;
    let bytes = STANDARD.decode(rest).ok()?;
    String::from_utf8(cipher.unprotect(&bytes)?).ok() // other user/machine, or damaged: "no secret"
}

pub fn is_legacy_plaintext(stored: Option<&str>) -> bool {
    matches!(stored, Some(s) if !s.is_empty() && !s.starts_with(PREFIX))
}

/// serde adapter for `Option<String>` fields: protect on write, unprotect on read.
pub fn serialize<S: Serializer>(value: &Option<String>, s: S) -> Result<S::Ok, S::Error> {
    match protect(value.as_deref()) {
        Some(v) => s.serialize_some(&v),
        None => s.serialize_none(),
    }
}

pub fn deserialize<'de, D: Deserializer<'de>>(d: D) -> Result<Option<String>, D::Error> {
    let raw = Option::<String>::deserialize(d)?;
    Ok(unprotect(raw.as_deref()))
}

#[cfg(windows)]
pub struct DpapiCipher;

#[cfg(windows)]
mod dpapi {
    use windows::Win32::Foundation::{LocalFree, HLOCAL};
    use windows::Win32::Security::Cryptography::{
        CryptProtectData, CryptUnprotectData, CRYPT_INTEGER_BLOB,
    };

    const ENTROPY: &[u8] = b"WDM.secret.v1";

    fn blob(data: &[u8]) -> CRYPT_INTEGER_BLOB {
        CRYPT_INTEGER_BLOB {
            cbData: data.len() as u32,
            pbData: data.as_ptr() as *mut u8,
        }
    }

    unsafe fn take(out: CRYPT_INTEGER_BLOB) -> Vec<u8> {
        let v = std::slice::from_raw_parts(out.pbData, out.cbData as usize).to_vec();
        let _ = LocalFree(HLOCAL(out.pbData as *mut _));
        v
    }

    pub fn protect(plain: &[u8]) -> Option<Vec<u8>> {
        let input = blob(plain);
        let entropy = blob(ENTROPY);
        let mut out = CRYPT_INTEGER_BLOB::default();
        unsafe {
            CryptProtectData(&input, None, Some(&entropy), None, None, 0, &mut out).ok()?;
            Some(take(out))
        }
    }

    pub fn unprotect(protected: &[u8]) -> Option<Vec<u8>> {
        let input = blob(protected);
        let entropy = blob(ENTROPY);
        let mut out = CRYPT_INTEGER_BLOB::default();
        unsafe {
            CryptUnprotectData(&input, None, Some(&entropy), None, None, 0, &mut out).ok()?;
            Some(take(out))
        }
    }
}

#[cfg(windows)]
impl SecretCipher for DpapiCipher {
    fn protect(&self, plain: &[u8]) -> Option<Vec<u8>> {
        dpapi::protect(plain)
    }
    fn unprotect(&self, protected: &[u8]) -> Option<Vec<u8>> {
        dpapi::unprotect(protected)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[cfg(windows)]
    #[test]
    fn dpapi_round_trips() {
        // Uses the cipher directly: the global slot is shared with tests that install a fake.
        let c = DpapiCipher;
        let enc = c.protect(b"cookie=secret; other=1").unwrap();
        assert!(!String::from_utf8_lossy(&enc).contains("secret"));
        assert_eq!(c.unprotect(&enc).as_deref(), Some(&b"cookie=secret; other=1"[..]));
        assert!(c.unprotect(b"garbage").is_none());
    }

    #[test]
    fn legacy_plaintext_is_read_and_flagged() {
        assert_eq!(unprotect(Some("plain")).as_deref(), Some("plain"));
        assert!(is_legacy_plaintext(Some("plain")));
        assert!(!is_legacy_plaintext(Some("")));
        assert!(!is_legacy_plaintext(None));
        assert!(!is_legacy_plaintext(Some("dpapi1:AAAA")));
    }

    #[test]
    fn damaged_secret_is_treated_as_none() {
        assert_eq!(unprotect(Some("dpapi1:!!!not base64!!!")), None);
    }
}
