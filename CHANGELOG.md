# Changelog

All notable changes to HuduCommunityBot will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
