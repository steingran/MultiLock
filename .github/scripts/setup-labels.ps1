# PowerShell script to set up GitHub labels for the MultiLock repository
# This script uses the GitHub CLI (gh) to create labels defined in labels.yml
# 
# Prerequisites:
# - GitHub CLI installed: https://cli.github.com/
# - Authenticated with GitHub: gh auth login
#
# Usage:
#   .\setup-labels.ps1

param(
    [string]$Repository = "steingran/MultiLock",
    [switch]$DryRun = $false
)

Write-Host "🏷️  GitHub Labels Setup Script" -ForegroundColor Cyan
Write-Host "================================" -ForegroundColor Cyan
Write-Host ""

# Check if gh CLI is installed
try {
    $ghVersion = gh --version
    Write-Host "✅ GitHub CLI found: $($ghVersion[0])" -ForegroundColor Green
} catch {
    Write-Host "❌ GitHub CLI not found. Please install it from https://cli.github.com/" -ForegroundColor Red
    exit 1
}

# Check if authenticated
try {
    $authStatus = gh auth status 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "❌ Not authenticated with GitHub. Please run: gh auth login" -ForegroundColor Red
        exit 1
    }
    Write-Host "✅ Authenticated with GitHub" -ForegroundColor Green
} catch {
    Write-Host "❌ Authentication check failed. Please run: gh auth login" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Repository: $Repository" -ForegroundColor Yellow
if ($DryRun) {
    Write-Host "Mode: DRY RUN (no changes will be made)" -ForegroundColor Yellow
} else {
    Write-Host "Mode: LIVE (labels will be created)" -ForegroundColor Yellow
}
Write-Host ""

# Define labels (from labels.yml)
$labels = @(
    # Type Labels
    @{name="feature"; color="0e8a16"; description="New feature or enhancement"},
    @{name="enhancement"; color="0e8a16"; description="Enhancement to existing functionality"},
    @{name="feat"; color="0e8a16"; description="New feature (conventional commits style)"},
    @{name="bug"; color="d73a4a"; description="Something is not working"},
    @{name="fix"; color="d73a4a"; description="Bug fix"},
    @{name="bugfix"; color="d73a4a"; description="Bug fix"},
    @{name="documentation"; color="0075ca"; description="Improvements or additions to documentation"},
    @{name="docs"; color="0075ca"; description="Documentation changes"},
    @{name="test"; color="fbca04"; description="Test improvements or additions"},
    @{name="testing"; color="fbca04"; description="Testing related changes"},
    @{name="performance"; color="ff6b6b"; description="Performance improvements"},
    @{name="perf"; color="ff6b6b"; description="Performance optimization"},
    @{name="chore"; color="fef2c0"; description="Maintenance tasks and chores"},
    @{name="maintenance"; color="fef2c0"; description="Repository maintenance"},
    @{name="dependencies"; color="0366d6"; description="Dependency updates"},
    @{name="deps"; color="0366d6"; description="Dependency changes"},
    @{name="security"; color="ee0701"; description="Security related changes"},
    @{name="refactor"; color="fbca04"; description="Code refactoring"},
    @{name="refactoring"; color="fbca04"; description="Code restructuring"},
    
    # Version Impact Labels
    @{name="major"; color="b60205"; description="Breaking changes - major version bump"},
    @{name="breaking"; color="b60205"; description="Breaking changes"},
    @{name="breaking-change"; color="b60205"; description="Introduces breaking changes"},
    @{name="minor"; color="0e8a16"; description="New features - minor version bump"},
    @{name="patch"; color="c5def5"; description="Bug fixes and patches - patch version bump"},
    
    # Workflow Labels
    @{name="skip-changelog"; color="ffffff"; description="Do not include in changelog"},
    @{name="no-changelog"; color="ffffff"; description="Exclude from changelog"},
    @{name="duplicate"; color="cfd3d7"; description="This issue or pull request already exists"},
    @{name="invalid"; color="e4e669"; description="This does not seem right"},
    @{name="wontfix"; color="ffffff"; description="This will not be worked on"},
    
    # Status Labels
    @{name="good first issue"; color="7057ff"; description="Good for newcomers"},
    @{name="help wanted"; color="008672"; description="Extra attention is needed"},
    @{name="question"; color="d876e3"; description="Further information is requested"},
    @{name="wip"; color="fbca04"; description="Work in progress"},
    @{name="ready for review"; color="0e8a16"; description="Ready for code review"},
    @{name="needs review"; color="fbca04"; description="Awaiting review"},
    @{name="approved"; color="0e8a16"; description="Approved and ready to merge"},
    @{name="blocked"; color="d93f0b"; description="Blocked by other work"},
    
    # Provider-Specific Labels
    @{name="provider/azure"; color="0078d4"; description="Azure Blob Storage provider"},
    @{name="provider/consul"; color="ca2171"; description="Consul provider"},
    @{name="provider/filesystem"; color="5319e7"; description="File System provider"},
    @{name="provider/inmemory"; color="bfdadc"; description="In-Memory provider"},
    @{name="provider/postgresql"; color="336791"; description="PostgreSQL provider"},
    @{name="provider/redis"; color="dc382d"; description="Redis provider"},
    @{name="provider/sqlserver"; color="cc2927"; description="SQL Server provider"},
    @{name="provider/zookeeper"; color="ff6b00"; description="ZooKeeper provider"},
    
    # Priority Labels
    @{name="priority/critical"; color="b60205"; description="Critical priority"},
    @{name="priority/high"; color="d93f0b"; description="High priority"},
    @{name="priority/medium"; color="fbca04"; description="Medium priority"},
    @{name="priority/low"; color="c5def5"; description="Low priority"}
)

Write-Host "📋 Creating $($labels.Count) labels..." -ForegroundColor Cyan
Write-Host ""

$created = 0
$skipped = 0
$errors = 0

foreach ($label in $labels) {
    $name = $label.name
    $color = $label.color
    $description = $label.description
    
    if ($DryRun) {
        Write-Host "[DRY RUN] Would create: $name" -ForegroundColor Gray
        $created++
    } else {
        try {
            # Try to create the label
            $result = gh label create $name --color $color --description $description --repo $Repository 2>&1
            
            if ($LASTEXITCODE -eq 0) {
                Write-Host "✅ Created: $name" -ForegroundColor Green
                $created++
            } else {
                # Label might already exist
                if ($result -match "already exists") {
                    Write-Host "⏭️  Skipped: $name (already exists)" -ForegroundColor Yellow
                    $skipped++
                } else {
                    Write-Host "❌ Error creating $name : $result" -ForegroundColor Red
                    $errors++
                }
            }
        } catch {
            Write-Host "❌ Error creating $name : $_" -ForegroundColor Red
            $errors++
        }
    }
}

Write-Host ""
Write-Host "================================" -ForegroundColor Cyan
Write-Host "Summary:" -ForegroundColor Cyan
Write-Host "  Created: $created" -ForegroundColor Green
Write-Host "  Skipped: $skipped" -ForegroundColor Yellow
Write-Host "  Errors:  $errors" -ForegroundColor $(if ($errors -gt 0) { "Red" } else { "Green" })
Write-Host ""

if ($DryRun) {
    Write-Host "ℹ️  This was a dry run. Run without -DryRun to actually create labels." -ForegroundColor Yellow
} else {
    Write-Host "✅ Label setup complete!" -ForegroundColor Green
    Write-Host ""
    Write-Host "Next steps:" -ForegroundColor Cyan
    Write-Host "1. Review labels in GitHub: https://github.com/$Repository/labels" -ForegroundColor White
    Write-Host "2. Start using labels on Pull Requests for automatic changelog generation" -ForegroundColor White
    Write-Host "3. See .github/PULL_REQUEST_TEMPLATE.md for label guidelines" -ForegroundColor White
}


