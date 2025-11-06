# Release Drafter Guide

This guide explains how the MultiLock project uses Release Drafter for automatic changelog generation and release management.

---

## Overview

**Release Drafter** automatically generates release notes and changelogs based on Pull Requests merged into the `main` branch. It categorizes changes using PR labels and maintains a draft release that's updated as PRs are merged.

### Benefits

✅ **Automatic changelog generation** - No manual CHANGELOG.md updates needed  
✅ **Consistent release notes** - Standardized format across all releases  
✅ **PR-based workflow** - Works naturally with GitHub's PR workflow  
✅ **No commit format required** - Unlike conventional commits, no special commit message format needed  
✅ **Automatic categorization** - Changes grouped by type (features, bugs, docs, etc.)  
✅ **Version suggestions** - Suggests next version based on PR labels  

---

## How It Works

### 1. Pull Request Created/Updated

When a PR is opened or updated:
- Release Drafter workflow runs automatically
- PR is analyzed for labels
- Draft release is updated with the PR information

### 2. Pull Request Merged

When a PR is merged to `main`:
- Draft release is updated to include the merged PR
- Changes are categorized based on PR labels
- Next version is calculated based on labels

### 3. Release Published

When you create a Git tag (e.g., `v1.0.0`):
- Publish workflow runs
- Release Drafter publishes the draft release
- CHANGELOG.md is automatically updated
- NuGet packages are published

---

## PR Labels Guide

### Type Labels (for categorization)

| Label | Category | Description |
|-------|----------|-------------|
| `feature`, `enhancement`, `feat` | 🚀 Features | New features or enhancements |
| `bug`, `fix`, `bugfix` | 🐛 Bug Fixes | Bug fixes |
| `documentation`, `docs` | 📚 Documentation | Documentation changes |
| `test`, `testing` | 🧪 Tests | Test improvements |
| `performance`, `perf` | ⚡ Performance | Performance improvements |
| `chore`, `maintenance` | 🔧 Maintenance | Maintenance tasks |
| `dependencies`, `deps` | 📦 Dependencies | Dependency updates |
| `security` | 🔒 Security | Security fixes |
| `refactor`, `refactoring` | ♻️ Refactoring | Code refactoring |

### Version Impact Labels (for semantic versioning)

| Label | Version Bump | Use When |
|-------|--------------|----------|
| `major`, `breaking`, `breaking-change` | 1.0.0 → 2.0.0 | Breaking changes |
| `minor`, `feature`, `enhancement` | 1.0.0 → 1.1.0 | New features (backward compatible) |
| `patch`, `fix`, `bugfix`, `docs`, `chore` | 1.0.0 → 1.0.1 | Bug fixes, patches |

### Workflow Labels

| Label | Purpose |
|-------|---------|
| `skip-changelog`, `no-changelog` | Exclude from changelog |
| `duplicate` | Duplicate PR/issue |
| `invalid` | Invalid PR |
| `wontfix` | Will not be fixed |

---

## Setting Up Labels

### Option 1: Using PowerShell Script (Recommended)

```powershell
# From the repository root, run the setup script
.\.github\scripts\setup-labels.ps1

# Or dry run first to see what would be created
.\.github\scripts\setup-labels.ps1 -DryRun
```

### Option 2: Using GitHub CLI Manually

```bash
# Example: Create a feature label
gh label create feature --color 0e8a16 --description "New feature or enhancement"

# Create a bug label
gh label create bug --color d73a4a --description "Something is not working"
```

### Option 3: Using GitHub Web UI

1. Go to https://github.com/steingran/MultiLock/labels
2. Click "New label"
3. Enter name, description, and color
4. Click "Create label"

---

## Creating Pull Requests

### Step 1: Create Your PR

```bash
# Create a feature branch
git checkout -b feature/add-etcd-provider

# Make your changes
# ... code changes ...

# Commit and push
git add .
git commit -m "Add etcd provider support"
git push origin feature/add-etcd-provider
```

### Step 2: Open PR on GitHub

1. Go to https://github.com/steingran/MultiLock/pulls
2. Click "New pull request"
3. Select your branch
4. Fill in the PR template
5. **Add appropriate labels** (this is crucial!)

### Step 3: Add Labels

**For a new feature:**
- Add `feature` or `enhancement`
- Add `minor` (for version bump)
- Optionally add provider-specific label (e.g., `provider/redis`)

**For a bug fix:**
- Add `bug` or `fix`
- Add `patch` (for version bump)

**For documentation:**
- Add `documentation` or `docs`
- Add `patch` (for version bump)

**For breaking changes:**
- Add `breaking` or `major`
- Add `major` (for version bump)

### Step 4: Review and Merge

1. Wait for CI checks to pass
2. Get code review approval
3. Merge the PR
4. Release Drafter automatically updates the draft release!

---

## Viewing Draft Releases

1. Go to https://github.com/steingran/MultiLock/releases
2. Look for the draft release at the top
3. You'll see all merged PRs categorized by type
4. Version number is suggested based on PR labels

---

## Publishing a Release

### Step 1: Review Draft Release

1. Check the draft release notes
2. Verify all PRs are included
3. Confirm version number is correct
4. Edit release notes if needed (optional)

### Step 2: Create and Push Tag

```bash
# Ensure you're on main and up to date
git checkout main
git pull

# Create version tag (use the version from draft release)
git tag v1.0.0

# Push the tag
git push origin v1.0.0
```

### Step 3: Automatic Publishing

The publish workflow will automatically:
1. ✅ Build and test the solution
2. ✅ Pack NuGet packages
3. ✅ Validate packages
4. ✅ Publish to NuGet.org
5. ✅ Publish the draft release (using Release Drafter)
6. ✅ Update CHANGELOG.md
7. ✅ Commit CHANGELOG.md back to main

---

## CHANGELOG.md Updates

CHANGELOG.md is automatically updated when a release is published:

1. Release notes are fetched from the published GitHub release
2. New entry is prepended to CHANGELOG.md
3. File is committed back to the `main` branch
4. Commit message: `chore: update CHANGELOG.md for vX.Y.Z [skip ci]`

**Note:** The `[skip ci]` tag prevents the commit from triggering another CI build.

---

## Customizing Release Notes

### Editing the Template

Edit `.github/release-drafter.yml` to customize:

```yaml
template: |
  ## What's Changed
  
  $CHANGES
  
  ## Custom Section
  
  Your custom content here...
```

### Adding New Categories

Add to the `categories` section:

```yaml
categories:
  - title: '🎨 UI/UX'
    labels:
      - 'ui'
      - 'ux'
      - 'design'
```

### Changing Version Resolution

Modify the `version-resolver` section:

```yaml
version-resolver:
  major:
    labels:
      - 'major'
      - 'breaking'
  minor:
    labels:
      - 'minor'
      - 'feature'
  patch:
    labels:
      - 'patch'
      - 'fix'
  default: patch
```

---

## Auto-Labeling

Release Drafter can automatically add labels based on:

### File Paths

```yaml
autolabeler:
  - label: 'documentation'
    files:
      - '*.md'
      - 'docs/**/*'
```

### Branch Names

```yaml
autolabeler:
  - label: 'bug'
    branch:
      - '/fix\/.+/'
```

### PR Titles

```yaml
autolabeler:
  - label: 'feature'
    title:
      - '/feat/i'
```

---

## Best Practices

### For Contributors

1. ✅ **Always add labels to PRs** - This ensures proper categorization
2. ✅ **Use descriptive PR titles** - They appear in release notes
3. ✅ **Add version impact labels** - Helps determine next version
4. ✅ **Review the PR template** - It guides you through the process
5. ✅ **Check draft release** - Verify your PR appears correctly

### For Maintainers

1. ✅ **Review draft releases regularly** - Ensure quality
2. ✅ **Edit release notes if needed** - Add context or highlights
3. ✅ **Verify version numbers** - Confirm semantic versioning is correct
4. ✅ **Test before tagging** - Ensure main branch is stable
5. ✅ **Announce releases** - Share with community

---

## Troubleshooting

### PR Not Appearing in Draft Release

**Problem:** Merged PR doesn't show in draft release.

**Solutions:**
- Check if PR has `skip-changelog` label
- Verify PR was merged to `main` branch
- Check workflow logs: https://github.com/steingran/MultiLock/actions
- Manually trigger workflow: Actions → Release Drafter → Run workflow

### Wrong Version Number

**Problem:** Draft release suggests incorrect version.

**Solutions:**
- Check PR labels - version is based on labels
- Add correct version impact label (`major`, `minor`, or `patch`)
- Manually edit version in draft release before publishing

### CHANGELOG.md Not Updated

**Problem:** CHANGELOG.md not updated after release.

**Solutions:**
- Check publish workflow logs
- Verify tag was pushed correctly
- Check for errors in "Update CHANGELOG.md" step
- Manually update if needed and commit

### Duplicate Entries in CHANGELOG.md

**Problem:** Same release appears twice in CHANGELOG.md.

**Solutions:**
- This shouldn't happen with the current setup
- If it does, manually edit CHANGELOG.md to remove duplicate
- Report as an issue

---

## Examples

### Example 1: Feature PR

```markdown
Title: Add connection pooling to Redis provider

Labels:
- feature
- minor
- provider/redis
- performance

Result in Release Notes:
🚀 Features
- Add connection pooling to Redis provider @username (#123)
```

### Example 2: Bug Fix PR

```markdown
Title: Fix race condition in PostgreSQL leader election

Labels:
- bug
- patch
- provider/postgresql

Result in Release Notes:
🐛 Bug Fixes
- Fix race condition in PostgreSQL leader election @username (#124)
```

### Example 3: Breaking Change PR

```markdown
Title: Remove deprecated API methods

Labels:
- breaking
- major

Result in Release Notes:
💥 Breaking Changes
- Remove deprecated API methods @username (#125)
```

---

## Additional Resources

- [Release Drafter Documentation](https://github.com/release-drafter/release-drafter)
- [Semantic Versioning](https://semver.org/)
- [Keep a Changelog](https://keepachangelog.com/)
- [GitHub Labels Best Practices](https://docs.github.com/en/issues/using-labels-and-milestones-to-track-work/managing-labels)

---

## Support

If you have questions or issues with Release Drafter:

1. Check this guide
2. Review workflow logs
3. Check Release Drafter documentation
4. Open an issue on GitHub

