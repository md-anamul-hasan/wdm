use wdm_core::engine::SiteGrabberService;

#[tokio::test]
async fn grab_links_direct_media_url_returns_immediate_item() {
    let results = SiteGrabberService::grab_links("https://example.com/assets/video_clip.mp4", None)
        .await
        .unwrap();

    assert_eq!(results.len(), 1);
    assert_eq!(results[0].file_name, "video_clip.mp4");
    assert_eq!(results[0].category, "Video");
    assert_eq!(results[0].extension, "mp4");
    assert!(results[0].is_selected);
}

#[tokio::test]
async fn grab_links_direct_archive_url_returns_compressed_category() {
    let results = SiteGrabberService::grab_links("https://download.server.net/packages/app-v1.0.zip", None)
        .await
        .unwrap();

    assert_eq!(results.len(), 1);
    assert_eq!(results[0].file_name, "app-v1.0.zip");
    assert_eq!(results[0].category, "Compressed");
    assert_eq!(results[0].extension, "zip");
}

#[tokio::test]
async fn grab_links_empty_url_returns_empty_list() {
    let results = SiteGrabberService::grab_links("", None)
        .await
        .unwrap();

    assert!(results.is_empty());
}
