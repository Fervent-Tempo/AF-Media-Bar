//! 滚动日志：有界队列 + 后台写入线程，写满不阻塞调用方（丢弃并计数）。
//!
//! 对应 C# `AppLogService` + `RollingLogFile`：默认 5 MiB × 5 个文件、
//! 会话起止记录、退出前 [`AppLog::flush`]。行格式：UTC ISO-8601 + 级别 + 类别 + 文本。

use std::fmt;
use std::fs::{self, File, OpenOptions};
use std::io::{BufWriter, Write};
use std::path::PathBuf;
use std::sync::Arc;
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::mpsc::{Receiver, SyncSender, sync_channel};
use std::time::{Duration, SystemTime};

const DEFAULT_MAX_BYTES: u64 = 5 * 1024 * 1024;
const DEFAULT_MAX_FILES: usize = 5;
const DEFAULT_QUEUE_CAPACITY: usize = 4096;

#[derive(Debug, Clone, Copy, PartialEq, Eq, PartialOrd, Ord)]
pub enum Level {
    Trace,
    Debug,
    Info,
    Warning,
    Error,
}

impl Level {
    fn as_str(self) -> &'static str {
        match self {
            Level::Trace => "TRACE",
            Level::Debug => "DEBUG",
            Level::Info => "INFO",
            Level::Warning => "WARN",
            Level::Error => "ERROR",
        }
    }
}

struct Msg {
    level: Level,
    category: String,
    message: String,
}

/// 日志门面：克隆廉价，写入非阻塞。
pub struct AppLog {
    tx: SyncSender<Msg>,
    enqueued: AtomicU64,
    processed: AtomicU64,
    dropped: AtomicU64,
    dir: PathBuf,
    max_bytes: u64,
    max_files: usize,
    prefix: String,
}

impl AppLog {
    /// 以默认参数（5 MiB × 5、队列 4096）在目录内初始化并启动后台线程。
    pub fn init(dir: impl Into<PathBuf>) -> Arc<Self> {
        Self::with_options(
            dir,
            "afmb",
            DEFAULT_MAX_BYTES,
            DEFAULT_MAX_FILES,
            DEFAULT_QUEUE_CAPACITY,
        )
    }

    pub fn with_options(
        dir: impl Into<PathBuf>,
        prefix: &str,
        max_bytes: u64,
        max_files: usize,
        queue_capacity: usize,
    ) -> Arc<Self> {
        assert!(max_files >= 1, "至少保留 1 个日志文件");
        let dir = dir.into();
        let (tx, rx) = sync_channel(queue_capacity);
        let log = Arc::new(Self {
            tx,
            enqueued: AtomicU64::new(0),
            processed: AtomicU64::new(0),
            dropped: AtomicU64::new(0),
            dir,
            max_bytes,
            max_files,
            prefix: prefix.to_owned(),
        });
        spawn_writer(Arc::clone(&log), rx);
        log
    }

    /// 非阻塞写入。队列满时丢弃该条并累计丢弃数（不反压调用方）。
    pub fn write(&self, level: Level, category: &str, message: impl fmt::Display) {
        let msg = Msg {
            level,
            category: category.to_owned(),
            message: message.to_string(),
        };
        if self.tx.try_send(msg).is_ok() {
            self.enqueued.fetch_add(1, Ordering::AcqRel);
        } else {
            self.dropped.fetch_add(1, Ordering::Relaxed);
        }
    }

    pub fn info(&self, category: &str, message: impl fmt::Display) {
        self.write(Level::Info, category, message);
    }

    pub fn warning(&self, category: &str, message: impl fmt::Display) {
        self.write(Level::Warning, category, message);
    }

    pub fn error(&self, category: &str, message: impl fmt::Display) {
        self.write(Level::Error, category, message);
    }

    pub fn session_start(&self, version: &str) {
        self.info("session", format!("AF Media Bar (Rust) {version} 启动"));
    }

    pub fn session_end(&self, reason: &str) {
        self.info("session", format!("会话结束：{reason}"));
    }

    /// 因队列满被丢弃的条数。
    pub fn dropped_count(&self) -> u64 {
        self.dropped.load(Ordering::Relaxed)
    }

    /// 阻塞至入队时间不晚于当前时刻的消息全部处理完毕（超时后返回，不保证完成）。
    pub fn flush(&self, timeout: Duration) {
        let target = self.enqueued.load(Ordering::Acquire);
        let deadline = std::time::Instant::now() + timeout;
        while self.processed.load(Ordering::Acquire) < target {
            if std::time::Instant::now() >= deadline {
                return;
            }
            std::thread::sleep(Duration::from_millis(1));
        }
    }

    fn rotate_path(&self, index: usize) -> PathBuf {
        if index == 0 {
            self.dir.join(format!("{}.log", self.prefix))
        } else {
            self.dir.join(format!("{}-{index}.log", self.prefix))
        }
    }
}

fn spawn_writer(log: Arc<AppLog>, rx: Receiver<Msg>) {
    // 用 spawn_observed：writer 内任何 panic（如磁盘故障）都会经全局钩子被观察到，
    // 而不是静默死亡——之后所有写入按丢弃计数，flush 超时返回。
    if crate::thread::spawn_observed("afmb-log", move || writer_loop(log, rx)).is_err() {
        eprintln!("[afmb-core] 日志线程启动失败");
    }
}

type OpenFile = (BufWriter<File>, u64);

fn writer_loop(log: Arc<AppLog>, rx: Receiver<Msg>) {
    let mut open: Option<OpenFile> = None;
    // 批量排空当前积压，每批刷盘一次：突发不丢、磁盘开销可控。
    while let Ok(first) = rx.recv() {
        let mut batch = vec![first];
        while batch.len() < 256 {
            match rx.try_recv() {
                Ok(msg) => batch.push(msg),
                Err(_) => break,
            }
        }
        for msg in batch {
            if open.is_none() {
                open = Some(open_new(&log));
            }
            let (writer, written) = open.as_mut().expect("writer 刚被创建");
            let line = format!(
                "{} [{}] [{}] {}\n",
                format_utc_now(),
                msg.level.as_str(),
                msg.category,
                msg.message
            );
            if writer.write_all(line.as_bytes()).is_err() {
                // 磁盘故障：丢弃句柄，按丢弃计数，下一条消息尝试重建。
                open = None;
                log.dropped.fetch_add(1, Ordering::Relaxed);
            } else {
                *written += line.len() as u64;
                if *written >= log.max_bytes {
                    rotate(&log, &mut open);
                }
            }
            log.processed.fetch_add(1, Ordering::AcqRel);
        }
        if let Some((writer, _)) = open.as_mut() {
            // 批次边界刷盘：flush() 的"已处理"语义以磁盘内容为准。
            let _ = writer.flush();
        }
    }
    if let Some((mut writer, _)) = open {
        let _ = writer.flush();
    }
}

fn open_new(log: &AppLog) -> OpenFile {
    let _ = fs::create_dir_all(&log.dir);
    let file = OpenOptions::new()
        .create(true)
        .append(true)
        .open(log.rotate_path(0))
        .expect("日志文件必须可创建");
    let written = file.metadata().map(|m| m.len()).unwrap_or(0);
    (BufWriter::new(file), written)
}

fn rotate(log: &AppLog, open: &mut Option<OpenFile>) {
    if let Some((writer, _)) = open.as_mut() {
        let _ = writer.flush();
    }
    // 平移历史：最旧的 -(max_files-1) 删除，其余依次后移；当前文件变 -1。
    for index in (1..log.max_files).rev() {
        let from = log.rotate_path(index);
        if index == log.max_files - 1 {
            let _ = fs::remove_file(&from);
        } else if from.exists() {
            let _ = fs::rename(&from, log.rotate_path(index + 1));
        }
    }
    let current = log.rotate_path(0);
    if log.max_files == 1 {
        let _ = fs::remove_file(&current);
    } else {
        let _ = fs::rename(&current, log.rotate_path(1));
    }
    *open = Some(open_new(log));
}

/// UTC 时间戳：`2026-11-08T04:12:33.456Z`（无外部依赖的民用时间换算）。
pub fn format_utc_now() -> String {
    let now = SystemTime::now()
        .duration_since(SystemTime::UNIX_EPOCH)
        .unwrap_or_default();
    format_utc_unix_millis(now.as_millis() as u64)
}

pub fn format_utc_unix_millis(millis: u64) -> String {
    let secs = millis / 1000;
    let ms = millis % 1000;
    let days = secs / 86_400;
    let secs_of_day = secs % 86_400;
    let (year, month, day) = civil_from_days(days as i64);
    format!(
        "{year:04}-{month:02}-{day:02}T{h:02}:{m:02}:{s:02}.{ms:03}Z",
        h = secs_of_day / 3600,
        m = (secs_of_day % 3600) / 60,
        s = secs_of_day % 60
    )
}

/// Howard Hinnant 民用算法：Unix 天数 → (年, 月, 日)。
fn civil_from_days(days: i64) -> (i64, u32, u32) {
    let z = days + 719_468;
    let era = z.div_euclid(146_097);
    let doe = z.rem_euclid(146_097);
    let yoe = (doe - doe / 1460 + doe / 36_524 - doe / 146_096) / 365;
    let y = yoe + era * 400;
    let doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
    let mp = (5 * doy + 2) / 153;
    let d = doy - (153 * mp + 2) / 5 + 1;
    let m = if mp < 10 { mp + 3 } else { mp - 9 };
    (if m <= 2 { y + 1 } else { y }, m as u32, d as u32)
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::fs;
    use std::path::Path;
    use std::time::Instant;

    fn read_current(dir: &Path, prefix: &str) -> String {
        fs::read_to_string(dir.join(format!("{prefix}.log"))).unwrap_or_default()
    }

    #[test]
    fn civil_conversion_matches_known_dates() {
        // 1970-01-01 = 0；2000-03-01 = 11017；2026-11-08 = 20765。
        assert_eq!(civil_from_days(0), (1970, 1, 1));
        assert_eq!(civil_from_days(11_017), (2000, 3, 1));
        assert_eq!(civil_from_days(20_765), (2026, 11, 8));
    }

    #[test]
    fn timestamp_format_is_iso8601_utc() {
        // 2026-11-08T00:00:00Z = 20765 天。
        let millis: u64 = 20_765 * 86_400 * 1000;
        assert_eq!(format_utc_unix_millis(millis), "2026-11-08T00:00:00.000Z");
    }

    #[test]
    fn writes_lines_and_rotates_within_max_files() {
        let dir = tempfile::tempdir().expect("tempdir");
        // 容量 512 可整批吸收 200 条突发，丢弃计数才有确定性。
        let log = AppLog::with_options(dir.path(), "test", 512, 3, 512);
        for i in 0..200 {
            log.write(Level::Info, "unit", format!("line-{i}-payload-0123456789"));
        }
        log.flush(Duration::from_secs(5));
        // 轮转后当前文件可能为空（最后一条恰好触发轮转）：补写一条验证当前文件继续可用。
        log.write(Level::Info, "unit", "tail-after-rotation");
        log.flush(Duration::from_secs(5));
        assert!(
            read_current(dir.path(), "test").contains("tail-after-rotation"),
            "轮转后当前文件应继续写入"
        );
        assert!(
            dir.path().join("test-1.log").exists(),
            "写入 200 条后应至少轮转一次"
        );
        assert!(
            dir.path().join("test-2.log").exists(),
            "max_files=3 保留两份历史"
        );
        assert!(
            !dir.path().join("test-3.log").exists(),
            "历史文件不得超过 max_files"
        );
        assert_eq!(log.dropped_count(), 0, "容量充足时不应丢弃");
    }

    #[test]
    fn flush_returns_within_timeout() {
        let dir = tempfile::tempdir().expect("tempdir");
        let log = AppLog::with_options(dir.path(), "flush", 4096, 2, 16);
        log.write(Level::Info, "unit", "hello");
        let started = Instant::now();
        log.flush(Duration::from_secs(5));
        assert!(started.elapsed() < Duration::from_secs(4));
        assert!(read_current(dir.path(), "flush").contains("hello"));
    }

    #[test]
    fn flush_is_idempotent_under_concurrency() {
        let dir = tempfile::tempdir().expect("tempdir");
        let log = AppLog::with_options(dir.path(), "conc", 4096, 2, 64);
        log.write(Level::Info, "unit", "concurrent");
        let handles: Vec<_> = (0..4)
            .map(|_| {
                let log = Arc::clone(&log);
                std::thread::spawn(move || log.flush(Duration::from_secs(5)))
            })
            .collect();
        for handle in handles {
            handle.join().expect("flush 线程不得 panic");
        }
        log.flush(Duration::from_secs(5));
        assert!(read_current(dir.path(), "conc").contains("concurrent"));
    }
}
