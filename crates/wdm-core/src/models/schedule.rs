use chrono::{DateTime, Duration, Local, NaiveTime, TimeZone};

pub struct ScheduleTrigger;

impl ScheduleTrigger {
    pub fn default_max_lateness() -> Duration {
        Duration::minutes(30)
    }

    /// Decides whether a daily wall-clock time (HH:mm) was reached between two observations of the clock.
    /// Unlike comparing the current minute for equality, this still fires after sleep/hibernate,
    /// a busy dispatcher or a daylight-saving jump that skips the scheduled time.
    pub fn crossed(
        previous: DateTime<Local>,
        now: DateTime<Local>,
        time: &str,
        max_lateness: Option<Duration>,
    ) -> bool {
        if now <= previous {
            return false;
        }

        let Some(naive_time) = Self::parse_time(time) else {
            return false;
        };

        let limit = max_lateness.unwrap_or_else(Self::default_max_lateness);

        let mut current_date = previous.date_naive();
        let end_date = now.date_naive();

        while current_date <= end_date {
            let naive_dt = current_date.and_time(naive_time);
            if let Some(moment) = Local.from_local_datetime(&naive_dt).single() {
                if moment > previous && moment <= now && (now - moment) <= limit {
                    return true;
                }
            }
            current_date = match current_date.succ_opt() {
                Some(d) => d,
                None => break,
            };
        }

        false
    }

    fn parse_time(time_str: &str) -> Option<NaiveTime> {
        let trimmed = time_str.trim();
        let (h_str, m_str) = trimmed.split_once(':')?;
        let h: u32 = h_str.parse().ok()?;
        let m: u32 = m_str.parse().ok()?;
        NaiveTime::from_hms_opt(h, m, 0)
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use chrono::{Duration, TimeZone};

    #[test]
    fn test_schedule_trigger_crossed() {
        let t1 = Local.with_ymd_and_hms(2026, 11, 3, 1, 55, 0).unwrap();
        let t2 = Local.with_ymd_and_hms(2026, 11, 3, 2, 5, 0).unwrap();
        assert!(ScheduleTrigger::crossed(t1, t2, "02:00", None));
        assert!(!ScheduleTrigger::crossed(t1, t2, "03:00", None));
    }

    #[test]
    fn test_schedule_trigger_too_late() {
        let t1 = Local.with_ymd_and_hms(2026, 11, 3, 1, 0, 0).unwrap();
        let t2 = Local.with_ymd_and_hms(2026, 11, 3, 3, 0, 0).unwrap();
        // 02:00 is 60 minutes ago, limit is 30 mins
        assert!(!ScheduleTrigger::crossed(t1, t2, "02:00", None));
        // With 70 mins lateness allowed:
        assert!(ScheduleTrigger::crossed(t1, t2, "02:00", Some(Duration::minutes(70))));
    }
}
