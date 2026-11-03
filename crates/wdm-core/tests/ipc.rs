use std::time::Duration;
use wdm_core::ipc::{ExternalDownloadRequest, LaunchArguments, NativeHostManifestInspector, WdmPipe};

#[test]
fn launch_arguments_parses_idm_style_switches() {
    let args = [
        "/d", "https://example.com/a.zip",
        "/p", r"C:\Temp",
        "/f", "b.zip",
        "/n",
    ];

    let req = LaunchArguments::parse(args);
    assert_eq!(req.urls, vec!["https://example.com/a.zip"]);
    assert_eq!(req.file_name.as_deref(), Some("b.zip"));
    assert!(req.silent);
    assert!(!req.queue_only);
}

#[test]
fn launch_arguments_accepts_bare_url_and_rejects_other_schemes() {
    let args = [
        "\"https://example.com/x.iso\"",
        "file:///c:/windows/system32/cmd.exe",
        "javascript:alert(1)",
    ];

    let req = LaunchArguments::parse(args);
    assert_eq!(req.urls, vec!["https://example.com/x.iso"]);
}

#[tokio::test]
async fn pipe_delivers_request_to_listener() {
    let pipe_name = format!(r"\\.\pipe\WDM.TestIntegrationPipe.{}", uuid::Uuid::new_v4());
    let (stop_tx, stop_rx) = tokio::sync::watch::channel(false);
    let (received_tx, mut received_rx) = tokio::sync::mpsc::channel(1);

    let pipe_name_clone = pipe_name.clone();
    tokio::spawn(async move {
        WdmPipe::listen(
            move |r| {
                let _ = received_tx.try_send(r);
            },
            stop_rx,
            Some(Duration::from_secs(3)),
            Some(&pipe_name_clone),
        )
        .await;
    });

    tokio::time::sleep(Duration::from_millis(150)).await;

    let sent = ExternalDownloadRequest {
        urls: vec!["https://example.com/f.bin".into()],
        referer: Some("https://example.com/".into()),
        cookies: Some("a=b".into()),
        ..Default::default()
    };

    let ok = WdmPipe::try_send(&sent, Duration::from_secs(3), Some(&pipe_name)).await;
    assert!(ok);

    let got = tokio::time::timeout(Duration::from_secs(3), received_rx.recv())
        .await
        .expect("timeout")
        .expect("received request");

    assert_eq!(got.urls, vec!["https://example.com/f.bin"]);
    assert_eq!(got.cookies.as_deref(), Some("a=b"));

    let _ = stop_tx.send(true);
}

#[test]
fn native_host_manifest_inspects_chromium_and_firefox() {
    let chromium_json = r#"{
        "name": "com.wdm.download",
        "description": "desc",
        "path": "C:\\Program Files\\WDM\\wdm-nativehost.exe",
        "type": "stdio",
        "allowed_origins": [
            "chrome-extension://kdjgcdbahccolkkagbckkchfpcchcfne/",
            "chrome-extension://flnbghmpkajibfdfhbcfhkcjkhlokfgi/"
        ]
    }"#;

    let findings = NativeHostManifestInspector::check_chromium(chromium_json, |_| true);
    assert!(!findings.is_empty());
    assert!(findings.iter().all(|f| f.level != wdm_core::ipc::FindingLevel::Failure));

    let firefox_json = r#"{
        "name": "com.wdm.download",
        "description": "desc",
        "path": "C:\\Program Files\\WDM\\wdm-nativehost.exe",
        "type": "stdio",
        "allowed_extensions": ["wdm@windowsdownloadmanager.com"]
    }"#;

    let ff_findings = NativeHostManifestInspector::check_firefox(firefox_json);
    assert!(!ff_findings.is_empty());
    assert!(ff_findings.iter().all(|f| f.level != wdm_core::ipc::FindingLevel::Failure));
}
