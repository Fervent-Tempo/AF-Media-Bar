//! 设置存储：加载、恢复、隔离、去抖原子保存与「我的默认设置」。
//!
//! 对齐 C# `SettingsPersistenceService`（语义逐条移植）：
//! - **只读当前 schema 编号**（`CURRENT_SCHEMA_VERSION`），编号不同的文件一律隔离改名
//!   （`*.unsupported-<时间戳>` / `*.invalid-<时间戳>`）后回到内置默认，不做"尽力读取"；
//! - 主文件失败时尝试 `settings.json.bak` 恢复；
//! - 原子保存：`settings.<uuid>.tmp`（独占创建 + 全量落盘）→ 旧文件复制为 `.bak` → 改名覆盖；
//! - 变更后 300 ms 去抖后台保存；[`SettingsStore::flush`] 同步落盘；
//! - `user-defaults.json` 走同一条读取路径，编号不同的快照同样隔离；
//! - 读取容忍注释与尾逗号（对齐 C# `ReadCommentHandling.Skip` / `AllowTrailingCommas`）。
//!
//! 已知偏差（如实记录）：
//! - 隔离文件名时间戳为 UTC（C# 用本地时间），仅影响文件名可读性；
//! - `AllowNamedFloatingPointLiterals`（NaN/Infinity 字面量）不支持：含此类字面量的
//!   手改文件会被判 invalid 隔离，而 C# 能读入并归一化。

use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::{Arc, Condvar, Mutex};
use std::time::{Duration, Instant};

use afmb_core::log::AppLog;
use serde::{Deserialize, Serialize};

use crate::model::AppSettings;

/// 当前设置文件 schema。**只在发布批次里变更**：编号不同的文件不读取、不迁移，隔离后回到内置默认。
pub const CURRENT_SCHEMA_VERSION: i64 = 2;

/// 变更去抖间隔。
pub const DEFAULT_DEBOUNCE: Duration = Duration::from_millis(300);

/// 信封：`{ schemaVersion, settings }`（camelCase，与 C# 序列化一致）。
#[derive(Debug, Serialize, Deserialize, Default)]
#[serde(rename_all = "camelCase", default)]
struct SettingsEnvelope {
    schema_version: i64,
    settings: Option<AppSettings>,
}

#[derive(Debug)]
pub enum SettingsError {
    Io(std::io::Error),
    Parse(String),
}

impl std::fmt::Display for SettingsError {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            SettingsError::Io(error) => write!(f, "IO 错误: {error}"),
            SettingsError::Parse(message) => write!(f, "解析错误: {message}"),
        }
    }
}

impl std::error::Error for SettingsError {}

struct Shared {
    state: Mutex<StoreState>,
    signal: Condvar,
}

struct StoreState {
    current: AppSettings,
    /// 去抖保存期限；None 表示无待保存。
    save_deadline: Option<Instant>,
    stop: bool,
    /// 每次变更递增，用于合并等待唤醒。
    revision: u64,
}

/// 变更监听器：收到归一化后的新快照。
pub type ChangeListener = Arc<dyn Fn(&AppSettings) + Send + Sync>;

/// 设置存储门面。进程内单实例，由组合根持有。
pub struct SettingsStore {
    directory: PathBuf,
    settings_path: PathBuf,
    backup_path: PathBuf,
    user_defaults_path: PathBuf,
    debounce: Duration,
    shared: Arc<Shared>,
    log: Option<Arc<AppLog>>,
    listeners: Mutex<Vec<ChangeListener>>,
    writer_handle: std::sync::Mutex<Option<std::thread::JoinHandle<()>>>,
    stopped: std::sync::atomic::AtomicBool,
    listener_seq: AtomicU64,
}

impl SettingsStore {
    /// 打开存储并加载（目录缺省 `%LOCALAPPDATA%\AFMediaBar`；防抖可覆盖以便测试）。
    pub fn open(
        directory: Option<PathBuf>,
        debounce: Option<Duration>,
        log: Option<Arc<AppLog>>,
    ) -> Result<Arc<Self>, SettingsError> {
        let directory = directory.unwrap_or_else(default_directory);
        let settings_path = directory.join("settings.json");
        let backup_path = directory.join("settings.json.bak");
        let user_defaults_path = directory.join("user-defaults.json");
        let debounce = debounce.unwrap_or(DEFAULT_DEBOUNCE);

        let mut current = None;
        let mut loaded_schema: Option<i64> = None;
        if settings_path.is_file() {
            match read_envelope(&settings_path) {
                Ok((envelope, schema)) => {
                    current = Some(envelope);
                    loaded_schema = Some(schema);
                }
                Err(LoadError::UnsupportedSchema(version)) => {
                    quarantine(&settings_path, "unsupported");
                    if let Some(log) = &log {
                        log.warning(
                            "Settings",
                            format!("设置文件 schema {version} 不受支持，已隔离并回退默认"),
                        );
                    }
                }
                Err(LoadError::Invalid(message)) => {
                    if let Some(log) = &log {
                        log.warning("Settings", format!("设置文件无效({message})，已隔离"));
                    }
                    quarantine(&settings_path, "invalid");
                }
            }
        }

        if current.is_none() && backup_path.is_file() {
            match read_envelope(&backup_path) {
                Ok((envelope, schema)) => {
                    current = Some(envelope);
                    loaded_schema = Some(schema);
                }
                Err(error) => {
                    if let Some(log) = &log {
                        log.warning("Settings", format!("备份设置不可用: {error}"));
                    }
                }
            }
        }

        let mut current = current.unwrap_or_default();
        current.normalize();

        let store = Arc::new(Self {
            directory,
            settings_path,
            backup_path,
            user_defaults_path,
            debounce,
            shared: Arc::new(Shared {
                state: Mutex::new(StoreState {
                    current,
                    save_deadline: None,
                    stop: false,
                    revision: 0,
                }),
                signal: Condvar::new(),
            }),
            log,
            listeners: Mutex::new(Vec::new()),
            writer_handle: std::sync::Mutex::new(None),
            stopped: std::sync::atomic::AtomicBool::new(false),
            listener_seq: AtomicU64::new(0),
        });

        if !store.settings_path.is_file() {
            // 首次运行/隔离后：立即写出一份当前默认设置。
            store.save_sync();
        }
        if let Some(log) = &store.log {
            // 与 C# 日志一致：反映实际读到的编号（none = 新装/隔离后回退默认）。
            let loaded = loaded_schema
                .map(|v| v.to_string())
                .unwrap_or_else(|| "none".to_owned());
            log.info(
                "Settings",
                format!(
                    "已加载设置: schema {loaded} → {CURRENT_SCHEMA_VERSION}, file={}",
                    store.settings_path.display()
                ),
            );
        }

        let shared = Arc::clone(&store.shared);
        let writer_store = Arc::clone(&store);
        let handle = afmb_core::thread::spawn_observed("afmb-settings-writer", move || {
            writer_loop(writer_store, shared);
        })
        .map_err(SettingsError::Io)?;
        *store.writer_handle.lock().expect("writer 句柄锁不得中毒") = Some(handle);

        Ok(store)
    }

    pub fn settings_path(&self) -> &Path {
        &self.settings_path
    }

    /// 当前设置的快照。
    pub fn current(&self) -> AppSettings {
        self.shared
            .state
            .lock()
            .expect("设置状态锁不得中毒")
            .current
            .clone()
    }

    /// 订阅变更（回调收到新快照）。返回注销序号。
    pub fn on_change(&self, listener: ChangeListener) -> u64 {
        let id = self.listener_seq.fetch_add(1, Ordering::AcqRel);
        self.listeners
            .lock()
            .expect("监听器锁不得中毒")
            .push(listener);
        id
    }

    /// 修改设置：归一化、发布变更、调度去抖保存。
    pub fn update(&self, mutate: impl FnOnce(&mut AppSettings)) {
        let notify = {
            let mut state = self.shared.state.lock().expect("设置状态锁不得中毒");
            mutate(&mut state.current);
            state.current.normalize();
            state.revision += 1;
            state.save_deadline = Some(Instant::now() + self.debounce);
            let snapshot = state.current.clone();
            self.shared.signal.notify_all();
            drop(state);
            snapshot
        };
        self.emit(&notify);
    }

    /// 同步等待当前防抖写入并立即落盘。
    pub fn flush(&self) {
        {
            let mut state = self.shared.state.lock().expect("设置状态锁不得中毒");
            state.save_deadline = None;
            state.revision += 1;
            self.shared.signal.notify_all();
        }
        self.save_sync();
    }

    /// 把当前设置保存为「我的默认设置」；设置本身不变。
    pub fn save_current_as_user_defaults(&self) -> Result<(), SettingsError> {
        let snapshot = self.current();
        write_envelope(&self.user_defaults_path, None, &snapshot)?;
        if let Some(log) = &self.log {
            log.info("Settings", "已保存「我的默认设置」/ user defaults saved");
        }
        Ok(())
    }

    /// 读取「我的默认设置」；不存在返回 None，编号不同则隔离该文件并返回 None。
    pub fn load_user_defaults(&self) -> Result<Option<AppSettings>, SettingsError> {
        if !self.user_defaults_path.is_file() {
            return Ok(None);
        }
        match read_envelope(&self.user_defaults_path) {
            Ok((mut snapshot, _schema)) => {
                snapshot.normalize();
                Ok(Some(snapshot))
            }
            Err(LoadError::UnsupportedSchema(version)) => {
                quarantine(&self.user_defaults_path, "unsupported");
                if let Some(log) = &self.log {
                    log.warning(
                        "Settings",
                        format!("默认设置快照 schema {version} 不受支持，已隔离"),
                    );
                }
                Ok(None)
            }
            Err(LoadError::Invalid(message)) => {
                if let Some(log) = &self.log {
                    log.warning("Settings", format!("默认设置快照无效({message})"));
                }
                Ok(None)
            }
        }
    }

    /// 删除「我的默认设置」快照，让所有重置入口回到内置默认。
    pub fn clear_user_defaults(&self) -> Result<(), SettingsError> {
        match std::fs::remove_file(&self.user_defaults_path) {
            Ok(()) => Ok(()),
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => Ok(()),
            Err(error) => Err(SettingsError::Io(error)),
        }
    }

    /// 打开设置所在目录（资源管理器）。
    pub fn open_settings_folder(&self) -> std::io::Result<()> {
        std::fs::create_dir_all(&self.directory)?;
        std::process::Command::new("explorer.exe")
            .arg(self.directory.as_os_str())
            .spawn()
            .map(|_| ())
    }

    fn emit(&self, snapshot: &AppSettings) {
        let listeners = self.listeners.lock().expect("监听器锁不得中毒").clone();
        for listener in listeners {
            listener(snapshot);
        }
    }

    fn save_sync(&self) {
        let snapshot = self
            .shared
            .state
            .lock()
            .expect("设置状态锁不得中毒")
            .current
            .clone();
        match write_envelope(&self.settings_path, Some(&self.backup_path), &snapshot) {
            Ok(()) => {}
            Err(error) => {
                if let Some(log) = &self.log {
                    log.error(
                        "Settings",
                        format!("保存设置失败，内存中的设置保留: {error}"),
                    );
                }
            }
        }
    }

    /// 停止写入线程并落盘（进程退出前调用；此后 update 仍可改内存但不落盘）。
    pub fn shutdown(&self) {
        if self.stopped.swap(true, Ordering::AcqRel) {
            return;
        }
        {
            let mut state = self.shared.state.lock().expect("设置状态锁不得中毒");
            state.stop = true;
            state.save_deadline = None;
            self.shared.signal.notify_all();
        }
        if let Some(handle) = self
            .writer_handle
            .lock()
            .expect("writer 句柄锁不得中毒")
            .take()
        {
            let _ = handle.join();
        }
        self.save_sync();
    }
}

impl Drop for SettingsStore {
    fn drop(&mut self) {
        self.shutdown();
    }
}

fn writer_loop(store: Arc<SettingsStore>, shared: Arc<Shared>) {
    let mut guard = shared.state.lock().expect("设置状态锁不得中毒");
    loop {
        let deadline = guard.save_deadline;
        let Some(deadline) = deadline else {
            if guard.stop {
                return;
            }
            guard = shared.signal.wait(guard).expect("设置等待锁不得中毒");
            continue;
        };
        let now = Instant::now();
        if now < deadline {
            let (next, _timeout) = shared
                .signal
                .wait_timeout(guard, deadline - now)
                .expect("设置等待锁不得中毒");
            guard = next;
            continue;
        }
        guard.save_deadline = None;
        let snapshot = guard.current.clone();
        drop(guard);
        match write_envelope(&store.settings_path, Some(&store.backup_path), &snapshot) {
            Ok(()) => {}
            Err(error) => {
                if let Some(log) = &store.log {
                    log.error(
                        "Settings",
                        format!("保存设置失败，内存中的设置保留: {error}"),
                    );
                }
            }
        }
        guard = shared.state.lock().expect("设置状态锁不得中毒");
    }
}

enum LoadError {
    UnsupportedSchema(i64),
    Invalid(String),
}

impl std::fmt::Display for LoadError {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            LoadError::UnsupportedSchema(version) => write!(f, "schema {version}"),
            LoadError::Invalid(message) => f.write_str(message),
        }
    }
}

/// 读取信封（容忍注释/尾逗号）；只接受当前 schema 编号。返回 (设置, 文件实际编号)。
fn read_envelope(path: &Path) -> Result<(AppSettings, i64), LoadError> {
    let text = std::fs::read_to_string(path)
        .map_err(|error| LoadError::Invalid(format!("读取失败: {error}")))?;
    let mut value: serde_json::Value = serde_json5::from_str(&text)
        .map_err(|error| LoadError::Invalid(format!("JSON 解析失败: {error}")))?;
    let Some(object) = value.as_object_mut() else {
        return Err(LoadError::Invalid("Settings envelope is empty.".to_owned()));
    };
    // 移植 C# ReadEnvelope：五个可能含 NaN/Infinity 的旧字段若为 null 则视为缺失。
    if let Some(settings) = object.get_mut("settings").and_then(|s| s.as_object_mut()) {
        for key in [
            "layoutLengthScalePercent",
            "layoutThicknessScalePercent",
            "taskbarBarCrossAxisOffsetDip",
            "dynamicIslandLeft",
            "dynamicIslandTop",
        ] {
            if settings.get(key).is_some_and(|v| v.is_null()) {
                settings.remove(key);
            }
        }
    }
    let envelope: SettingsEnvelope = serde_json::from_value(value)
        .map_err(|error| LoadError::Invalid(format!("信封绑定失败: {error}")))?;
    if envelope.schema_version != CURRENT_SCHEMA_VERSION {
        return Err(LoadError::UnsupportedSchema(envelope.schema_version));
    }
    let mut settings = envelope.settings.unwrap_or_default();
    settings.normalize();
    Ok((settings, envelope.schema_version))
}

fn write_envelope(
    path: &Path,
    backup: Option<&Path>,
    settings: &AppSettings,
) -> Result<(), SettingsError> {
    let directory = path
        .parent()
        .ok_or_else(|| SettingsError::Parse("设置路径缺少父目录".to_owned()))?;
    std::fs::create_dir_all(directory).map_err(SettingsError::Io)?;
    let mut normalized = settings.clone();
    normalized.normalize();
    let envelope = SettingsEnvelope {
        schema_version: CURRENT_SCHEMA_VERSION,
        settings: Some(normalized),
    };
    let body = serde_json::to_string_pretty(&envelope)
        .map_err(|error| SettingsError::Parse(format!("序列化失败: {error}")))?;

    // 原子写：独占创建临时文件 + 全量落盘 → 复制旧文件为 .bak → 改名覆盖。
    let temporary = directory.join(format!("settings.{}.tmp", unique_suffix()));
    let write_result = (|| -> std::io::Result<()> {
        use std::io::Write;
        let mut file = std::fs::OpenOptions::new()
            .create_new(true)
            .write(true)
            .open(&temporary)?;
        file.write_all(body.as_bytes())?;
        file.sync_all()
    })();
    if let Err(error) = write_result {
        let _ = std::fs::remove_file(&temporary);
        return Err(SettingsError::Io(error));
    }
    if let Some(backup) = backup
        && path.is_file()
        && let Err(error) = std::fs::copy(path, backup)
    {
        let _ = std::fs::remove_file(&temporary);
        return Err(SettingsError::Io(error));
    }
    if let Err(error) = std::fs::rename(&temporary, path) {
        let _ = std::fs::remove_file(&temporary);
        return Err(SettingsError::Io(error));
    }
    Ok(())
}

/// 隔离：改名留档（不删除）。
fn quarantine(path: &Path, reason: &str) {
    let target = path.with_file_name(format!(
        "{}.{reason}-{timestamp}",
        file_stem_with_extension(path),
        timestamp = quarantine_timestamp()
    ));
    if std::fs::rename(path, target).is_err() {
        // 改名失败（如被占用）：保留原文件，由内置默认接管。
    }
}

fn file_stem_with_extension(path: &Path) -> String {
    path.file_name()
        .map(|n| n.to_string_lossy().into_owned())
        .unwrap_or_default()
}

/// 唯一后缀：进程 ID + 纳秒时间（十六进制），避免引入 uuid 依赖。
fn unique_suffix() -> String {
    let nanos = std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map(|d| d.as_nanos())
        .unwrap_or(0);
    format!("{:x}{:x}", std::process::id(), nanos)
}

/// 隔离时间戳：`yyyyMMddHHmmssfff`（UTC）。
fn quarantine_timestamp() -> String {
    let millis = std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map(|d| d.as_millis() as u64)
        .unwrap_or(0);
    let secs = millis / 1000;
    let days = secs / 86_400;
    let secs_of_day = secs % 86_400;
    let z = days as i64 + 719_468;
    let era = z.div_euclid(146_097);
    let doe = z.rem_euclid(146_097);
    let yoe = (doe - doe / 1460 + doe / 36_524 - doe / 146_096) / 365;
    let y = yoe + era * 400;
    let doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
    let mp = (5 * doy + 2) / 153;
    let d = doy - (153 * mp + 2) / 5 + 1;
    let m = if mp < 10 { mp + 3 } else { mp - 9 };
    let year = if m <= 2 { y + 1 } else { y };
    format!(
        "{year:04}{month:02}{day:02}{h:02}{min:02}{s:02}{ms:03}",
        month = m,
        day = d,
        h = secs_of_day / 3600,
        min = (secs_of_day % 3600) / 60,
        s = secs_of_day % 60,
        ms = millis % 1000
    )
}

/// 默认目录：`%LOCALAPPDATA%\AFMediaBar`。
fn default_directory() -> PathBuf {
    let base = std::env::var_os("LOCALAPPDATA")
        .map(PathBuf::from)
        .unwrap_or_else(|| PathBuf::from("."));
    base.join("AFMediaBar")
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::fs;

    fn write_file(path: &Path, content: &str) {
        std::fs::create_dir_all(path.parent().unwrap()).expect("create_dir_all");
        fs::write(path, content).expect("write");
    }

    #[test]
    fn fresh_store_writes_settings_file_with_current_schema() {
        let dir = tempfile::tempdir().expect("tempdir");
        let store = SettingsStore::open(
            Some(dir.path().to_path_buf()),
            Some(Duration::from_millis(20)),
            None,
        )
        .expect("open");
        let envelope: serde_json::Value =
            serde_json5::from_str(&fs::read_to_string(store.settings_path()).expect("read"))
                .expect("json");
        assert_eq!(envelope["schemaVersion"], 2);
        assert!(envelope["settings"]["lyricsEnabled"].as_bool().unwrap());
        store.shutdown();
    }

    #[test]
    fn round_trip_preserves_unknown_group_fields() {
        let dir = tempfile::tempdir().expect("tempdir");
        let settings_path = dir.path().join("settings.json");
        write_file(
            &settings_path,
            r#"{
                // C# 读取容忍注释与尾逗号
                "schemaVersion": 2,
                "settings": {
                    "lyricsEnabled": false,
                    "layoutLengthScalePercent": 110,
                    "appearance": { "unknownFutureField": "keep-me", "backdropMode": "Acrylic" },
                    "futureGroup": { "nested": [1, 2, 3] },
                },
            }"#,
        );
        let store = SettingsStore::open(
            Some(dir.path().to_path_buf()),
            Some(Duration::from_millis(20)),
            None,
        )
        .expect("open");
        assert!(!store.current().lyrics_enabled);
        assert_eq!(store.current().layout_length_scale_percent, 110.0);

        // 修改根字段后写回：未知分组字段必须原样保留。
        store.update(|settings| settings.lyrics_enabled = true);
        store.flush();

        let envelope: serde_json::Value =
            serde_json5::from_str(&fs::read_to_string(store.settings_path()).expect("read"))
                .expect("json");
        assert_eq!(
            envelope["settings"]["appearance"]["unknownFutureField"],
            "keep-me"
        );
        assert_eq!(envelope["settings"]["futureGroup"]["nested"][2], 3);
        assert_eq!(envelope["settings"]["layoutLengthScalePercent"], 110.0);
        assert_eq!(envelope["settings"]["lyricsEnabled"], true);
        store.shutdown();
    }

    #[test]
    fn quarantines_wrong_schema_and_falls_back_to_defaults() {
        let dir = tempfile::tempdir().expect("tempdir");
        let settings_path = dir.path().join("settings.json");
        write_file(
            &settings_path,
            r#"{ "schemaVersion": 1, "settings": { "lyricsEnabled": false } }"#,
        );
        let store = SettingsStore::open(
            Some(dir.path().to_path_buf()),
            Some(Duration::from_millis(20)),
            None,
        )
        .expect("open");
        assert!(store.current().lyrics_enabled, "编号不同必须回退内置默认");
        let quarantined: Vec<_> = fs::read_dir(dir.path())
            .expect("read_dir")
            .filter_map(|entry| entry.ok())
            .map(|entry| entry.file_name().to_string_lossy().into_owned())
            .filter(|name| name.starts_with("settings.json.unsupported-"))
            .collect();
        assert_eq!(
            quarantined.len(),
            1,
            "旧编号文件应被隔离改名: {quarantined:?}"
        );
        store.shutdown();
    }

    #[test]
    fn recovers_from_backup_when_main_file_invalid() {
        let dir = tempfile::tempdir().expect("tempdir");
        let settings_path = dir.path().join("settings.json");
        let backup_path = dir.path().join("settings.json.bak");
        write_file(&settings_path, "{ this is not json ");
        write_file(
            &backup_path,
            r#"{ "schemaVersion": 2, "settings": { "developerModeEnabled": true } }"#,
        );
        let store = SettingsStore::open(
            Some(dir.path().to_path_buf()),
            Some(Duration::from_millis(20)),
            None,
        )
        .expect("open");
        assert!(
            store.current().developer_mode_enabled,
            "主文件无效时必须从 .bak 恢复"
        );
        assert!(
            fs::read_dir(dir.path())
                .expect("read_dir")
                .filter_map(|entry| entry.ok())
                .any(|entry| entry.file_name().to_string_lossy().contains("invalid-")),
            "无效主文件应被隔离"
        );
        store.shutdown();
    }

    #[test]
    fn debounced_update_persists_after_interval() {
        let dir = tempfile::tempdir().expect("tempdir");
        let store = SettingsStore::open(
            Some(dir.path().to_path_buf()),
            Some(Duration::from_millis(40)),
            None,
        )
        .expect("open");
        store.update(|settings| settings.developer_mode_enabled = true);
        // 去抖窗口内尚未写盘。
        std::thread::sleep(Duration::from_millis(10));
        assert!(
            !fs::read_to_string(store.settings_path())
                .expect("read")
                .contains("\"developerModeEnabled\": true")
        );
        std::thread::sleep(Duration::from_millis(120));
        assert!(
            fs::read_to_string(store.settings_path())
                .expect("read")
                .contains("\"developerModeEnabled\": true")
        );
        store.shutdown();
    }

    #[test]
    fn change_listeners_receive_normalized_snapshot() {
        let dir = tempfile::tempdir().expect("tempdir");
        let store = SettingsStore::open(
            Some(dir.path().to_path_buf()),
            Some(Duration::from_millis(20)),
            None,
        )
        .expect("open");
        let seen = Arc::new(Mutex::new(None));
        let seen2 = Arc::clone(&seen);
        store.on_change(Arc::new(move |settings| {
            *seen2.lock().unwrap() = Some(settings.layout_length_scale_percent);
        }));
        store.update(|settings| settings.layout_length_scale_percent = 999.0);
        let observed = *seen.lock().unwrap();
        assert_eq!(observed, Some(125.0), "监听器收到的是归一化后的快照");
        store.shutdown();
    }

    #[test]
    fn user_defaults_save_load_and_clear() {
        let dir = tempfile::tempdir().expect("tempdir");
        let store = SettingsStore::open(
            Some(dir.path().to_path_buf()),
            Some(Duration::from_millis(20)),
            None,
        )
        .expect("open");
        store.update(|settings| settings.developer_mode_enabled = true);
        store
            .save_current_as_user_defaults()
            .expect("save defaults");
        let snapshot = store.load_user_defaults().expect("load defaults");
        assert!(snapshot.expect("snapshot").developer_mode_enabled);
        store.clear_user_defaults().expect("clear");
        assert!(store.load_user_defaults().expect("load").is_none());
        store.shutdown();
    }

    #[test]
    fn user_defaults_with_wrong_schema_is_quarantined_not_applied() {
        let dir = tempfile::tempdir().expect("tempdir");
        let store = SettingsStore::open(
            Some(dir.path().to_path_buf()),
            Some(Duration::from_millis(20)),
            None,
        )
        .expect("open");
        write_file(
            &dir.path().join("user-defaults.json"),
            r#"{ "schemaVersion": 3, "settings": { "lyricsEnabled": false } }"#,
        );
        let snapshot = store.load_user_defaults().expect("load");
        assert!(snapshot.is_none(), "编号不同的快照不得应用");
        assert!(
            fs::read_dir(dir.path())
                .expect("read_dir")
                .filter_map(|entry| entry.ok())
                .any(|entry| entry
                    .file_name()
                    .to_string_lossy()
                    .contains("user-defaults.json.unsupported-")),
            "快照应被隔离留档"
        );
        store.shutdown();
    }
}
