# Contributing to MultiLock

Thank you for your interest in contributing to MultiLock! This document provides guidelines and instructions for contributing to the project.

## Table of Contents

- [Code of Conduct](#code-of-conduct)
- [Getting Started](#getting-started)
- [Development Setup](#development-setup)
- [How to Contribute](#how-to-contribute)
- [Coding Standards](#coding-standards)
- [Testing Guidelines](#testing-guidelines)
- [Pull Request Process](#pull-request-process)
- [Commit Message Guidelines](#commit-message-guidelines)
- [Provider Development](#provider-development)
- [Documentation](#documentation)
- [Community](#community)

## Code of Conduct

This project adheres to the Contributor Covenant Code of Conduct. By participating, you are expected to uphold this code. Please read our [Code of Conduct](CODE_OF_CONDUCT.md) for details. Please report unacceptable behavior to [stein.gran@protonmail.com](mailto:stein.gran@protonmail.com).

## Getting Started

1. **Fork the repository** on GitHub
2. **Clone your fork** locally:
   ```bash
   git clone https://github.com/YOUR-USERNAME/MultiLock.git
   cd MultiLock
   ```
3. **Add the upstream repository**:
   ```bash
   git remote add upstream https://github.com/steingran/MultiLock.git
   ```
4. **Create a branch** for your changes:
   ```bash
   git checkout -b feature/your-feature-name
   ```

## Development Setup

### Prerequisites

- **.NET 8.0 SDK** or later
- **Docker Desktop** (for integration tests)
- **Git**
- **Visual Studio 2022**, **JetBrains Rider**, or **VS Code** (recommended)

### Building the Project

```bash
# Restore dependencies
dotnet restore

# Build the solution
dotnet build

# Run unit tests
dotnet test tests/MultiLock.Tests/
```

### Running Integration Tests

Integration tests require Docker to be running:

```bash
# Start all test services (PostgreSQL, Redis, SQL Server, Consul, ZooKeeper, Azurite)
cd tests
docker-compose up -d

# Wait for services to be healthy
docker-compose ps

# Run integration tests
dotnet test MultiLock.IntegrationTests/

# Stop services when done
docker-compose down
```

### Project Structure

```
MultiLock/
├── src/
│   ├── MultiLock/                    # Core framework
│   └── Providers/                    # Provider implementations
│       ├── MultiLock.AzureBlobStorage/
│       ├── MultiLock.Consul/
│       ├── MultiLock.FileSystem/
│       ├── MultiLock.InMemory/
│       ├── MultiLock.PostgreSQL/
│       ├── MultiLock.Redis/
│       ├── MultiLock.SqlServer/
│       └── MultiLock.ZooKeeper/
├── tests/
│   ├── MultiLock.Tests/              # Unit tests
│   └── MultiLock.IntegrationTests/   # Integration tests
├── samples/
│   ├── MultiLock.Sample/             # Basic sample
│   └── LeaderElection.MultiProvider/ # Multi-provider demo
└── docs/                             # Documentation
```

## How to Contribute

### Reporting Bugs

- **Check existing issues** to avoid duplicates
- **Use the bug report template** when creating a new issue
- **Include**:
  - Clear description of the problem
  - Steps to reproduce
  - Expected vs actual behavior
  - Environment details (.NET version, OS, provider used)
  - Stack traces or error messages

### Suggesting Enhancements

- **Check existing issues** and discussions
- **Use the feature request template**
- **Describe**:
  - The problem you're trying to solve
  - Your proposed solution
  - Alternative solutions considered
  - Any breaking changes

### Contributing Code

1. **Find or create an issue** describing what you'll work on
2. **Comment on the issue** to let others know you're working on it
3. **Follow the coding standards** (see below)
4. **Write tests** for your changes
5. **Update documentation** as needed
6. **Submit a pull request**

## Coding Standards

### C# Style Guidelines

This project follows modern C# best practices and conventions:

- **Use the latest C# syntax** available in .NET 8.0
- **Enable nullable reference types** (`<Nullable>enable</Nullable>`)
- **One class/interface/record per file**
- **No `#region` directives** - use comments instead
- **No brackets for one-line if statements**:
  ```csharp
  // Good
  if (condition)
      DoSomething();
  
  // Avoid
  if (condition)
  {
      DoSomething();
  }
  ```

### EditorConfig

The project includes an `.editorconfig` file that enforces consistent formatting. Your IDE should automatically apply these settings.

Key conventions:
- **Indentation**: 4 spaces for C#, 2 spaces for XML/JSON/YAML
- **Line endings**: LF (Unix-style)
- **Encoding**: UTF-8
- **Braces**: Allman style (new line)
- **Naming**: PascalCase for public members, camelCase for private fields

### Code Quality

- **Follow SOLID principles**
- **Keep methods focused and small**
- **Use meaningful names** for variables, methods, and classes
- **Add XML documentation** for public APIs
- **Handle exceptions appropriately**
- **Use async/await** for I/O operations
- **Dispose resources properly** (use `using` statements)

## Testing Guidelines

### Unit Tests

- **Use xUnit** as the testing framework
- **Use Shouldly** for assertions (not FluentAssertions)
- **Use Moq** for mocking
- **Follow AAA pattern**: Arrange, Act, Assert
- **Name tests descriptively**: `MethodName_Scenario_ExpectedBehavior`

Example:
```csharp
[Fact]
public async Task TryAcquireLeadershipAsync_WhenNotCurrentlyLeader_ShouldAcquireSuccessfully()
{
    // Arrange
    var provider = new InMemoryLeaderElectionProvider();
    
    // Act
    var result = await provider.TryAcquireLeadershipAsync("group1", "participant1", TimeSpan.FromMinutes(5));
    
    // Assert
    result.ShouldBeTrue();
}
```

### Integration Tests

- **Test against real services** (PostgreSQL, Redis, etc.)
- **Do NOT use EF Core In-Memory database** - use the PostgreSQL instance from docker-compose.yml
- **Clean up resources** after tests
- **Use unique identifiers** to avoid test interference
- **Mark with `[Trait("Category", "Integration")]`**

### Test Coverage

- **Aim for high coverage** of core functionality
- **Test edge cases** and error conditions
- **Test concurrent scenarios** for thread safety

## Pull Request Process

### Before Submitting

1. **Ensure all tests pass**:
   ```bash
   dotnet test
   ```

2. **Build succeeds without warnings**:
   ```bash
   dotnet build
   ```

3. **Update documentation** if needed

4. **Add/update tests** for your changes

5. **Follow commit message guidelines** (see below)

### PR Guidelines

- **Use the PR template** provided
- **Link related issues** using keywords (e.g., "Fixes #123")
- **Add appropriate labels**:
  - Type: `feature`, `bug`, `enhancement`, `docs`, etc.
  - Version impact: `major`, `minor`, `patch`
  - Provider-specific: `provider/postgresql`, `provider/redis`, etc.
- **Keep PRs focused** - one feature/fix per PR
- **Respond to review feedback** promptly
- **Ensure CI passes** before requesting review

### PR Title Format

Use conventional commit format:
```
<type>(<scope>): <description>

Examples:
feat(postgresql): add connection pooling support
fix(redis): resolve race condition in leader election
docs: update contribution guidelines
test(sqlserver): add integration tests for failover
```

## Commit Message Guidelines

Follow the [Conventional Commits](https://www.conventionalcommits.org/) specification:

### Format

```
<type>[optional scope]: <description>

[optional body]

[optional footer(s)]
```

### Types

- **feat**: New feature
- **fix**: Bug fix
- **docs**: Documentation changes
- **test**: Adding or updating tests
- **refactor**: Code refactoring
- **perf**: Performance improvements
- **chore**: Maintenance tasks
- **ci**: CI/CD changes

### Examples

```
feat(redis): add support for Redis Sentinel

Implements leader election using Redis Sentinel for high availability.
Includes automatic failover detection and reconnection logic.

Closes #45
```

```
fix(postgresql): prevent connection leak in heartbeat

The heartbeat mechanism was not properly disposing connections,
leading to connection pool exhaustion under high load.

Fixes #78
```

## Provider Development

### Creating a New Provider

If you're adding a new provider:

1. **Create a new project** under `src/Providers/MultiLock.YourProvider/`
2. **Implement `ILeaderElectionProvider`** interface
3. **Add extension methods** for service registration
4. **Include the core framework** as a dependency
5. **Write comprehensive tests**
6. **Add documentation** and examples
7. **Update the main README** to list the new provider

### Provider Requirements

- Thread-safe implementation
- Proper error handling and logging
- Support for configurable timeouts
- Graceful handling of transient failures
- Clean resource disposal
- XML documentation for public APIs

## Documentation

### Code Documentation

- **Add XML comments** for all public APIs
- **Include examples** in documentation where helpful
- **Document exceptions** that can be thrown
- **Explain complex logic** with inline comments

### README Updates

- Update the main README.md if adding features
- Update provider-specific documentation
- Add code examples for new functionality

### Changelog

- The changelog is automatically generated from PR labels
- Ensure your PR has appropriate labels for changelog categorization

## Community

### Getting Help

- **GitHub Discussions**: For questions and general discussion
- **GitHub Issues**: For bug reports and feature requests
- **Email**: [stein.gran@protonmail.com](mailto:stein.gran@protonmail.com)

### Recognition

Contributors will be recognized in:
- The project's README
- Release notes
- GitHub's contributor graph

Thank you for contributing to MultiLock! 🎉

