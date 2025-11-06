# GitHub Actions Workflows

This directory contains the CI/CD workflows for the MultiLock project.

## Workflows

### 1. Build and Test (`build-and-test.yml`)

**Triggers:**
- Push to `main` branch
- Pull requests to `main` branch
- Manual workflow dispatch

**Purpose:**
- Validates code quality on every commit
- Runs all unit and integration tests
- Provides test reports on PRs

**Steps:**
1. Checkout code
2. Setup .NET 8.0 SDK
3. Restore dependencies
4. Build solution in Release mode
5. Start Docker Compose services (PostgreSQL, Redis, SQL Server, Consul, ZooKeeper, Azurite)
6. Run all tests with timeout protection
7. Generate test reports
8. Upload test artifacts on failure
9. Cleanup Docker services

---

### 2. Publish to NuGet (`publish-nuget.yml`)

**Triggers:**
- Push of Git tags matching `v*` (e.g., `v1.0.0`, `v1.1.0-beta.1`)
- Manual workflow dispatch with optional version override

**Purpose:**
- Publishes 8 provider packages to NuGet.org
- Creates GitHub releases with package artifacts
- Validates packages before publishing

**Package Structure:**
The MultiLock project publishes **8 provider packages**, each containing the core MultiLock framework embedded within:

1. **MultiLock.AzureBlobStorage** - Azure Blob Storage provider
2. **MultiLock.Consul** - Consul provider
3. **MultiLock.FileSystem** - File System provider
4. **MultiLock.InMemory** - In-Memory provider (for testing)
5. **MultiLock.PostgreSQL** - PostgreSQL provider
6. **MultiLock.Redis** - Redis provider
7. **MultiLock.SqlServer** - SQL Server provider
8. **MultiLock.ZooKeeper** - ZooKeeper provider

> **Note:** Users only need to install ONE provider package. The core framework is included automatically.

**Steps:**
1. Checkout code with full git history (required for MinVer)
2. Setup .NET 8.0 SDK
3. Install dotnet-validate tool
4. Restore dependencies
5. Build solution in Release mode
6. Run all tests to ensure quality
7. Pack NuGet packages
8. Display package information
9. Validate packages using dotnet-validate
10. Check for required metadata (license, repository)
11. Upload packages as artifacts
12. Push packages to NuGet.org (unless dry run)
13. Push symbol packages (.snupkg) for debugging
14. Create GitHub release with changelog

---

## Setup Instructions

### Required GitHub Secrets

To use the publish workflow, you need to configure the following secret in your GitHub repository:

1. **`NUGET_API_KEY`** - Your NuGet.org API key
   - Go to https://www.nuget.org/account/apikeys
   - Create a new API key with "Push" permission
   - Scope it to the MultiLock.* package pattern
   - Add it to GitHub: Settings → Secrets and variables → Actions → New repository secret

### Repository Permissions

The workflows require the following permissions (already configured in the workflow files):

- **contents: write** - To create GitHub releases
- **packages: write** - To publish packages
- **checks: write** - To publish test results
- **pull-requests: write** - To comment on PRs with test results

---

## How to Publish a New Release

### Option 1: Using Git Tags (Recommended)

This is the standard way to publish releases:

```bash
# 1. Ensure you're on the main branch and up to date
git checkout main
git pull

# 2. Create and push a version tag
git tag v1.0.0
git push origin v1.0.0

# The workflow will automatically:
# - Build and test the solution
# - Pack all 8 provider packages
# - Validate packages
# - Publish to NuGet.org
# - Create a GitHub release
```

**Version Tag Format:**
- Stable releases: `v1.0.0`, `v1.2.3`, `v2.0.0`
- Pre-releases: `v1.0.0-alpha.1`, `v1.0.0-beta.2`, `v1.0.0-rc.1`

MinVer will automatically use the tag to version the packages.

### Option 2: Manual Workflow Dispatch

For testing or special cases:

1. Go to **Actions** → **Publish to NuGet**
2. Click **Run workflow**
3. Choose the branch
4. (Optional) Enter a version number to override MinVer
5. (Optional) Enable "Dry run" to test without publishing
6. Click **Run workflow**

**Dry Run Mode:**
- Builds and validates packages
- Runs all tests
- Does NOT publish to NuGet.org
- Does NOT create GitHub releases
- Useful for testing the workflow

---

## Versioning Strategy

The project uses **MinVer** for automatic semantic versioning based on Git tags.

### How MinVer Works

- **No tags:** Version is `1.0.0-preview.0.{height}` (configured in `src/Directory.Build.props`)
- **Tag exists:** Version matches the tag (e.g., tag `v1.2.3` → version `1.2.3`)
- **Commits after tag:** Version is `1.2.4-preview.0.{commits-since-tag}`

### Version Configuration

See `src/Directory.Build.props`:
```xml
<MinVerMinimumMajorMinor>1.0</MinVerMinimumMajorMinor>
<MinVerDefaultPreReleaseIdentifiers>preview.0</MinVerDefaultPreReleaseIdentifiers>
```

### Pre-release Versions

To publish pre-release versions, use tags with pre-release identifiers:

```bash
# Alpha release
git tag v1.0.0-alpha.1
git push origin v1.0.0-alpha.1

# Beta release
git tag v1.0.0-beta.1
git push origin v1.0.0-beta.1

# Release candidate
git tag v1.0.0-rc.1
git push origin v1.0.0-rc.1
```

Pre-release packages will be marked as "pre-release" on NuGet.org and in GitHub releases.

---

## Package Validation

The workflow includes comprehensive package validation:

### Automated Checks

1. **dotnet-validate** - Official .NET package validation tool
   - Checks package structure
   - Validates metadata
   - Ensures compatibility

2. **Metadata verification**
   - License information present
   - Repository URL configured
   - Author information included

3. **Content verification**
   - DLL files included
   - XML documentation present (when configured)
   - README and LICENSE files

### Manual Verification

After publishing, verify packages on NuGet.org:
- Check package metadata displays correctly
- Verify README renders properly
- Test installation in a sample project
- Confirm all 8 packages published successfully

---

## Troubleshooting

### Workflow Fails on Test Step

**Problem:** Tests fail during the publish workflow.

**Solution:**
- The workflow will NOT publish if tests fail (by design)
- Fix the failing tests before attempting to publish
- Run tests locally: `dotnet test`
- Check test artifacts uploaded by the workflow

### Package Validation Fails

**Problem:** `dotnet validate package` reports errors.

**Solution:**
- Review the validation error messages
- Common issues:
  - Missing package metadata
  - Incorrect package structure
  - Missing dependencies
- Fix issues in .csproj files and retry

### NuGet Push Fails

**Problem:** Publishing to NuGet.org fails.

**Possible causes:**
1. **Invalid API key** - Regenerate and update the secret
2. **Package already exists** - Increment version number
3. **API key permissions** - Ensure "Push" permission is enabled
4. **Package ID conflict** - Check package naming

**Solution:**
- Check workflow logs for specific error
- Verify `NUGET_API_KEY` secret is set correctly
- Ensure API key has correct permissions and scope

### MinVer Version Incorrect

**Problem:** Packages have unexpected version numbers.

**Solution:**
- Ensure git tags are pushed: `git push --tags`
- Verify tag format: `v1.2.3` (must start with 'v')
- Check MinVer configuration in `src/Directory.Build.props`
- Use workflow dispatch with manual version override if needed

### Symbol Packages Not Published

**Problem:** .snupkg files not appearing on NuGet.org.

**Solution:**
- Ensure SourceLink is configured in .csproj files
- Check that symbol packages are generated during pack
- Verify symbol packages are in artifacts directory
- Symbol packages may take longer to process on NuGet.org

---

## Best Practices

### Before Publishing

1. ✅ Update CHANGELOG.md with release notes
2. ✅ Ensure all tests pass locally
3. ✅ Review package metadata in .csproj files
4. ✅ Test packages locally with `dotnet pack`
5. ✅ Verify version number is correct
6. ✅ Update README if needed

### After Publishing

1. ✅ Verify all 8 packages appear on NuGet.org
2. ✅ Check GitHub release was created
3. ✅ Test installation in a sample project
4. ✅ Announce release (social media, forums, etc.)
5. ✅ Monitor for issues or feedback

### Version Numbering

Follow [Semantic Versioning](https://semver.org/):

- **MAJOR** (1.0.0 → 2.0.0): Breaking changes
- **MINOR** (1.0.0 → 1.1.0): New features, backward compatible
- **PATCH** (1.0.0 → 1.0.1): Bug fixes, backward compatible

---

## Workflow Outputs

### Artifacts

The workflow uploads the following artifacts:

1. **nuget-packages** - All .nupkg files (retained for 30 days)
2. **symbol-packages** - All .snupkg files (retained for 30 days)

### GitHub Release

When triggered by a tag, creates a release with:
- Package files attached
- Auto-generated release notes
- Installation instructions
- Link to CHANGELOG.md

---

## Additional Resources

- [NuGet Package Best Practices](https://learn.microsoft.com/en-us/nuget/create-packages/package-authoring-best-practices)
- [MinVer Documentation](https://github.com/adamralph/minver)
- [GitHub Actions Documentation](https://docs.github.com/en/actions)
- [Semantic Versioning](https://semver.org/)

