use std::sync::Mutex;
use std::time::{Duration, Instant};

/// Thread-safe token-bucket rate limiter for bandwidth throttling. A single instance can be shared by
/// several engines to enforce a global limit. Callers may overdraw the bucket (token debt), so a read
/// larger than one second of budget is delayed instead of waiting forever.
pub struct SpeedLimiter {
    state: Mutex<State>,
}

struct State {
    bytes_per_second: i64,
    available: f64,
    last_refill: Instant,
}

impl SpeedLimiter {
    /// `max_bytes_per_second` of 0 disables limiting.
    pub fn new(max_bytes_per_second: i64) -> Self {
        let rate = max_bytes_per_second.max(0);
        Self { state: Mutex::new(State { bytes_per_second: rate, available: rate as f64, last_refill: Instant::now() }) }
    }

    pub fn max_bytes_per_second(&self) -> i64 {
        self.state.lock().unwrap().bytes_per_second
    }

    pub fn set_max_bytes_per_second(&self, value: i64) {
        let mut s = self.state.lock().unwrap();
        let limit = value.max(0);
        s.bytes_per_second = limit;
        s.available = s.available.min(limit as f64);
        s.last_refill = Instant::now();
    }

    pub fn is_enabled(&self) -> bool {
        self.max_bytes_per_second() > 0
    }

    /// How long the caller must wait before it may proceed; consumes `bytes` of budget when it may.
    fn try_consume(&self, bytes: usize) -> Option<Duration> {
        let mut s = self.state.lock().unwrap();
        let rate = s.bytes_per_second;
        if rate <= 0 {
            return None; // disabled, possibly while this call was waiting
        }
        let now = Instant::now();
        let elapsed = now.duration_since(s.last_refill).as_secs_f64();
        if elapsed > 0.0 {
            s.last_refill = now;
            s.available = (s.available + elapsed * rate as f64).min(rate as f64);
        }
        if s.available > 0.0 {
            s.available -= bytes as f64;
            return None;
        }
        Some(Duration::from_secs_f64((-s.available / rate as f64).clamp(0.001, 1.0)))
    }

    /// Consumes `bytes` of budget, waiting while the bucket is in debt. Cancel-safe: dropping the
    /// future (e.g. via `select!` on a cancellation token) abandons the wait.
    pub async fn throttle(&self, bytes: usize) {
        while let Some(delay) = self.try_consume(bytes) {
            tokio::time::sleep(delay).await;
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[tokio::test]
    async fn disabled_limiter_never_waits() {
        let l = SpeedLimiter::new(0);
        assert!(!l.is_enabled());
        let t = Instant::now();
        for _ in 0..1000 {
            l.throttle(1 << 20).await;
        }
        assert!(t.elapsed() < Duration::from_millis(200));
    }

    #[tokio::test]
    async fn enabled_limiter_delays_after_budget_is_spent() {
        // 100 KB/s: the first call is free (full bucket) but leaves 50 KB of debt
        let l = SpeedLimiter::new(100 * 1024);
        l.throttle(150 * 1024).await;
        let t = Instant::now();
        l.throttle(1).await;
        assert!(t.elapsed() >= Duration::from_millis(300), "waited {:?}", t.elapsed());
    }

    #[tokio::test]
    async fn disabling_releases_waiters() {
        let l = std::sync::Arc::new(SpeedLimiter::new(1024));
        l.throttle(1024 * 100).await; // ~100 s of debt
        let l2 = l.clone();
        let waiter = tokio::spawn(async move { l2.throttle(1).await });
        tokio::time::sleep(Duration::from_millis(50)).await;
        l.set_max_bytes_per_second(0);
        tokio::time::timeout(Duration::from_secs(3), waiter).await.unwrap().unwrap();
    }
}

