use std::path::PathBuf;

use crate::ipc::ExternalDownloadRequest;

pub struct LaunchArguments;

impl LaunchArguments {
    pub fn parse<I, S>(args: I) -> ExternalDownloadRequest
    where
        I: IntoIterator<Item = S>,
        S: AsRef<str>,
    {
        let mut request = ExternalDownloadRequest::default();
        let list: Vec<String> = args.into_iter().map(|s| s.as_ref().to_string()).collect();

        let mut i = 0;
        while i < list.len() {
            let raw = list[i].trim_matches('"').trim();
            let key = raw.to_ascii_lowercase();
            let has_value = i + 1 < list.len();

            if (key == "/d" || key == "-d" || key == "--url") && has_value {
                i += 1;
                Self::add_url(&mut request, &list[i]);
            } else if (key == "/p" || key == "-p" || key == "--path") && has_value {
                i += 1;
                request.destination_directory =
                    Some(Self::resolve_directory(list[i].trim_matches('"')));
            } else if (key == "/f" || key == "-f" || key == "--file") && has_value {
                i += 1;
                request.file_name = Some(list[i].trim_matches('"').to_string());
            } else if (key == "/sha256" || key == "-sha256" || key == "--sha256") && has_value {
                i += 1;
                request.sha256 = Some(list[i].trim_matches('"').to_string());
            } else if key == "/n" || key == "-n" || key == "--silent" {
                request.silent = true;
            } else if key == "/a" || key == "-a" || key == "--queue" {
                request.queue_only = true;
            } else {
                Self::add_url(&mut request, raw);
            }
            i += 1;
        }

        request
    }

    fn resolve_directory(path: &str) -> String {
        if let Ok(full) = std::fs::canonicalize(path) {
            full.to_string_lossy().into_owned()
        } else {
            PathBuf::from(path).to_string_lossy().into_owned()
        }
    }

    fn add_url(request: &mut ExternalDownloadRequest, value: &str) {
        let url = value.trim_matches('"').trim();
        if ExternalDownloadRequest::is_http_url(url) && !request.urls.contains(&url.to_string()) {
            request.urls.push(url.to_string());
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_parse_arguments() {
        let args = vec![
            "/d",
            "https://example.com/test.bin",
            "/p",
            "C:\\temp",
            "/f",
            "custom.bin",
            "/sha256",
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            "/n",
        ];

        let req = LaunchArguments::parse(args);
        assert_eq!(req.urls, vec!["https://example.com/test.bin"]);
        assert_eq!(req.file_name, Some("custom.bin".into()));
        assert_eq!(
            req.sha256,
            Some("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855".into())
        );
        assert!(req.silent);
        assert!(!req.queue_only);
    }

    #[test]
    fn test_parse_bare_url() {
        let args = vec!["https://example.com/bare.zip", "/a"];
        let req = LaunchArguments::parse(args);
        assert_eq!(req.urls, vec!["https://example.com/bare.zip"]);
        assert!(req.queue_only);
    }
}
