//! Builds and sends download requests. Redirects are followed manually so that credentials
//! (cookies, basic auth, referer) are only ever sent to the host the request was made for.

use std::time::Duration;

use base64::{engine::general_purpose::STANDARD, Engine};
use reqwest::header::{
    HeaderMap, HeaderValue, ACCEPT, ACCEPT_ENCODING, AUTHORIZATION, COOKIE, LOCATION, RANGE,
    REFERER, USER_AGENT,
};
use reqwest::{redirect, Client, Method, Response, StatusCode};
use tokio_util::sync::CancellationToken;
use url::Url;

use crate::models::settings::DEFAULT_USER_AGENT;
use crate::models::DownloadRequestOptions;
use crate::{Error, Result};

const MAX_REDIRECTS: usize = 10;

/// An HTTP byte range; `to` is inclusive, `None` means "to the end".
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct ByteRange {
    pub from: i64,
    pub to: Option<i64>,
}

impl ByteRange {
    fn header_value(&self) -> String {
        match self.to {
            Some(to) => format!("bytes={}-{}", self.from, to),
            None => format!("bytes={}-", self.from),
        }
    }
}

/// A client that never follows redirects, keeps no cookies and never decompresses
/// (offsets and sizes must refer to the bytes on the wire).
pub fn create_client(connect_timeout: Option<Duration>) -> Result<Client> {
    Ok(Client::builder()
        .redirect(redirect::Policy::none())
        .connect_timeout(connect_timeout.unwrap_or(Duration::from_secs(30)))
        .pool_idle_timeout(Duration::from_secs(120))
        .pool_max_idle_per_host(64)
        .build()?)
}

/// True for CR, LF and other control characters (tab excepted), which are never valid in these header values.
pub fn has_control_characters(value: &str) -> bool {
    value.chars().any(|c| c.is_control() && c != '\t')
}

fn is_redirect(code: StatusCode) -> bool {
    matches!(code.as_u16(), 301 | 302 | 303 | 307 | 308)
}

fn is_blank(s: Option<&str>) -> bool {
    s.map_or(true, |v| v.trim().is_empty())
}

/// Request headers for one hop of a download. `origin` is the URL the user asked for, `current` the hop being requested.
pub fn build_headers(
    options: Option<&DownloadRequestOptions>,
    range: Option<ByteRange>,
    origin: &Url,
    current: &Url,
) -> HeaderMap {
    let mut h = HeaderMap::new();
    let put = |h: &mut HeaderMap, name, value: &str| {
        if let Ok(v) = HeaderValue::from_str(value) {
            h.insert(name, v);
        }
    };

    let ua = options.and_then(|o| o.user_agent.as_deref());
    let ua = if is_blank(ua) || has_control_characters(ua.unwrap()) {
        DEFAULT_USER_AGENT
    } else {
        ua.unwrap()
    };
    put(&mut h, USER_AGENT, ua);
    put(&mut h, ACCEPT, "*/*");
    put(&mut h, ACCEPT_ENCODING, "identity");
    if let Some(r) = range {
        put(&mut h, RANGE, &r.header_value());
    }

    let Some(o) = options else { return h };

    if let Some(referer) = o.referer.as_deref() {
        if !referer.trim().is_empty() && !has_control_characters(referer) {
            put(&mut h, REFERER, referer);
        }
    }

    // Credentials stay with the host they were issued for and never travel over a downgrade to plain http
    if origin.scheme() == "https" && current.scheme() != "https" {
        return h;
    }

    let same_host = |a: &Url, b: &str| a.host_str().is_some_and(|x| x.eq_ignore_ascii_case(b));
    let cookie_host = match o.cookie_host.as_deref() {
        Some(c) if !c.trim().is_empty() => c.to_string(),
        _ => origin.host_str().unwrap_or_default().to_string(),
    };
    if let Some(cookies) = o.cookies.as_deref() {
        if !cookies.trim().is_empty()
            && !has_control_characters(cookies)
            && same_host(current, &cookie_host)
        {
            put(&mut h, COOKIE, cookies);
        }
    }

    if let Some(user) = o.username.as_deref().filter(|u| !u.is_empty()) {
        if same_host(current, origin.host_str().unwrap_or_default()) {
            let token = STANDARD.encode(format!("{user}:{}", o.password.as_deref().unwrap_or("")));
            put(&mut h, AUTHORIZATION, &format!("Basic {token}"));
        }
    }
    h
}

pub fn parse_url(url: &str) -> Result<Url> {
    let parsed = Url::parse(url.trim())
        .map_err(|e| Error::InvalidArgument(format!("Invalid URL '{url}': {e}")))?;
    match parsed.scheme() {
        "http" | "https" => Ok(parsed),
        other => Err(Error::InvalidArgument(format!(
            "Unsupported URL scheme '{other}'."
        ))),
    }
}

/// Sends a request, following redirects by hand. Returns `Error::Canceled` if `ct` fires first.
pub async fn send(
    client: &Client,
    method: Method,
    url: &str,
    options: Option<&DownloadRequestOptions>,
    range: Option<ByteRange>,
    ct: &CancellationToken,
) -> Result<Response> {
    let origin = parse_url(url)?;
    let mut current = origin.clone();

    for hop in 0.. {
        let request = client
            .request(method.clone(), current.clone())
            .headers(build_headers(options, range, &origin, &current));
        let response = tokio::select! {
            _ = ct.cancelled() => return Err(Error::Canceled),
            r = request.send() => r?,
        };

        let location = response
            .headers()
            .get(LOCATION)
            .and_then(|v| v.to_str().ok())
            .map(str::to_owned);
        let Some(location) = location.filter(|_| is_redirect(response.status())) else {
            return Ok(response);
        };
        drop(response);

        if hop >= MAX_REDIRECTS {
            return Err(Error::Http("Too many redirects.".into()));
        }
        current = current
            .join(&location)
            .map_err(|e| Error::Http(format!("Bad redirect location '{location}': {e}")))?;
    }
    unreachable!()
}

#[cfg(test)]
mod tests {
    use super::*;

    fn u(s: &str) -> Url {
        Url::parse(s).unwrap()
    }

    fn opts() -> DownloadRequestOptions {
        DownloadRequestOptions {
            cookies: Some("sid=1".into()),
            username: Some("bob".into()),
            password: Some("pw".into()),
            referer: Some("https://ref/".into()),
            ..Default::default()
        }
    }

    #[test]
    fn defaults_and_range() {
        let h = build_headers(
            None,
            Some(ByteRange {
                from: 5,
                to: Some(9),
            }),
            &u("http://a/"),
            &u("http://a/"),
        );
        assert_eq!(h[USER_AGENT], DEFAULT_USER_AGENT);
        assert_eq!(h[ACCEPT_ENCODING], "identity");
        assert_eq!(h[RANGE], "bytes=5-9");
        let h = build_headers(
            None,
            Some(ByteRange { from: 5, to: None }),
            &u("http://a/"),
            &u("http://a/"),
        );
        assert_eq!(h[RANGE], "bytes=5-");
    }

    #[test]
    fn credentials_go_to_the_origin_host() {
        let o = opts();
        let h = build_headers(Some(&o), None, &u("https://a.com/f"), &u("https://A.com/g"));
        assert_eq!(h[COOKIE], "sid=1");
        assert_eq!(
            h[AUTHORIZATION],
            format!("Basic {}", STANDARD.encode("bob:pw"))
        );
        assert_eq!(h[REFERER], "https://ref/");
    }

    #[test]
    fn credentials_are_not_sent_to_other_hosts() {
        let h = build_headers(
            Some(&opts()),
            None,
            &u("https://a.com/f"),
            &u("https://cdn.other.com/f"),
        );
        assert!(h.get(COOKIE).is_none() && h.get(AUTHORIZATION).is_none());
        assert!(h.get(REFERER).is_some(), "referer is not a credential");
    }

    #[test]
    fn cookie_host_override_is_honoured() {
        let mut o = opts();
        o.cookie_host = Some("cdn.other.com".into());
        let h = build_headers(
            Some(&o),
            None,
            &u("https://a.com/f"),
            &u("https://cdn.other.com/f"),
        );
        assert_eq!(h[COOKIE], "sid=1");
        assert!(
            h.get(AUTHORIZATION).is_none(),
            "basic auth never leaves the origin host"
        );
    }

    #[test]
    fn https_to_http_downgrade_strips_credentials() {
        let h = build_headers(
            Some(&opts()),
            None,
            &u("https://a.com/f"),
            &u("http://a.com/f"),
        );
        assert!(h.get(COOKIE).is_none() && h.get(AUTHORIZATION).is_none());
    }

    #[test]
    fn header_injection_is_rejected() {
        let mut o = opts();
        o.user_agent = Some("evil\r\nX-Injected: 1".into());
        o.cookies = Some("a=b\r\nX: y".into());
        o.referer = Some("http://x\nX: y".into());
        let h = build_headers(Some(&o), None, &u("http://a/"), &u("http://a/"));
        assert_eq!(h[USER_AGENT], DEFAULT_USER_AGENT);
        assert!(h.get(COOKIE).is_none() && h.get(REFERER).is_none());
        assert!(h.get("x-injected").is_none());
    }

    #[test]
    fn url_validation() {
        assert!(parse_url("https://a.com/x").is_ok());
        assert!(matches!(
            parse_url("ftp://a.com/x"),
            Err(Error::InvalidArgument(_))
        ));
        assert!(matches!(
            parse_url("not a url"),
            Err(Error::InvalidArgument(_))
        ));
        assert!(matches!(
            parse_url("file:///c:/x"),
            Err(Error::InvalidArgument(_))
        ));
    }
}
