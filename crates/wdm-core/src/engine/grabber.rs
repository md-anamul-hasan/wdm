use std::collections::HashSet;
use std::path::Path;
use std::sync::OnceLock;
use std::time::Duration;
use regex::Regex;
use url::Url;

use crate::engine::http;
use crate::error::{Error, Result};
use crate::models::{DownloadRequestOptions, FileCategories, GrabMediaItem};

const MAX_PAGE_BYTES: usize = 8 * 1024 * 1024;
const REQUEST_TIMEOUT: Duration = Duration::from_secs(20);

static MEDIA_PATTERN: OnceLock<Regex> = OnceLock::new();

fn media_pattern() -> &'static Regex {
    MEDIA_PATTERN.get_or_init(|| {
        Regex::new(
            r#"(?i)(?:href|src|data-src|data-url)\s*=\s*["'](?P<url>[^"'#\s>]+)["']|["'](?P<direct>https?://[^"'\s\\]+\.(?:mp4|m3u8|webm|mkv|mp3|wav|flac|aac|ogg|zip|rar|7z|tar|gz|iso|pdf|docx?|xlsx?|pptx?|exe|msi|apk|png|jpe?g|webp)[^"'\s\\]*)["']"#,
        )
        .expect("valid regex")
    })
}

pub struct SiteGrabberService;

impl SiteGrabberService {
    pub async fn grab_links(page_url: &str, user_agent: Option<&str>) -> Result<Vec<GrabMediaItem>> {
        let mut results = Vec::new();
        let trimmed = page_url.trim();
        if trimmed.is_empty() {
            return Ok(results);
        }

        let base_url = match Url::parse(trimmed) {
            Ok(u) if u.scheme() == "http" || u.scheme() == "https" => u,
            _ => {
                if trimmed.contains("://") {
                    return Ok(results);
                }
                match Url::parse(&format!("https://{trimmed}")) {
                    Ok(u) if u.scheme() == "http" || u.scheme() == "https" => u,
                    _ => return Ok(results),
                }
            }
        };

        // Direct file URL check
        let path_str = base_url.path();
        let direct_ext = Path::new(path_str).extension().and_then(|e| e.to_str()).unwrap_or("");
        if FileCategories::is_known_extension(Some(direct_ext)) {
            results.push(Self::create_item(&base_url, direct_ext, results.len()));
            return Ok(results);
        }

        let client = http::create_client(Some(REQUEST_TIMEOUT))?;
        let options = DownloadRequestOptions {
            user_agent: user_agent.map(|s| s.to_string()),
            ..Default::default()
        };

        let ct = tokio_util::sync::CancellationToken::new();
        let response = tokio::time::timeout(
            REQUEST_TIMEOUT,
            http::send(&client, reqwest::Method::GET, base_url.as_str(), Some(&options), None, &ct),
        )
        .await
        .map_err(|_| Error::Http(format!("The page did not respond within {} seconds", REQUEST_TIMEOUT.as_secs())))??;

        let status = response.status();
        if !status.is_success() {
            return Err(Error::Http(format!("HTTP error: {status}")));
        }

        let content_type = response
            .headers()
            .get(reqwest::header::CONTENT_TYPE)
            .and_then(|v| v.to_str().ok())
            .unwrap_or("")
            .to_ascii_lowercase();

        if !content_type.contains("html") {
            // Not HTML: the URL itself is the download
            results.push(Self::create_item(&base_url, direct_ext, results.len()));
            return Ok(results);
        }

        let bytes = response.bytes().await.map_err(Error::from)?;
        let slice = if bytes.len() > MAX_PAGE_BYTES {
            &bytes[..MAX_PAGE_BYTES]
        } else {
            &bytes[..]
        };
        let html = String::from_utf8_lossy(slice);

        let pattern = media_pattern();
        let mut seen_urls = HashSet::new();

        for cap in pattern.captures_iter(&html) {
            let raw = cap.name("url").or_else(|| cap.name("direct")).map(|m| m.as_str()).unwrap_or("");
            if raw.is_empty() {
                continue;
            }

            let resolved = match base_url.join(raw) {
                Ok(u) => u,
                Err(_) => continue,
            };

            if resolved.scheme() != "http" && resolved.scheme() != "https" {
                continue;
            }

            let resolved_str = resolved.to_string();
            if !seen_urls.insert(resolved_str) {
                continue;
            }

            let ext = Path::new(resolved.path())
                .extension()
                .and_then(|e| e.to_str())
                .unwrap_or("");

            if FileCategories::is_known_extension(Some(ext)) {
                results.push(Self::create_item(&resolved, ext, results.len()));
            }
        }

        Ok(results)
    }

    fn create_item(url: &Url, extension: &str, index: usize) -> GrabMediaItem {
        let path = url.path();
        let file_name_raw = Path::new(path).file_name().and_then(|n| n.to_str()).unwrap_or("");
        let unescaped = percent_encoding::percent_decode_str(file_name_raw).decode_utf8_lossy().into_owned();
        let ext_with_dot = if extension.is_empty() || extension.starts_with('.') {
            extension.to_string()
        } else {
            format!(".{extension}")
        };

        let file_name = if unescaped.trim().is_empty() {
            format!("download_{}{}", index + 1, ext_with_dot)
        } else {
            unescaped
        };

        let cat = FileCategories::from_extension(Some(extension));
        let category_str = FileCategories::display_name(cat).to_string();

        GrabMediaItem::new(
            url.as_str(),
            file_name,
            category_str,
            extension.to_ascii_lowercase(),
        )
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[tokio::test]
    async fn test_direct_file_url() {
        let items = SiteGrabberService::grab_links("https://example.com/test.zip", None)
            .await
            .unwrap();
        assert_eq!(items.len(), 1);
        assert_eq!(items[0].file_name, "test.zip");
        assert_eq!(items[0].category, "Compressed");
    }
}
