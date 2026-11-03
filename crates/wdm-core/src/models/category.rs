use serde::{Deserialize, Serialize};
use std::collections::HashMap;
use std::path::Path;
use std::sync::OnceLock;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Serialize, Deserialize)]
pub enum FileCategory {
    General,
    Video,
    Music,
    Compressed,
    Documents,
    Programs,
    Images,
}

impl FileCategory {
    pub fn display_name(&self) -> &'static str {
        match self {
            FileCategory::General => "Other",
            FileCategory::Video => "Video",
            FileCategory::Music => "Music",
            FileCategory::Compressed => "Compressed",
            FileCategory::Documents => "Documents",
            FileCategory::Programs => "Programs",
            FileCategory::Images => "Images",
        }
    }
}

pub struct FileCategories;

static MAP: OnceLock<HashMap<&'static str, FileCategory>> = OnceLock::new();

impl FileCategories {
    fn map() -> &'static HashMap<&'static str, FileCategory> {
        MAP.get_or_init(|| {
            let mut map = HashMap::new();
            let mut add = |category: FileCategory, exts: &'static str| {
                for ext in exts.split_whitespace() {
                    map.insert(ext, category);
                }
            };

            add(FileCategory::Video, "mp4 mkv avi webm mov m4v wmv flv mpg mpeg 3gp ogv m3u8 ts");
            add(FileCategory::Music, "mp3 wav flac aac ogg m4a wma");
            add(FileCategory::Compressed, "zip rar 7z tar gz bz2 xz iso");
            add(FileCategory::Documents, "pdf doc docx txt rtf csv xls xlsx ppt pptx epub");
            add(FileCategory::Programs, "exe msi apk bat cmd pkg");
            add(FileCategory::Images, "png jpg jpeg webp gif svg");

            map
        })
    }

    pub fn from_extension(extension: Option<&str>) -> FileCategory {
        let Some(mut ext) = extension else {
            return FileCategory::General;
        };
        ext = ext.trim();
        if ext.is_empty() {
            return FileCategory::General;
        }
        if let Some(stripped) = ext.strip_prefix('.') {
            ext = stripped;
        }
        let lower = ext.to_ascii_lowercase();
        Self::map().get(lower.as_str()).copied().unwrap_or(FileCategory::General)
    }

    pub fn from_file_name(file_name: Option<&str>) -> FileCategory {
        let Some(name) = file_name else {
            return FileCategory::General;
        };
        let ext = Path::new(name).extension().and_then(|e| e.to_str());
        Self::from_extension(ext)
    }

    pub fn is_known_extension(extension: Option<&str>) -> bool {
        Self::from_extension(extension) != FileCategory::General
    }

    pub fn display_name(category: FileCategory) -> &'static str {
        category.display_name()
    }
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub struct GrabMediaItem {
    pub url: String,
    pub file_name: String,
    pub category: String,
    pub extension: String,
    #[serde(default = "default_true")]
    pub is_selected: bool,
}

fn default_true() -> bool {
    true
}

impl GrabMediaItem {
    pub fn new(url: impl Into<String>, file_name: impl Into<String>, category: impl Into<String>, extension: impl Into<String>) -> Self {
        Self {
            url: url.into(),
            file_name: file_name.into(),
            category: category.into(),
            extension: extension.into(),
            is_selected: true,
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_category_lookup() {
        assert_eq!(FileCategories::from_extension(Some("mp4")), FileCategory::Video);
        assert_eq!(FileCategories::from_extension(Some(".MP4")), FileCategory::Video);
        assert_eq!(FileCategories::from_extension(Some(".zip")), FileCategory::Compressed);
        assert_eq!(FileCategories::from_extension(Some("exe")), FileCategory::Programs);
        assert_eq!(FileCategories::from_extension(Some("pdf")), FileCategory::Documents);
        assert_eq!(FileCategories::from_extension(Some("unknown")), FileCategory::General);
        assert_eq!(FileCategories::from_extension(None), FileCategory::General);
        assert_eq!(FileCategories::from_file_name(Some("video.mkv")), FileCategory::Video);
        assert!(FileCategories::is_known_extension(Some(".png")));
        assert!(!FileCategories::is_known_extension(Some(".bin")));
    }
}
