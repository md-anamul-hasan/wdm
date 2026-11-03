// Prevents additional console window on Windows in release
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

mod commands;
mod state;

use std::sync::atomic::Ordering;
use std::time::Duration;
use chrono::Local;
use tauri::menu::{Menu, MenuItem};
use tauri::tray::{MouseButton, MouseButtonState, TrayIconBuilder, TrayIconEvent};
use tauri::{AppHandle, Emitter, Manager, WindowEvent};

use wdm_core::ipc::{ExternalDownloadRequest, LaunchArguments, WdmPipe};
use crate::state::AppState;

/// One WDM per Windows user, as before: a later launch hands its request (or just "come forward")
/// to the running one and quits. Returns false in that case. The mutex lives until the process ends.
#[cfg(windows)]
fn claim_single_instance() -> bool {
    use windows::core::PCWSTR;
    use windows::Win32::Foundation::{GetLastError, ERROR_ALREADY_EXISTS};
    use windows::Win32::System::Threading::CreateMutexW;

    let user = std::env::var("USERNAME").unwrap_or_default();
    let name: Vec<u16> = format!(r"Local\WDM.SingleInstance.{user}").encode_utf16().chain(Some(0)).collect();
    // SAFETY: `name` is a NUL-terminated UTF-16 string that outlives the call.
    let created = unsafe { CreateMutexW(None, true, PCWSTR(name.as_ptr())) };
    // SAFETY: reads the thread's last error right after the call above.
    let exists = unsafe { GetLastError() } == ERROR_ALREADY_EXISTS;
    match created {
        Ok(handle) if !exists => {
            let _ = handle; // never closed: the mutex lives as long as the process
            true
        }
        Ok(_) => false,
        Err(_) => true, // cannot tell: better a second window than no window
    }
}

#[cfg(not(windows))]
fn claim_single_instance() -> bool {
    true
}

/// A request from the browser, the command line or a second launch.
fn handle_external(h: &AppHandle, mut req: ExternalDownloadRequest) {
    if let Some(win) = h.get_webview_window("main") {
        let _ = win.show();
        let _ = win.unminimize();
        let _ = win.set_focus();
    }
    if req.activate_only || req.is_empty() {
        return;
    }
    let Some(state) = h.try_state::<AppState>() else { return };

    // Several addresses cannot share one "add download" dialog: list them (queued) or start them
    let show_dialog = state.get_settings().show_start_download_dialog;
    let many = req.urls.len() > 1;
    if many && !req.silent && show_dialog {
        req.queue_only = true;
    }

    if req.silent || req.queue_only || !show_dialog {
        let urls = std::mem::take(&mut req.urls);
        for url in urls {
            let one = ExternalDownloadRequest { urls: vec![url], file_name: if many { None } else { req.file_name.clone() }, sha256: if many { None } else { req.sha256.clone() }, ..req.clone() };
            let _ = state.add_download(one, h);
        }
        let _ = h.emit("downloads-changed", ());
    } else {
        // One address: let the user confirm in the dialog first
        let _ = h.emit("external-download", &req);
    }
}

fn main() {
    let request = LaunchArguments::parse(std::env::args().skip(1));
    if !claim_single_instance() {
        let hand_over = if request.is_empty() { ExternalDownloadRequest { activate_only: true, ..Default::default() } } else { request };
        tauri::async_runtime::block_on(WdmPipe::try_send(&hand_over, Duration::from_secs(3), None));
        return;
    }

    let app_state = AppState::new();

    tauri::Builder::default()
        .plugin(tauri_plugin_shell::init())
        .plugin(tauri_plugin_dialog::init())
        .manage(app_state)
        .setup(|app| {
            let handle = app.handle().clone();

            // Setup System Tray
            setup_tray(&handle)?;

            // Background Named Pipe Listener (WDM.Ipc.<User>)
            let pipe_handle = handle.clone();
            let (stop_tx, stop_rx) = tokio::sync::watch::channel(false);
            if let Some(state) = handle.try_state::<AppState>() {
                *state.pipe_stop_tx.lock().unwrap() = Some(stop_tx);
            }
            if !request.is_empty() {
                handle_external(&handle, request);
            }
            tauri::async_runtime::spawn(async move {
                WdmPipe::listen(
                    move |req| handle_external(&pipe_handle, req),
                    stop_rx,
                    Some(Duration::from_secs(5)),
                    None,
                )
                .await;
            });

            // Background Scheduler Loop
            let sched_handle = handle.clone();
            tauri::async_runtime::spawn(async move {
                let mut previous_time = Local::now();
                loop {
                    tokio::time::sleep(Duration::from_secs(10)).await;
                    let now = Local::now();
                    if let Some(state) = sched_handle.try_state::<AppState>() {
                        state.check_scheduler(previous_time, now, &sched_handle);
                        state.retry_failed(&sched_handle);
                    }
                    previous_time = now;
                }
            });

            Ok(())
        })
        .on_window_event(|window, event| {
            if let WindowEvent::CloseRequested { api, .. } = event {
                // Minimize to tray instead of closing
                api.prevent_close();
                let _ = window.hide();

                if let Some(state) = window.app_handle().try_state::<AppState>() {
                    if state.is_first_hide.swap(false, Ordering::SeqCst) {
                        println!("[WDM] Minimized to notification tray. Downloads continue in background.");
                    }
                }
            }
        })
        .invoke_handler(tauri::generate_handler![
            commands::get_downloads,
            commands::get_settings,
            commands::save_settings,
            commands::add_download,
            commands::start_download,
            commands::pause_download,
            commands::delete_download,
            commands::start_all,
            commands::pause_all,
            commands::grab_links,
            commands::add_batch_downloads,
            commands::open_file,
            commands::open_folder,
            commands::minimize_to_tray,
        ])
        .run(tauri::generate_context!())
        .expect("error while running WDM desktop application");
}

fn setup_tray(app: &AppHandle) -> Result<(), Box<dyn std::error::Error>> {
    let open_i = MenuItem::with_id(app, "open", "Open WDM", true, None::<&str>)?;
    let pause_all_i = MenuItem::with_id(app, "pause_all", "Pause All", true, None::<&str>)?;
    let resume_all_i = MenuItem::with_id(app, "resume_all", "Resume All", true, None::<&str>)?;
    let exit_i = MenuItem::with_id(app, "exit", "Exit", true, None::<&str>)?;

    let menu = Menu::with_items(app, &[
        &open_i,
        &pause_all_i,
        &resume_all_i,
        &exit_i,
    ])?;

    let _tray = TrayIconBuilder::new()
        .menu(&menu)
        .tooltip("Windows Download Manager")
        .show_menu_on_left_click(false)
        .on_menu_event(|app, event| match event.id.as_ref() {
            "open" => {
                if let Some(win) = app.get_webview_window("main") {
                    let _ = win.show();
                    let _ = win.set_focus();
                }
            }
            "pause_all" => {
                if let Some(state) = app.try_state::<AppState>() {
                    state.pause_all(app);
                }
            }
            "resume_all" => {
                if let Some(state) = app.try_state::<AppState>() {
                    state.start_all(app);
                }
            }
            "exit" => {
                if let Some(state) = app.try_state::<AppState>() {
                    state.pause_all(app);
                }
                app.exit(0);
            }
            _ => {}
        })
        .on_tray_icon_event(|tray, event| {
            if let TrayIconEvent::Click {
                button: MouseButton::Left,
                button_state: MouseButtonState::Up,
                ..
            } = event
            {
                let app = tray.app_handle();
                if let Some(win) = app.get_webview_window("main") {
                    let _ = win.show();
                    let _ = win.set_focus();
                }
            }
        })
        .build(app)?;

    Ok(())
}
