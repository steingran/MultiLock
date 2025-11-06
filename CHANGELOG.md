# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- Initial release of MultiLock framework
- Support for 8 leader election providers:
  - Azure Blob Storage provider
  - Consul provider
  - File System provider
  - In-Memory provider (for testing and development)
  - PostgreSQL provider
  - Redis provider
  - SQL Server provider
  - ZooKeeper provider
- AsyncEnumerable API for leadership change events
- Extension methods for leadership event filtering
- Comprehensive unit test coverage
- Integration tests with Docker Compose
- CI/CD pipeline with GitHub Actions
- Automatic changelog generation with Release Drafter

### Features
- Leader election with automatic failover
- Configurable heartbeat intervals and timeouts
- Thread-safe concurrent operations
- Graceful shutdown handling
- Metadata support for leader information
- Dependency injection integration with Microsoft.Extensions

### Documentation
- Comprehensive README with usage examples
- Architecture diagrams
- Provider-specific configuration guides
- Testing instructions
- Docker Compose setup for local development

---

## How to Use This Changelog

This changelog is automatically updated when new releases are published. Each release includes:

- **Features** - New functionality added
- **Bug Fixes** - Issues that were fixed
- **Documentation** - Documentation improvements
- **Performance** - Performance improvements
- **Maintenance** - Internal changes and maintenance
- **Dependencies** - Dependency updates
- **Security** - Security-related changes

Release notes are generated from Pull Request titles and labels using [Release Drafter](https://github.com/release-drafter/release-drafter).

---

## Contributing

When creating Pull Requests, please add appropriate labels to help categorize changes:

- `feature` or `enhancement` - New features
- `bug` or `fix` - Bug fixes
- `documentation` or `docs` - Documentation changes
- `test` or `testing` - Test improvements
- `performance` or `perf` - Performance improvements
- `chore` or `maintenance` - Maintenance tasks
- `dependencies` or `deps` - Dependency updates
- `security` - Security fixes
- `breaking` or `major` - Breaking changes

These labels help automatically generate well-organized release notes.

