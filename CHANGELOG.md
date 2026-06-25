# Changelog

All notable changes to HuduCommunityBot will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.2.22] - 2026-06-25

### Changed

* Add move messages and thread relocation with reaction and pinned metadata copy

## [1.2.21] - 2026-06-24

### Changed

* Restore clickable deleted-message author mention and author icon rendering

## [1.2.20] - 2026-06-24

### Changed

* Guarantee deleted-message avatar thumbnail fallback

## [1.2.19] - 2026-06-24

### Changed

* Stabilize deleted message attribution with early capture and author profile fallback

## [1.2.18] - 2026-06-24

### Changed

* Add receive-time snapshot fallback for deleted message author attribution

## [1.2.17] - 2026-06-24

### Changed

* Enable message cache for deleted-message author attribution

## [1.2.16] - 2026-06-24

### Changed

* Improve deleted message author attribution from audit logs

## [1.2.15] - 2026-06-24

### Changed

* Add moderation event audit logging and deploy secret wiring

## [1.2.14] - 2026-06-20

### Changed

* show offending image in cross-channel spam log embed

## [1.2.13] - 2026-06-18

### Changed

* Show username and user ID in /singlemessage list output

## [1.2.12] - 2026-06-18

### Changed

* Add EF migration for persistent single-message backfill startup fix

## [1.2.11] - 2026-06-18

### Changed

* Add persistent background single-message history backfill

## [1.2.10] - 2026-06-18

### Changed

* Fix single-message enable interaction timeout by deferring response

## [1.2.9] - 2026-06-15

### Changed

* Add configurable cross-channel spam enforcement defaults (delete+timeout on)

## [1.2.8] - 2026-06-15

### Changed

* Fix live test detection race via TCS; make content optional (attachment-only test now supported)

## [1.2.7] - 2026-06-15

### Changed

* Fix cross-channel live test detection state and add attachment-aware spam test support

## [1.2.6] - 2026-06-15

### Changed

* Improve cross-channel spam detection fingerprinting, logging, and add cleanup-enabled live testing

## [1.2.5] - 2026-06-15

### Changed

* Add moderation exemptions, command access controls, and forum log resolution fallback

## [1.2.4] - 2026-06-15

### Changed

* `SingleMessageService` is now fully DB-backed — channel registration no longer requires an `appsettings.json`/env-var config entry; `/singlemessage enable` and `/singlemessage disable` operate directly on the database at runtime with no redeploy needed
* `/singlemessage enable` gains a `scan_history` parameter (default `true`) replacing the old per-channel config flag
* `/singlemessage list` now shows enforcement status (active / disabled) alongside posted users

### Added

* `/spam test` command (requires Manage Messages) — dry-runs the cross-channel spam detector against any text, showing the computed fingerprint, current config, trigger conditions, and enforcement actions without taking any real action

## [1.2.3] - 2026-06-15

### Fixed

* Hudu release feed HTTP timeouts are now logged at `Information` instead of `Warning` — these are transient retryable failures from a slow external endpoint and do not warrant warning-level noise

## [1.2.2] - 2026-06-15

### Fixed

* `YoutubeFeedUrlsEndpointHostedService` is no longer registered when the YouTube monitor is disabled — previously it started unconditionally and would crash with `ObjectDisposedException` at startup

## [1.2.1] - 2026-06-15

### Fixed

* YouTube forum post title now truncates to Discord's 100-character limit; guards against orphaned surrogate pairs at the truncation boundary
* YouTube forum post title falls back to `[{ChannelName}] {VideoId}` if template substitution produces an empty or whitespace-only result
* Log the resolved post title (with length) at Info level before posting, to aid diagnosis of future `BASE_TYPE_BAD_LENGTH` rejections

## [1.2.0] - 2026-06-14

### Added

* Moderation action logging: all mod actions (ban, unban, kick, mute, unmute, warn, clear, purge, lock, unlock, slowmode, single-message deletions) post rich embeds to a configurable forum channel
* Cross-channel spam detection: detects identical messages across channels within a time window, with moderator ban/dismiss action buttons

### Fixed

* YouTube `/set-default-template` parameter description truncated to satisfy Discord's 100-character option description limit

## [1.1.0] - 2026-06-14

### Changed

* Add single-message-per-user channel enforcement with /singlemessage slash commands

## [1.0.22] - 2026-05-27

### Changed

* Switch YouTube feed endpoint to WebApplication

## [1.0.21] - 2026-05-27

### Changed

* Improve slash command registration diagnostics

## [1.0.20] - 2026-05-27

### Changed

* Expose and emit endpoint-based YouTube observability metrics

## [1.0.19] - 2026-05-27

### Changed

* Add YouTube feed URL endpoint for observability

## [1.0.18] - 2026-05-27

### Changed

* Add Prometheus metrics endpoint

## [1.0.17] - 2026-05-21

### Changed

* Remove /status command and StatusModule

## [1.0.16] - 2026-05-21

### Changed

* Remove HaloStatusMonitorService (use /status command instead)

## [1.0.15] - 2026-05-21

### Changed

* Add observed-feed logging for reconciliation dashboard

## [1.0.14] - 2026-05-19

### Changed

* Add Discord timestamp placeholders for YouTube published date templates

## [1.0.13] - 2026-05-19

### Changed

* Support escaped newline tokens in YouTube title and body templates

## [1.0.12] - 2026-05-19

### Changed

* Add configurable YouTube post body template and placeholders

## [1.0.11] - 2026-05-19

### Changed

* Add YouTube title template placeholders from feed metadata

## [1.0.10] - 2026-05-19

### Changed

* Add Hudu community RSS monitor and thread creation for feed posts

## [1.0.9] - 2026-05-19

### Changed

* Fix Hudu release monitor feed parsing and polling reliability

## [1.0.8] - 2026-05-19

### Changed

* Version bump

## [1.0.7] - 2026-05-19

### Changed

* Harmonize disconnect lifecycle logging with Halo and Panda bots

## [1.0.6] - 2026-05-19

### Changed

* Reduce reconnect noise for slash command registration and transient disconnect logging

## [1.0.5] - 2026-05-19

### Changed

* Conditionally register YouTube monitor service when enabled

## [1.0.4] - 2026-05-19

### Changed

* Register slash commands only once; skip re-registration on gateway reconnects

## [1.0.3] - 2026-05-19

### Changed

* Downgrade graceful Discord disconnect log from Warning to Information

## [1.0.2] - 2026-05-19

### Changed

* Fix missing database tables by switching initialisation from EnsureCreated to MigrateAsync

## [1.0.1] - 2026-05-19

### Changed

* Fix heartbeat monitor not starting when slash command registration fails on startup

## [1.0.0] - 2026-05-18

### Changed

* Reset baseline for HuduCommunityBot
