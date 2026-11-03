use std::io::{self, Read, Write};
use std::path::{Path, PathBuf};
use std::time::Duration;
use serde_json::{json, Value};
use url::Url;

use wdm_core::ipc::{
    ExternalDownloadRequest, FindingLevel, NativeHostManifest,
    NativeHostManifestInspector, WdmPipe, CHROMIUM_REGISTRY_ROOTS, FIREFOX_EXTENSION_ID,
    FIREFOX_REGISTRY_ROOT, HOST_NAME,
};
use wdm_core::models::AppSettings;

const VERSION: &str = env!("CARGO_PKG_VERSION");
const MAX_INCOMING_BYTES: usize = 10 * 1024 * 1024;

#[tokio::main]
async fn main() {
    let args: Vec<String> = std::env::args().skip(1).collect();

    if !args.is_empty() {
        let first = args[0].to_ascii_lowercase();
        match first.as_str() {
            "--register" | "-r" => {
                let code = register(&args[1..]);
                std::process::exit(code);
            }
            "--unregister" | "-u" => {
                let code = unregister();
                std::process::exit(code);
            }
            "--diagnose" | "-d" => {
                let code = diagnose().await;
                std::process::exit(code);
            }
            _ => {}
        }
    }

    run_host_loop().await;
}

/// Browser stdio loop.
/// Protocol: 4 bytes native-endian length prefix followed by UTF-8 JSON.
/// Output MUST BE protocol-only: NEVER print text directly to stdout.
async fn run_host_loop() {
    let mut stdin = io::stdin().lock();
    let mut stdout = io::stdout().lock();

    let mut length_buf = [0u8; 4];

    loop {
        if stdin.read_exact(&mut length_buf).is_err() {
            break; // Browser closed connection
        }

        let length = u32::from_ne_bytes(length_buf) as usize;
        if length == 0 || length > MAX_INCOMING_BYTES {
            break;
        }

        let mut message_buf = vec![0u8; length];
        if stdin.read_exact(&mut message_buf).is_err() {
            break;
        }

        let response_str = handle_message(&message_buf).await;
        let response_bytes = response_str.as_bytes();
        let resp_len = (response_bytes.len() as u32).to_ne_bytes();

        if stdout.write_all(&resp_len).is_err() || stdout.write_all(response_bytes).is_err() || stdout.flush().is_err() {
            break;
        }
    }
}

async fn handle_message(bytes: &[u8]) -> String {
    let val: Value = match serde_json::from_slice(bytes) {
        Ok(v) => v,
        Err(e) => return reply(json!({ "status": "error", "message": e.to_string() })),
    };

    let action = val.get("action").and_then(|v| v.as_str()).unwrap_or("").to_ascii_lowercase();

    match action.as_str() {
        "ping" => reply(json!({
            "status": "ok",
            "message": "pong",
            "version": VERSION
        })),

        "download" => {
            let url = val.get("url").and_then(|v| v.as_str());
            let request = build_request(&val, url);
            if request.is_empty() {
                return reply(json!({
                    "status": "error",
                    "message": "Missing or invalid http(s) URL"
                }));
            }
            deliver_request(request).await
        }

        "batch" => {
            let urls_val = val.get("urls").and_then(|v| v.as_array());
            let mut urls = Vec::new();
            if let Some(arr) = urls_val {
                for u in arr {
                    if let Some(s) = u.as_str() {
                        let trimmed = s.trim();
                        if ExternalDownloadRequest::is_http_url(trimmed) && !urls.contains(&trimmed.to_string()) {
                            urls.push(trimmed.to_string());
                        }
                    }
                }
            }

            let mut request = build_request(&val, None);
            request.urls = urls;

            if request.is_empty() {
                return reply(json!({
                    "status": "error",
                    "message": "No valid http(s) URLs"
                }));
            }

            if request.cookies.is_some() {
                if let Ok(u) = Url::parse(&request.urls[0]) {
                    request.cookie_host = u.host_str().map(|h| h.to_string());
                }
            }

            deliver_request(request).await
        }

        other => reply(json!({
            "status": "unknown_action",
            "action": other
        })),
    }
}

fn build_request(val: &Value, url_opt: Option<&str>) -> ExternalDownloadRequest {
    let null_if_empty = |k: &str| -> Option<String> {
        val.get(k)
            .and_then(|v| v.as_str())
            .map(|s| s.trim().to_string())
            .filter(|s| !s.is_empty())
    };

    let mut req = ExternalDownloadRequest {
        referer: null_if_empty("referer"),
        cookies: null_if_empty("cookies"),
        user_agent: null_if_empty("userAgent"),
        file_name: null_if_empty("fileName"),
        ..Default::default()
    };

    if let Some(url) = url_opt {
        let trimmed = url.trim();
        if ExternalDownloadRequest::is_http_url(trimmed) {
            req.urls.push(trimmed.to_string());
            if req.cookies.is_some() {
                if let Ok(u) = Url::parse(trimmed) {
                    req.cookie_host = u.host_str().map(|h| h.to_string());
                }
            }
        }
    }

    req
}

async fn deliver_request(request: ExternalDownloadRequest) -> String {
    if WdmPipe::try_send(&request, Duration::from_millis(1500), None).await {
        return reply(json!({ "status": "ok", "message": "Download sent to WDM" }));
    }

    // WDM isn't running: start it, then wait for its pipe to come up
    let Some(ui_path) = find_wdm_ui() else {
        return reply(json!({
            "status": "error",
            "message": "WDM application (wdm-desktop.exe / Wdm.Ui.exe) was not found next to the native host"
        }));
    };

    let mut cmd = std::process::Command::new(&ui_path);
    if let Some(parent) = ui_path.parent() {
        cmd.current_dir(parent);
    }
    if let Err(e) = cmd.spawn() {
        return reply(json!({
            "status": "error",
            "message": format!("Failed to start WDM: {e}")
        }));
    }

    for _ in 0..40 {
        tokio::time::sleep(Duration::from_millis(250)).await;
        if WdmPipe::try_send(&request, Duration::from_millis(500), None).await {
            return reply(json!({ "status": "ok", "message": "WDM started and download sent" }));
        }
    }

    reply(json!({
        "status": "error",
        "message": "WDM did not respond after being started"
    }))
}

fn find_wdm_ui() -> Option<PathBuf> {
    let current_exe = std::env::current_exe().ok()?;
    let base_dir = current_exe.parent()?;
    let candidates = [
        base_dir.join("wdm-desktop.exe"),
        base_dir.join("WDM.exe"),
        base_dir.join("Wdm.Ui.exe"),
        base_dir.join("../../target/debug/wdm-desktop.exe"),
        base_dir.join("../../target/release/wdm-desktop.exe"),
        base_dir.join("../../../target/debug/wdm-desktop.exe"),
        base_dir.join("../../../target/release/wdm-desktop.exe"),
    ];

    for c in &candidates {
        if c.is_file() {
            return Some(c.clone());
        }
    }
    None
}

fn reply(val: Value) -> String {
    serde_json::to_string(&val).unwrap_or_else(|_| "{}".to_string())
}

fn manifest_directory() -> PathBuf {
    AppSettings::data_directory().join("NativeMessaging")
}

fn register(options: &[String]) -> i32 {
    let mut extension_ids = Vec::new();
    let mut i = 0;
    while i < options.len() {
        if options[i] == "--extension-id" && i + 1 < options.len() {
            i += 1;
            let id = options[i].trim().to_ascii_lowercase();
            if !NativeHostManifest::is_chrome_extension_id(&id) {
                eprintln!("[WDM] --extension-id needs a 32-character Chrome extension ID (letters a-p).");
                return 2;
            }
            extension_ids.push(id);
        }
        i += 1;
    }

    println!("[WDM] Registering native messaging host for Chrome, Edge, Brave and Firefox...");

    let exe_path = std::env::current_exe().unwrap_or_else(|_| PathBuf::from("wdm-nativehost.exe"));
    let manifest_dir = manifest_directory();
    let _ = std::fs::create_dir_all(&manifest_dir);

    let chromium_manifest = manifest_dir.join(format!("{HOST_NAME}.json"));
    let firefox_manifest = manifest_dir.join(format!("{HOST_NAME}.mozilla.json"));

    let chromium_json = json!({
        "name": HOST_NAME,
        "description": "Windows Download Manager browser integration",
        "path": exe_path.to_string_lossy(),
        "type": "stdio",
        "allowed_origins": NativeHostManifest::allowed_origins(&extension_ids)
    });

    let firefox_json = json!({
        "name": HOST_NAME,
        "description": "Windows Download Manager browser integration",
        "path": exe_path.to_string_lossy(),
        "type": "stdio",
        "allowed_extensions": [FIREFOX_EXTENSION_ID]
    });

    if let Err(e) = std::fs::write(&chromium_manifest, serde_json::to_string_pretty(&chromium_json).unwrap()) {
        eprintln!("[WDM] Failed to write Chromium manifest: {e}");
        return 1;
    }

    if let Err(e) = std::fs::write(&firefox_manifest, serde_json::to_string_pretty(&firefox_json).unwrap()) {
        eprintln!("[WDM] Failed to write Firefox manifest: {e}");
        return 1;
    }

    #[cfg(windows)]
    {
        for root in CHROMIUM_REGISTRY_ROOTS {
            let subkey = format!(r"{root}\{HOST_NAME}");
            if let Err(e) = set_registry_default_value(&subkey, &chromium_manifest.to_string_lossy()) {
                eprintln!("[WDM] Registry write failed for {subkey}: {e}");
                return 1;
            }
        }

        let ff_subkey = format!(r"{FIREFOX_REGISTRY_ROOT}\{HOST_NAME}");
        if let Err(e) = set_registry_default_value(&ff_subkey, &firefox_manifest.to_string_lossy()) {
            eprintln!("[WDM] Registry write failed for {ff_subkey}: {e}");
            return 1;
        }
    }

    println!("[WDM] Registered '{HOST_NAME}'.");
    println!("      Host:    {}", exe_path.display());
    println!("      Chrome:  {}", chromium_manifest.display());
    println!("      Firefox: {}", firefox_manifest.display());
    0
}

fn unregister() -> i32 {
    #[cfg(windows)]
    {
        for root in CHROMIUM_REGISTRY_ROOTS {
            let subkey = format!(r"{root}\{HOST_NAME}");
            let _ = delete_registry_key(&subkey);
        }
        let ff_subkey = format!(r"{FIREFOX_REGISTRY_ROOT}\{HOST_NAME}");
        let _ = delete_registry_key(&ff_subkey);
    }

    let manifest_dir = manifest_directory();
    let _ = std::fs::remove_dir_all(manifest_dir);

    println!("[WDM] Unregistered '{HOST_NAME}'.");
    0
}

async fn diagnose() -> i32 {
    let mut failures = 0;
    let mut warnings = 0;

    let exe_path = std::env::current_exe().unwrap_or_else(|_| PathBuf::from("wdm-nativehost.exe"));
    println!("WDM native host {VERSION}  ({})", exe_path.display());

    println!("\nBrowsers");
    let browsers: [(&str, &str, bool); 5] = [
        ("Chrome", CHROMIUM_REGISTRY_ROOTS[0], false),
        ("Edge", CHROMIUM_REGISTRY_ROOTS[1], false),
        ("Brave", CHROMIUM_REGISTRY_ROOTS[2], false),
        ("Chromium", CHROMIUM_REGISTRY_ROOTS[3], false),
        ("Firefox", FIREFOX_REGISTRY_ROOT, true),
    ];

    for (name, root, is_firefox) in browsers {
        println!(" {name}");
        let subkey = format!(r"{root}\{HOST_NAME}");
        #[cfg(windows)]
        let manifest_path_opt = get_registry_default_value(&subkey);
        #[cfg(not(windows))]
        let manifest_path_opt: Option<String> = None;

        let Some(manifest_path) = manifest_path_opt else {
            let level = if matches!(name, "Chrome" | "Edge" | "Brave") {
                warnings += 1;
                "[WARN]"
            } else {
                "[ OK ]"
            };
            println!("  {level} Not registered (fine if you do not use {name}). To register: wdm-nativehost --register");
            continue;
        };

        if !Path::new(&manifest_path).is_file() {
            failures += 1;
            println!("  [FAIL] Registry points to a manifest that does not exist: {manifest_path}. Re-run: wdm-nativehost --register");
            continue;
        }

        let Ok(json) = std::fs::read_to_string(&manifest_path) else {
            failures += 1;
            println!("  [FAIL] Cannot read manifest file: {manifest_path}");
            continue;
        };

        let findings = if is_firefox {
            NativeHostManifestInspector::check_firefox(&json)
        } else {
            NativeHostManifestInspector::check_chromium(&json, |p| Path::new(p).is_file())
        };

        for f in findings {
            let tag = match f.level {
                FindingLevel::Ok => "[ OK ]",
                FindingLevel::Warning => {
                    warnings += 1;
                    "[WARN]"
                }
                FindingLevel::Failure => {
                    failures += 1;
                    "[FAIL]"
                }
            };
            println!("  {tag} {}", f.message);
        }
    }

    println!("\nWDM application");
    let ui = find_wdm_ui();
    if let Some(p) = ui {
        println!("  [ OK ] Found: {}", p.display());
    } else {
        failures += 1;
        println!("  [FAIL] wdm-desktop.exe / Wdm.Ui.exe was not found next to the host; the browser cannot start WDM.");
    }

    if WdmPipe::is_listening(Duration::from_secs(1), None).await {
        println!("  [ OK ] WDM is running and accepting downloads.");
    } else {
        warnings += 1;
        println!("  [WARN] WDM is not running right now (it is started automatically when the browser sends a download).");
    }

    println!("\n{failures} problem(s), {warnings} warning(s).");
    if failures == 0 { 0 } else { 1 }
}

#[cfg(windows)]
fn set_registry_default_value(subkey: &str, value: &str) -> io::Result<()> {
    use windows::core::{HSTRING, PCWSTR};
    use windows::Win32::System::Registry::{
        RegCloseKey, RegCreateKeyExW, HKEY, HKEY_CURRENT_USER, KEY_WRITE,
        REG_OPTION_NON_VOLATILE, REG_SZ,
    };

    let subkey_h = HSTRING::from(subkey);
    let mut hkey = HKEY::default();
    unsafe {
        let res = RegCreateKeyExW(
            HKEY_CURRENT_USER,
            PCWSTR(subkey_h.as_ptr()),
            0,
            None,
            REG_OPTION_NON_VOLATILE,
            KEY_WRITE,
            None,
            &mut hkey,
            None,
        );
        if res.is_err() {
            return Err(io::Error::other(format!("RegCreateKeyExW failed: {res:?}")));
        }

        let val_h = HSTRING::from(value);
        let bytes = std::slice::from_raw_parts(val_h.as_ptr() as *const u8, (val_h.len() + 1) * 2);
        let set_res = windows::Win32::System::Registry::RegSetValueExW(
            hkey,
            PCWSTR::null(),
            0,
            REG_SZ,
            Some(bytes),
        );
        let _ = RegCloseKey(hkey);
        if set_res.is_err() {
            return Err(io::Error::other(format!("RegSetValueExW failed: {set_res:?}")));
        }
    }
    Ok(())
}

#[cfg(windows)]
fn get_registry_default_value(subkey: &str) -> Option<String> {
    use windows::core::{HSTRING, PCWSTR};
    use windows::Win32::System::Registry::{
        RegCloseKey, RegOpenKeyExW, RegQueryValueExW, HKEY, HKEY_CURRENT_USER, KEY_READ,
    };

    let subkey_h = HSTRING::from(subkey);
    let mut hkey = HKEY::default();
    unsafe {
        if RegOpenKeyExW(HKEY_CURRENT_USER, PCWSTR(subkey_h.as_ptr()), 0, KEY_READ, &mut hkey).is_err() {
            return None;
        }

        let mut buf = [0u16; 1024];
        let mut size = (buf.len() * 2) as u32;
        let q_res = RegQueryValueExW(
            hkey,
            PCWSTR::null(),
            None,
            None,
            Some(buf.as_mut_ptr() as *mut u8),
            Some(&mut size),
        );
        let _ = RegCloseKey(hkey);

        if q_res.is_ok() && size > 0 {
            let len = (size as usize / 2).saturating_sub(1);
            Some(String::from_utf16_lossy(&buf[..len]))
        } else {
            None
        }
    }
}

#[cfg(windows)]
fn delete_registry_key(subkey: &str) -> io::Result<()> {
    use windows::core::{HSTRING, PCWSTR};
    use windows::Win32::System::Registry::{RegDeleteKeyW, HKEY_CURRENT_USER};

    let subkey_h = HSTRING::from(subkey);
    unsafe {
        let res = RegDeleteKeyW(HKEY_CURRENT_USER, PCWSTR(subkey_h.as_ptr()));
        if res.is_err() {
            return Err(io::Error::other(format!("RegDeleteKeyW failed: {res:?}")));
        }
    }
    Ok(())
}
