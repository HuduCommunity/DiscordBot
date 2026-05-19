# Changelog

All notable changes to HuduCommunityBot will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
