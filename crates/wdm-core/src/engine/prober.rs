use std::path::{Path, PathBuf};

use chrono::{DateTime, FixedOffset, Utc};
use percent_encoding::percent_decode_str;
use reqwest::header::{
    HeaderMap, ACCEPT_RANGES, CONTENT_DISPOSITION, CONTENT_LENGTH, CONTENT_RANGE, CONTENT_TYPE,
    ETAG, LAST_MODIFIED,
};
use reqwest::{Client, Method, StatusCode};
use tokio_util::sync::CancellationToken;

use super::http::{self, ByteRange};
use super::resume;
use crate::models::{DownloadMetadata, DownloadRequestOptions};
use crate::{Error, Result};

/// Parsed `Content-Range` header (`bytes 0-0/1234`, `bytes */0`, `bytes 5-9/*`).
#[derive(Debug, PartialEq, Eq)]
pub struct ContentRange {
    pub from: Option<i64>,
    pub length: Option<i64>,
}

pub fn parse_content_range(value: &str) -> Option<ContentRange> {
    let rest = value.trim().strip_prefix("bytes")?.trim_start();
    let (range, length) = rest.split_once('/')?;
    let from = match range.trim() {
        "*" => None,
        r => Some(r.split_once('-')?.0.trim().parse().ok()?),
    };
    let length = match length.trim() {
        "*" => None,
        l => Some(l.parse().ok()?),
    };
    Some(ContentRange { from, length })
}

const RESERVED_DEVICE_NAMES: &[&str] = &[
    "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8",
    "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
];

/// Strips path separators and invalid characters, Windows device names and trailing dots/spaces.
/// Windows rules on every platform: server-supplied names must not traverse paths wherever they are parsed.
pub fn sanitize_file_name(name: &str) -> String {
    let is_invalid = |c: char| {
        c.is_control() || matches!(c, '/' | '\\' | ':' | '*' | '?' | '"' | '<' | '>' | '|')
    };
    let cleaned: String = name.chars().filter(|&c| !is_invalid(c)).collect();
    let mut clean = cleaned.trim().trim_end_matches(['.', ' ']).to_string();
    if clean.chars().count() > 200 {
        clean = clean
            .chars()
            .take(200)
            .collect::<String>()
            .trim_end_matches(['.', ' '])
            .to_string();
    }
    if clean.is_empty() {
        return "download.dat".into();
    }
    let stem = clean.split('.').next().unwrap_or(&clean);
    if RESERVED_DEVICE_NAMES
        .iter()
        .any(|r| r.eq_ignore_ascii_case(stem))
    {
        format!("_{clean}")
    } else {
        clean
    }
}

/// Splits on `;` outside double quotes.
fn split_params(s: &str) -> Vec<&str> {
    let (mut parts, mut start, mut quoted) = (Vec::new(), 0, false);
    for (i, c) in s.char_indices() {
        match c {
            '"' => quoted = !quoted,
            ';' if !quoted => {
                parts.push(&s[start..i]);
                start = i + 1;
            }
            _ => {}
        }
    }
    parts.push(&s[start..]);
    parts
}

/// File name from a `Content-Disposition` header; RFC 5987 `filename*` wins over `filename`.
pub fn parse_content_disposition(value: &str) -> Option<String> {
    let (mut star, mut plain) = (None, None);
    for part in split_params(value).into_iter().skip(1) {
        let Some((key, val)) = part.split_once('=') else {
            continue;
        };
        let (key, val) = (key.trim().to_ascii_lowercase(), val.trim());
        match key.as_str() {
            "filename*" => {
                let encoded = val.trim_matches(['"', '\'']);
                // charset'language'value
                let raw = match encoded.splitn(3, '\'').collect::<Vec<_>>().as_slice() {
                    [_, _, v] => v.to_string(),
                    _ => encoded.to_string(),
                };
                star = Some(percent_decode_str(&raw).decode_utf8_lossy().into_owned());
            }
            "filename" => plain = Some(val.trim_matches(['"', '\'']).to_string()),
            _ => {}
        }
    }
    star.filter(|s| !s.trim().is_empty())
        .or(plain.filter(|s| !s.trim().is_empty()))
}

fn extract_file_name(headers: &HeaderMap, url: &str) -> String {
    if let Some(name) = headers
        .get(CONTENT_DISPOSITION)
        .and_then(|v| v.to_str().ok())
        .and_then(parse_content_disposition)
    {
        return sanitize_file_name(&name);
    }
    file_name_from_url(url)
        .unwrap_or_else(|| format!("download_{}.dat", Utc::now().format("%Y%m%d_%H%M%S")))
}

/// Last path segment of the URL, percent-decoded and sanitised; `None` when the path has no usable name.
pub fn file_name_from_url(url: &str) -> Option<String> {
    let parsed = url::Url::parse(url).ok()?;
    let last = parsed
        .path_segments()
        .and_then(|mut s| s.next_back())
        .filter(|s| !s.is_empty())?;
    let decoded = percent_decode_str(last).decode_utf8_lossy();
    (!decoded.trim().is_empty()).then(|| sanitize_file_name(&decoded))
}

fn header_str(h: &HeaderMap, name: reqwest::header::HeaderName) -> Option<&str> {
    h.get(name).and_then(|v| v.to_str().ok())
}

fn default_download_dir() -> PathBuf {
    std::env::var_os("USERPROFILE")
        .or_else(|| std::env::var_os("HOME"))
        .map(PathBuf::from)
        .unwrap_or_default()
        .join("Downloads")
}

pub async fn probe(
    client: &Client,
    url: &str,
    destination_directory: Option<&Path>,
    options: Option<&DownloadRequestOptions>,
    ct: &CancellationToken,
) -> Result<DownloadMetadata> {
    let mut metadata = DownloadMetadata {
        url: url.to_string(),
        options: options.cloned(),
        ..Default::default()
    };

    // 1. Try HEAD first; servers that reject it (403/405/...) get a 1-byte ranged GET instead
    let mut response = http::send(client, Method::HEAD, url, options, None, ct).await?;
    if !response.status().is_success() {
        response = http::send(
            client,
            Method::GET,
            url,
            options,
            Some(ByteRange {
                from: 0,
                to: Some(0),
            }),
            ct,
        )
        .await?;

        // 416 with "Content-Range: bytes */0" is how servers say "this file is empty"
        let empty_file = response.status() == StatusCode::RANGE_NOT_SATISFIABLE
            && header_str(response.headers(), CONTENT_RANGE)
                .and_then(parse_content_range)
                .is_some_and(|r| r.length == Some(0));
        if !empty_file && !response.status().is_success() {
            let s = response.status();
            return Err(Error::Http(format!(
                "Response status code does not indicate success: {} ({}).",
                s.as_u16(),
                s.canonical_reason().unwrap_or("")
            )));
        }
    }

    let status = response.status();
    let headers = response.headers();
    metadata.supports_ranges = header_str(headers, ACCEPT_RANGES)
        .is_some_and(|v| v.to_ascii_lowercase().contains("bytes"))
        || status == StatusCode::PARTIAL_CONTENT;

    let content_range_len = header_str(headers, CONTENT_RANGE)
        .and_then(parse_content_range)
        .and_then(|r| r.length);
    if status == StatusCode::RANGE_NOT_SATISFIABLE {
        metadata.total_bytes = 0;
        metadata.supports_ranges = false;
    } else if let Some(len) = content_range_len {
        metadata.total_bytes = len;
        metadata.supports_ranges = true;
    } else if let Some(len) =
        header_str(headers, CONTENT_LENGTH).and_then(|v| v.trim().parse::<i64>().ok())
    {
        // (not Response::content_length: for HEAD that reports the empty body, not the resource size)
        metadata.total_bytes = len;
    }

    metadata.etag =
        header_str(headers, ETAG).map(|t| t.trim().trim_start_matches("W/").to_string());
    metadata.last_modified = header_str(headers, LAST_MODIFIED)
        .and_then(|v| DateTime::<FixedOffset>::parse_from_rfc2822(v).ok());
    metadata.content_type = header_str(headers, CONTENT_TYPE)
        .map(|v| v.split(';').next().unwrap_or("").trim().to_string())
        .filter(|v| !v.is_empty());

    metadata.file_name = match options
        .and_then(|o| o.file_name.as_deref())
        .filter(|n| !n.trim().is_empty())
    {
        Some(n) => sanitize_file_name(n),
        None => extract_file_name(headers, url),
    };
    drop(response);

    let dest_dir = destination_directory
        .map(Path::to_path_buf)
        .unwrap_or_else(default_download_dir);
    tokio::fs::create_dir_all(&dest_dir).await?;

    let target = reserve_file_path(&dest_dir.join(&metadata.file_name), url).await?;
    metadata.target_file_path = target.to_string_lossy().into_owned();
    metadata.part_file_path = Some(resume::part_path(&metadata.target_file_path));
    Ok(metadata)
}

fn numbered(dir: &Path, stem: &str, ext: &str, counter: u32) -> PathBuf {
    dir.join(format!("{stem} ({counter}){ext}"))
}

/// Picks the final name for this download and claims it by creating its `.wdm-part` file, so a concurrent
/// download of the same name cannot take it. A name holding a resumable checkpoint for the same URL is returned as-is.
/// Existing files, checkpoints and part files of other downloads are never touched.
async fn reserve_file_path(full_path: &Path, url: &str) -> Result<PathBuf> {
    let dir = full_path.parent().unwrap_or(Path::new("")).to_path_buf();
    let stem = full_path
        .file_stem()
        .map(|s| s.to_string_lossy().into_owned())
        .unwrap_or_default();
    let ext = full_path
        .extension()
        .map(|e| format!(".{}", e.to_string_lossy()))
        .unwrap_or_default();

    for counter in 0u32.. {
        let candidate = if counter == 0 {
            full_path.to_path_buf()
        } else {
            numbered(&dir, &stem, &ext, counter)
        };
        let candidate_str = candidate.to_string_lossy().into_owned();

        if resume::checkpoint_path(&candidate_str).exists() {
            if resume::load_checkpoint(&candidate_str)
                .await
                .is_some_and(|c| c.url == url)
            {
                return Ok(candidate); // resume
            }
            continue;
        }
        if candidate.exists() {
            continue;
        }

        match tokio::fs::OpenOptions::new()
            .write(true)
            .create_new(true)
            .open(resume::part_path(&candidate_str))
            .await
        {
            Ok(_) => return Ok(candidate),
            // Lost the race to another download (or an orphaned part file): try the next name
            Err(e) if e.kind() == std::io::ErrorKind::AlreadyExists => {}
            Err(e) => return Err(e.into()),
        }
    }
    unreachable!()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn content_range_forms() {
        assert_eq!(
            parse_content_range("bytes 0-0/1234"),
            Some(ContentRange {
                from: Some(0),
                length: Some(1234)
            })
        );
        assert_eq!(
            parse_content_range("bytes */0"),
            Some(ContentRange {
                from: None,
                length: Some(0)
            })
        );
        assert_eq!(
            parse_content_range("bytes 5-9/*"),
            Some(ContentRange {
                from: Some(5),
                length: None
            })
        );
        assert_eq!(parse_content_range("items 0-1/2"), None);
        assert_eq!(parse_content_range("bytes x-y/z"), None);
    }

    #[test]
    fn sanitize_strips_traversal_and_reserved_names() {
        // same result as the C# app: separators vanish, so nothing can traverse
        assert_eq!(sanitize_file_name("..\\..\\evil.exe"), "....evil.exe");
        assert_eq!(
            sanitize_file_name("a/b\\c:d*e?f\"g<h>i|j.txt"),
            "abcdefghij.txt"
        );
        assert_eq!(sanitize_file_name("  name. . "), "name");
        assert_eq!(sanitize_file_name("CON"), "_CON");
        assert_eq!(sanitize_file_name("nul.txt"), "_nul.txt");
        assert_eq!(sanitize_file_name("console.txt"), "console.txt");
        assert_eq!(sanitize_file_name("..."), "download.dat");
        assert_eq!(sanitize_file_name("a\u{0}b\n.txt"), "ab.txt");
        let long = format!("{}.bin", "x".repeat(300));
        assert_eq!(sanitize_file_name(&long).chars().count(), 200);
    }

    #[test]
    fn content_disposition_variants() {
        assert_eq!(
            parse_content_disposition("attachment; filename=\"a b.zip\"").as_deref(),
            Some("a b.zip")
        );
        assert_eq!(
            parse_content_disposition("attachment; filename=plain.zip").as_deref(),
            Some("plain.zip")
        );
        assert_eq!(
            parse_content_disposition("attachment; filename=\"semi;colon.zip\"").as_deref(),
            Some("semi;colon.zip")
        );
        assert_eq!(
            parse_content_disposition("attachment; FILENAME=\"Up.zip\"").as_deref(),
            Some("Up.zip")
        );
        // filename* wins, whatever the order
        assert_eq!(
            parse_content_disposition(
                "attachment; filename=\"fallback.txt\"; filename*=UTF-8''na%C3%AFve%20file.txt"
            )
            .as_deref(),
            Some("naïve file.txt")
        );
        assert_eq!(
            parse_content_disposition("attachment; filename*=UTF-8''x.txt; filename=\"y.txt\"")
                .as_deref(),
            Some("x.txt")
        );
        assert_eq!(parse_content_disposition("inline"), None);
    }

    #[test]
    fn file_name_falls_back_to_url() {
        let h = HeaderMap::new();
        assert_eq!(
            extract_file_name(&h, "https://e.com/dir/My%20File.zip?x=1"),
            "My File.zip"
        );
        assert!(extract_file_name(&h, "https://e.com/").starts_with("download_"));
    }

    #[tokio::test]
    async fn reserve_picks_free_names_and_creates_part_file() {
        let dir = tempfile::tempdir().unwrap();
        let wanted = dir.path().join("f.bin");
        let a = reserve_file_path(&wanted, "u").await.unwrap();
        assert_eq!(a, wanted);
        assert!(dir.path().join("f.bin.wdm-part").exists());
        let b = reserve_file_path(&wanted, "u").await.unwrap();
        assert_eq!(b, dir.path().join("f (1).bin"));
        std::fs::write(dir.path().join("f (2).bin"), b"x").unwrap(); // existing file is never overwritten
        assert_eq!(
            reserve_file_path(&wanted, "u").await.unwrap(),
            dir.path().join("f (3).bin")
        );
    }

    #[tokio::test]
    async fn reserve_resumes_only_the_same_url() {
        let dir = tempfile::tempdir().unwrap();
        let wanted = dir.path().join("f.bin");
        let target = wanted.to_string_lossy().into_owned();
        let meta = DownloadMetadata {
            url: "same".into(),
            target_file_path: target.clone(),
            ..Default::default()
        };
        resume::save_checkpoint(&meta, None).await.unwrap();
        assert_eq!(reserve_file_path(&wanted, "same").await.unwrap(), wanted);
        assert_eq!(
            reserve_file_path(&wanted, "other").await.unwrap(),
            dir.path().join("f (1).bin")
        );
    }
}
