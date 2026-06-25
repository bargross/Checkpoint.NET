# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2025-06-25

### Added
- Initial release of StateCheckpoint.NET.
- Support for saving/loading training checkpoints (weights + optimizer).
- Support for saving/loading inference sessions (KV-cache + token history).
- FileSystem, PostgreSQL, and SQL Server storage providers.
- Background (non‑blocking) saves with configurable queue.
- Complete tokenizer persistence (vocab maps, merge rules, special tokens).
- GUID‑based management with tagging system.
- Permission handling with fallback paths for file‑system stores.
- Full async/await support with cancellation tokens.
- Framework‑agnostic design – works with any C# ML library.

### Security
- Memory‑safe deep copying to prevent data corruption during background saves.