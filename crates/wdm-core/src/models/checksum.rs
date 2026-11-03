use sha2::{Digest, Sha256};
use tokio::io::AsyncReadExt;

/// Parsing and computing SHA-256 checksums as users write them (hex, optional "sha256:" prefix, any case, spaces).
pub struct Sha256Checksum;

impl Sha256Checksum {
    /// Normalises a user-supplied checksum to 64 lowercase hex characters; `None` if it is not one.
    pub fn try_normalize(input: Option<&str>) -> Option<String> {
        let text = input?.trim();
        if text.is_empty() {
            return None;
        }
        let text = match text.get(..7) {
            Some(p) if p.eq_ignore_ascii_case("sha256:") => &text[7..],
            _ => text,
        };
        let text: String = text.chars().filter(|c| !c.is_whitespace()).collect();
        if text.len() != 64 || !text.chars().all(|c| c.is_ascii_hexdigit()) {
            return None;
        }
        Some(text.to_ascii_lowercase())
    }

    pub async fn compute_file(path: &std::path::Path) -> std::io::Result<String> {
        let mut file = tokio::fs::File::open(path).await?;
        let mut hasher = Sha256::new();
        let mut buf = vec![0u8; 1 << 20];
        loop {
            let n = file.read(&mut buf).await?;
            if n == 0 {
                break;
            }
            hasher.update(&buf[..n]);
        }
        Ok(hex::encode(hasher.finalize()))
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    const GOOD: &str = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    #[test]
    fn normalizes_prefix_case_and_spaces() {
        let messy = format!("  SHA256:{}  ", GOOD.to_uppercase());
        assert_eq!(Sha256Checksum::try_normalize(Some(&messy)).as_deref(), Some(GOOD));
        let spaced = format!("{} {}", &GOOD[..32], &GOOD[32..]);
        assert_eq!(Sha256Checksum::try_normalize(Some(&spaced)).as_deref(), Some(GOOD));
    }

    #[test]
    fn rejects_bad_input() {
        assert!(Sha256Checksum::try_normalize(None).is_none());
        assert!(Sha256Checksum::try_normalize(Some("  ")).is_none());
        assert!(Sha256Checksum::try_normalize(Some("abc")).is_none());
        assert!(Sha256Checksum::try_normalize(Some(&"z".repeat(64))).is_none());
        assert!(Sha256Checksum::try_normalize(Some("é")).is_none());
    }

    #[tokio::test]
    async fn computes_file_hash() {
        let dir = tempfile::tempdir().unwrap();
        let p = dir.path().join("empty");
        std::fs::write(&p, b"").unwrap();
        assert_eq!(Sha256Checksum::compute_file(&p).await.unwrap(), GOOD);
    }
}

