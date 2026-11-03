use std::path::Path;
use tauri::{AppHandle, Manager, State};

use wdm_core::engine::{DownloadRecord, SiteGrabberService};
use wdm_core::ipc::ExternalDownloadRequest;
use wdm_core::models::{AppSettings, GrabMediaItem};

use crate::state::AppState;

#[tauri::command]
pub fn get_downloads(state: State<'_, AppState>) -> Vec<DownloadRecord> {
    state.get_downloads()
}

#[tauri::command]
pub fn get_settings(state: State<'_, AppState>) -> AppSettings {
    state.get_settings()
}

#[tauri::command]
pub fn save_settings(state: State<'_, AppState>, settings: AppSettings) {
    state.save_settings(settings);
}

#[tauri::command]
pub fn add_download(
    state: State<'_, AppState>,
    app_handle: AppHandle,
    request: ExternalDownloadRequest,
) -> Result<DownloadRecord, String> {
    state.add_download(request, &app_handle)
}

#[tauri::command]
pub fn start_download(
    state: State<'_, AppState>,
    app_handle: AppHandle,
    id: String,
) -> Result<(), String> {
    state.start_download(&id, &app_handle)
}

#[tauri::command]
pub fn pause_download(state: State<'_, AppState>, id: String) -> Result<(), String> {
    state.pause_download(&id)
}

#[tauri::command]
pub fn delete_download(
    state: State<'_, AppState>,
    id: String,
    delete_file: bool,
) -> Result<(), String> {
    state.delete_download(&id, delete_file)
}

#[tauri::command]
pub fn start_all(state: State<'_, AppState>, app_handle: AppHandle) -> Result<(), String> {
    state.start_all(&app_handle);
    Ok(())
}

#[tauri::command]
pub fn pause_all(state: State<'_, AppState>) -> Result<(), String> {
    state.pause_all();
    Ok(())
}

#[tauri::command]
pub async fn grab_links(url: String) -> Result<Vec<GrabMediaItem>, String> {
    SiteGrabberService::grab_links(&url, None)
        .await
        .map_err(|e| e.to_string())
}

#[tauri::command]
pub fn add_batch_downloads(
    state: State<'_, AppState>,
    app_handle: AppHandle,
    items: Vec<GrabMediaItem>,
    destination: Option<String>,
) -> Result<usize, String> {
    let mut count = 0;
    for item in items {
        if item.is_selected {
            let req = ExternalDownloadRequest {
                urls: vec![item.url],
                file_name: Some(item.file_name),
                destination_directory: destination.clone(),
                queue_only: true,
                ..Default::default()
            };
            if state.add_download(req, &app_handle).is_ok() {
                count += 1;
            }
        }
    }
    Ok(count)
}

#[tauri::command]
pub fn open_file(path: String) -> Result<(), String> {
    open::that(&path).map_err(|e| e.to_string())
}

#[tauri::command]
pub fn open_folder(path: String) -> Result<(), String> {
    let p = Path::new(&path);
    let target = if p.is_file() {
        p.parent().unwrap_or(p)
    } else {
        p
    };

    #[cfg(windows)]
    {
        let _ = std::process::Command::new("explorer").arg(target).spawn();
        Ok(())
    }
    #[cfg(not(windows))]
    {
        open::that(target).map_err(|e| e.to_string())
    }
}

#[tauri::command]
pub fn minimize_to_tray(app_handle: AppHandle) -> Result<(), String> {
    if let Some(win) = app_handle.get_webview_window("main") {
        let _ = win.hide();
    }
    Ok(())
}
