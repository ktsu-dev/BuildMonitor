# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build Commands

```bash
# Build the solution
dotnet build

# Run the application
dotnet run --project BuildMonitor/BuildMonitor.csproj

# Clean build artifacts
dotnet clean
```

## Architecture Overview

BuildMonitor is a desktop ImGui-based application that monitors CI/CD build statuses across multiple providers (GitHub Actions and Azure DevOps). The architecture follows a hierarchical data model with concurrent updating, priority-based scheduling, and adaptive rate limiting.

### Core Data Hierarchy

The application maintains a nested hierarchy of build data:

- **BuildProvider** (e.g., GitHub, AzureDevOps) → **Owner** → **Repository** → **Build** → **Run**

Each level is stored in `ConcurrentDictionary` collections to support thread-safe updates from async operations.

**Owner Semantics by Provider:**
- **GitHub**: Owners represent GitHub users or organizations
- **Azure DevOps**: Owners represent projects within an organization (the account ID is the organization name)

### Dual-Entity Pattern with Priority Scheduling

The codebase uses a "sync" pattern where domain entities (Build, Run) are paired with synchronization classes:

- **Build** / **BuildSync**: The Build entity stores data; BuildSync manages polling intervals, priority calculation, and async updates
- **Run** / **RunSync**: Same pattern for individual workflow runs

BuildSync and RunSync objects track when entities should be updated based on their state and calculate request priorities:
- **High Priority (0)**: Running/in-progress builds (users care most about these)
- **Medium Priority (1)**: Recent failures or builds with recent activity (within last hour)
- **Low Priority (2)**: Completed/successful builds, discovery operations

Priority affects both update frequency and whether requests are made when API budget is constrained.

### Update Flow with Adaptive Intervals

1. **Main Loop** (BuildMonitor.cs): Calls `UpdateAsync()` every frame
2. **Provider Refresh** (every 300s): Updates repositories and builds from providers
   - Skipped when provider is in low budget mode (< 15% remaining API calls)
   - Logs: "Skipping discovery due to low budget"
3. **Build Updates** (30-120s, priority-based):
   - High priority: 30s interval
   - Medium priority: 60s interval
   - Low priority: 120s interval
   - Filtered by provider's `MaxAllowedPriority` based on budget
4. **Run Updates** (10-60s, adaptive): Updates only ongoing runs
   - Dynamically adjusts interval based on estimated time remaining (ETA)
   - Clamped between 10s minimum and 60s maximum
   - Only high-priority requests (ongoing runs)

The update cycle creates BuildSync/RunSync entries in global collections (`BuildSyncCollection`, `RunSyncCollection`). Each sync object:
- Tracks elapsed time with a `Stopwatch`
- Calculates whether it should update via `ShouldUpdate` property
- Determines its priority via `Priority` property
- Is pruned when orphaned (parent entity deleted) or completed (runs only)

### Provider Architecture

BuildProvider is an abstract base class with JSON polymorphic serialization support. Currently supported providers:
- **GitHub**: Uses Octokit library for GitHub Actions API
- **AzureDevOps**: Uses Microsoft.TeamFoundation libraries for Azure DevOps REST API

New providers should:
- Inherit from BuildProvider
- Add `[JsonDerivedType]` attribute to BuildProvider base class
- Implement the four abstract update methods:
  - `UpdateRepositoriesAsync(Owner)`: Discover/update repositories for an owner
  - `UpdateBuildsAsync(Repository)`: Discover/update build definitions (workflows) for a repository
  - `UpdateBuildAsync(Build)`: Fetch recent runs for a specific build
  - `UpdateRunAsync(Run)`: Update the status of a specific run
- Register in `BuildMonitor.OnStart()` by adding to `AppData.BuildProviders`
- Implement provider-specific menu actions via `ShowMenu()` override

### Entity Update Strategy

The application uses a **GetOrAdd pattern** to ensure existing entities are updated when new data arrives, preventing data loss and keeping properties in sync with the remote APIs.

**Repository Updates:**
- Uses `ConcurrentDictionary.GetOrAdd()` instead of `TryAdd()` to get existing or create new
- Always updates mutable properties after getting the entity:
  - `IsPrivate`: Updated when repository visibility changes
  - `IsArchived`: Updated when repository is archived/unarchived
  - `IsFork`: Updated when repository fork status changes
- Only triggers `QueueSaveAppData()` when actual changes occur
- Logs separately: new repositories, updated repositories, archived repositories removed

**Build Updates:**
- Uses `GetOrAdd()` pattern for build/workflow definitions
- Updates `BuildName` if the workflow file or build definition is renamed
- Detects changes by comparing current value with API value
- Only saves when new builds are discovered or names change

**Run Updates:**
- Run entities are always updated in place (no GetOrAdd needed)
- `UpdateRunFromWorkflowAsync()` (GitHub) and `UpdateRunFromBuild()` (Azure DevOps) always update properties
- Updates: Status, Started, LastUpdated, Duration, Branch, Errors
- Errors are cleared when run status changes from failure to non-failure
- Triggers `Build.UpdateFromRun()` to keep parent build state in sync

**Benefits of Update Strategy:**
- Repository visibility changes are reflected without manual intervention
- Renamed workflows/build definitions don't create duplicate entries
- No data accumulation from stale entities
- Efficient AppData saves (only when changes occur)
- Better logging distinguishes between discovery and updates

### Provider Status Tracking

Each BuildProvider tracks its operational status with visual indicators in the status bar:

- **ProviderStatus.OK** (green): Operating normally
- **ProviderStatus.RateLimited** (yellow): API rate limit hit, includes backoff delay and reset time
- **ProviderStatus.AuthFailed** (red): Authentication failed, credentials cleared
- **ProviderStatus.Error** (magenta): Connection or other error

Status is displayed in a status bar at the top of the UI with:
- Color indicator showing status
- Provider name and status label
- Rate limit consumption display (e.g., "4532/5000")
- Tooltip with detailed status message and rate limit reset time

### Rate Limit Management and Adaptive Pacing

The application implements sophisticated rate limit management to avoid hitting API limits:

**Budget Tracking:**
- Tracks remaining API calls (`RateLimitBudgetRemaining`) and total limit (`RateLimitBudgetLimit`)
- Updates budget from successful API responses via `UpdateRateLimitBudget()`
- Calculates budget percentage: `BudgetPercentage` (0.0 to 1.0)

**Budget Thresholds:**
- **Low Budget Mode** (< 15% remaining): Skips low-priority requests and discovery operations
- **Critical Budget Mode** (< 5% remaining): Only processes high-priority requests (running builds)
- `MaxAllowedPriority` returns the maximum priority level allowed given current budget

**Adaptive Pacing:**
- `CalculateAdaptivePacing()`: Spreads remaining requests evenly across the time window until reset
- Reserves 10% of budget (or 50 requests minimum) for unexpected requests
- Calculates delay per request: `timeUntilReset / usableBudget * 1.1` (with 10% safety margin)
- Clamped between 100ms minimum and 30s maximum

**Wait Time Calculation:**
- `GetRateLimitWaitTime()`: Returns the appropriate delay before the next request
- When fully rate limited: waits until reset time (capped at 10 minutes)
- Otherwise: uses the larger of adaptive pacing delay or current `RateLimitSleep`
- Includes 5-second buffer for clock skew when waiting for reset

**Rate Limit Recovery:**
- `ClearStatus()`: Resets `RateLimitSleep` back to base delay (500ms) when recovering
- Status automatically clears on first successful request after rate limiting

### Authentication and Credentials

**Provider-Level Authentication:**
- Each provider has an `AccountId` and `Token` (stored in AppData, persisted)
- Set via "Set Credentials" menu item (two-step popup: AccountId, then Token)
- Cleared automatically on `AuthorizationException` or 403 Forbidden responses

**Owner-Level Authentication (GitHub only):**
- Each owner can have an optional `Token` property (overrides provider token)
- Enables access to private repositories in different organizations
- Set via "Providers → GitHub → Set Owner Token" submenu
- Clear via "Providers → GitHub → Set Owner Token → Clear Owner Token" submenu
- Owner names in the menu show "(token)" indicator if owner has a token
- `HasValidCredentials(owner)` checks owner token first, then falls back to provider token

**GitHub Client Management:**
- Maintains cache of `GitHubClient` instances per token (`TokenClients` dictionary)
- Uses `AsyncLocal<GitHubClient>` for current client context (`CurrentClientLocal`)
- `SetCurrentClient(owner)` must be called before each API operation
- Prevents race conditions when concurrent requests use different owner tokens

**Azure DevOps Client Management:**
- Creates `VssConnection` with `VssBasicCredential` using AccountId and Token
- Gets `ProjectHttpClient` and `BuildHttpClient` from connection
- `UpdateAzureDevOpsClientCredentials()` recreates clients when credentials change
- Organization URL format: `https://dev.azure.com/{AccountId}`

### Threading and Synchronization

- All data updates happen asynchronously via Task-based operations
- `ConcurrentDictionary` is used throughout for thread-safe collection access
- `BuildMonitor.SyncLock` exists but is only used for menu rendering
- `RequestSemaphore` limits concurrent requests per provider (default: 5)
- Rate limiting uses adaptive delays calculated per request via `GetRateLimitWaitTime()`
- Active requests are tracked in `ActiveRequests` dictionary for UI feedback

### User Interface

**Multi-Tab Layout:**
The UI uses a tabbed interface (`OwnerTabPanel`) with dynamic tab management:
- **"All" Tab**: Shows builds from all owners across all providers
- **Owner/Project Tabs**:
  - GitHub: One tab per owner (user or organization)
  - Azure DevOps: One tab per account (the organization itself)
  - Tab IDs format: `"GitHub:{ownerName}"` or `"ADO:{accountId}"`
- **"Logs" Tab**: Application logs with color-coded levels and auto-scrolling

Tabs are dynamically created/removed as owners are discovered or removed. Selected tab is persisted in AppData.

**Build Table Columns:**
1. **Status** (60px): Color indicator or radial progress bar
2. **Owner** (150px): Owner/organization/project name
3. **Repository** (150px): Repository name with property icons
4. **Build Name** (200px): Workflow/build definition name (cleaned of `.yml` and path prefixes)
5. **Branch** (150px): Git branch name
6. **Status** (80px): Text status (Pending, Running, Success, Failure, Canceled)
7. **Last Run** (180px): Timestamp of last run start (local time with timezone)
8. **Duration** (80px): Duration of the run (format: `hh:mm:ss` or `d.hh:mm:ss`)
9. **Estimate** (80px): Estimated total duration for the build
10. **History** (80px): Last 5 non-canceled runs as color indicators
11. **Progress** (100px): Radial progress bar + percentage (ongoing builds only)
12. **ETA** (80px): Estimated time remaining (ongoing builds only)
13. **Errors** (200px): Error messages from failed runs (clickable for details)
14. **Next Update** (100px): Countdown to next poll with radial progress indicator

**Repository Icons:**
Repositories display nerd font icons to indicate properties:
- `\uf023` (lock): Private repository
- `\uf126` (code fork): Forked repository
- `\uf187` (archive): Archived repository

**Empty Repositories:**
Repositories without workflows/builds are shown with:
- Gray status indicator
- Owner and repository name (with property icons)
- "No workflows" text in gray in the Build Name column
- Hidden when any build name, branch, or status filter is active

**Progress Indicators:**
The UI uses radial progress bars (from `ImGuiWidgets.RadialProgressBar`) for:
- **Build Status Column**: Ongoing builds show progress instead of color indicator
- **Progress Column**: Radial bar + percentage text for ongoing builds
- **Next Update Column**: Countdown visualization showing time until next poll

**Filtering:**
- Filter row below table headers with search boxes for each column
- Supports multiple filter types (via `TextFilterType`): Contains, Exact, Wildcard, Regex
- Match options: Case-sensitive, whole word, etc.
- Filters are persisted per column in AppData
- Empty repositories hidden when build name, branch, or status filters are active

**Column Width Management:**
- Widths are persisted in `AppData.ColumnWidths` dictionary
- Default widths defined in `DefaultColumnWidths`
- Manual pointer arithmetic used to read column widths from ImGui's native structs
- Workaround for Hexa.NET.ImGui struct layout bug (8-byte size difference)
- `SaveColumnWidth()` saves when width changes by more than 1px

### Context Menu Actions

Right-clicking on any build row opens a context menu with the following actions:

**Repository Actions:**
- Open Repository in Browser
- Copy Repository URL

**Workflow Actions:**
- Open Workflow in Browser
- Copy Workflow URL

**Branch Actions:**
- Open Branch in Browser
- Copy Branch URL

**Latest Run Actions:**
- Open Latest Run in Browser
- Copy Latest Run URL

**GitHub API Actions** (GitHub provider only):
- **Re-run Latest Workflow**: Re-runs a completed workflow (disabled for running builds)
- **Cancel Running Workflow**: Cancels an in-progress workflow (only shown for running builds)
- **Trigger Workflow on Branch**: Dispatches a new workflow run on the selected branch

**Data Refresh:**
- Refresh Build Data: Forces immediate update by calling `BuildSync.ResetTimer()`

All actions that modify build state (rerun, cancel, trigger) use `ExecuteGitHubApiAction()` which:
- Runs the action asynchronously in a background task
- Automatically triggers a build refresh on success
- Logs but does not crash on `ApiException` failures

### Logs Tab

The Logs tab provides a real-time view of application logs:

**Features:**
- Color-coded log levels:
  - Debug: Gray
  - Info: White
  - Warning: Yellow
  - Error: Red
- Format: `[HH:mm:ss.fff] [LEVEL] message`
- Auto-scrolling: Stays at bottom when already scrolled to bottom
- Clear logs button at top
- Scrollable region with horizontal scrollbar support

**Log Sources:**
- Provider operations (authentication, rate limiting, errors)
- API requests (start/completion with duration)
- Build status transitions (started, succeeded, failed)
- Discovery operations (owners, repositories, builds)
- Budget management decisions

### Error Display

Failed runs display error information fetched from provider-specific job logs:

**GitHub Error Fetching:**
- Errors are fetched via `FetchRunErrorsAsync()` when a run transitions to failure state or is a failure without errors
- Job logs retrieved via `GitHubJobs.GetLogs()`
- Parsed with `ParseLogForErrors()` using:
  - GitHub Actions error annotations: `##[error]message`
  - Common error patterns: regex `(?:^|\s)error\s*:` (case-insensitive)
- Errors are deduplicated using `HashSet<string>` (case-insensitive)
- Limited to first 10 errors to avoid UI overload
- Prefixed with job name: `[{job.Name}] {errorMessage}`
- Falls back to failed step names if no log errors found
- Further falls back to `[{job.Name}] Failed` if no steps failed

**UI Display:**
- Errors shown in red text in the Errors column
- Text is ellipsized if longer than column width (with "..." suffix)
- Clickable to open a popup with full error details
- Popup shows build name, branch, and all errors with word wrapping
- Tooltip on hover shows full error text
- Errors cleared when run status changes from failure to non-failure

### Duration Estimation

Build duration estimation uses sophisticated statistical methods via `DurationEstimator`:

**Estimation Strategy:**
1. **Sample Selection**: Uses last 20 successful runs, ordered by start time (most recent first)
2. **Branch-Specific Estimation**: When estimating for a branch:
   - First attempts estimate using only runs from that branch
   - Falls back to overall build estimation if branch has insufficient data (< 3 samples)
3. **Outlier Removal**: Uses IQR (Interquartile Range) method with 1.5× multiplier (Tukey fence)
   - Calculates Q1 (25th percentile) and Q3 (75th percentile)
   - Removes values outside `[Q1 - 1.5*IQR, Q3 + 1.5*IQR]`
   - Falls back to median if too many samples removed (< 3 remaining)
4. **Exponentially Weighted Average**: Recent runs have higher weight
   - Weight decreases by 70% for each older sample (decay factor: 0.3)
   - Most recent run has weight 1.0, next has 0.7, next has 0.49, etc.
   - Formula: `weight = (1 - decayFactor)^i` where i is age index

**Branch-Specific Estimation:**
- `Build.CalculateEstimatedDuration(branch)`: Estimates for a specific branch
- `Build.CalculateEstimatedDuration()`: Estimates across all branches
- Run ETA calculation uses branch-specific estimation: `Run.CalculateETA()` calls `Build.CalculateEstimatedDuration(Branch)`
- More accurate than global estimates for workflows that vary significantly by branch

**Estimation Statistics:**
- `DurationEstimator.GetEstimationStats()`: Returns detailed statistics for debugging
- Statistics include: sample count, min, max, median, final estimate, whether filtering was used

**Usage:**
- Used by UI to show estimated duration in "Estimate" column
- Used to calculate ETA (Estimated Time Remaining) for ongoing runs
- Drives adaptive update intervals in `RunSync.UpdateAsync()`

### Menu System

**File Menu:**
- **Clear Data**: Clears all repositories and builds for all providers (preserves provider credentials)
- **Exit**: Closes the application

**Providers Menu:**
Each provider has a submenu with provider-specific actions:

**GitHub Provider Menu:**
- **Set Credentials**: Two-step popup for AccountId and Token
- **Discover All Owners**: Automatically discovers user and all organizations
  - Fetches authenticated user via `CurrentClient.User.Current()`
  - Fetches all organizations via `CurrentClient.Organization.GetAllForCurrent()`
  - Adds each as an owner (user and orgs)
- **Add Owner**: Manually add an owner by name
- **Set Owner Token** (submenu): Per-owner token management
  - Lists all owners, shows "(token)" indicator for owners with tokens
  - Opens popup to set token for selected owner
  - **Clear Owner Token** (nested submenu): Remove token from selected owner

**Azure DevOps Provider Menu:**
- **Set Credentials**: Two-step popup for AccountId (organization name) and Token (PAT)
- **Discover All Projects**: Discovers all projects in the organization
  - Fetches projects via `ProjectClient.GetProjects()`
  - Creates an owner entry for each project
  - Creates a repository entry (project acts as both owner and repository)
- **Add Owner**: Manually add a project by name

### State Management and Persistence

**AppData Structure:**
Persisted via `ktsu.AppDataStorage` to JSON file:
- `WindowState`: ImGui window position, size, and state
- `BuildProviders`: Dictionary of provider instances (serialized polymorphically)
- `ColumnWidths`: Dictionary of column name to width
- `SelectedOwnerTabId`: Currently selected tab ID
- Filter settings per column (text, type, match options):
  - `FilterOwner`, `FilterOwnerType`, `FilterOwnerMatchOptions`
  - `FilterRepository`, `FilterRepositoryType`, `FilterRepositoryMatchOptions`
  - `FilterBuildName`, `FilterBuildNameType`, `FilterBuildNameMatchOptions`
  - `FilterBranch`, `FilterBranchType`, `FilterBranchMatchOptions`
  - `FilterStatus`, `FilterStatusType`, `FilterStatusMatchOptions`

**Save Batching:**
- `QueueSaveAppData()`: Marks data for saving without immediate write
- `SaveSettingsIfRequired()`: Called each frame, saves if queued
- Batches multiple changes within a single frame into one write

**Data Cleared on Auth Failure:**
- Provider `AccountId` and `Token` cleared on authentication failure
- Provider status set to `AuthFailed`
- Owner tokens preserved (only provider token cleared)

### Semantic String Types

The codebase uses `ktsu.Semantics.Strings` for type-safe string identifiers:

- `BuildProviderName`, `BuildProviderAccountId`, `BuildProviderToken`
- `OwnerName`, `OwnerId`
- `RepositoryName`, `RepositoryId`
- `BuildName`, `BuildId`
- `RunName`, `RunId`
- `BranchName`

These prevent mixing up different types of identifiers and enable type-safe conversions via `.As<T>()`.

**Example:**
```csharp
OwnerName ownerName = "microsoft".As<OwnerName>();
BuildId buildId = workflowId.ToString().As<BuildId>();
```

## Key Files

### Core Application
- **BuildMonitor.cs**: Main application class with UI rendering, update orchestration, and tab management
- **AppData.cs**: Persistent application state and settings

### Provider System
- **BuildProvider.cs**: Abstract base class with status tracking, rate limiting, and budget management
- **Providers/GitHub.cs**: GitHub Actions implementation with Octokit API integration
- **Providers/AzureDevOps.cs**: Azure DevOps implementation with Microsoft.TeamFoundation libraries

### Data Model
- **Owner.cs**: Represents a user/organization (GitHub) or project (Azure DevOps)
- **Repository.cs**: Represents a repository with properties (IsPrivate, IsArchived, IsFork)
- **Build.cs**: Represents a build definition/workflow with duration estimation
- **Run.cs**: Represents a workflow run with status and error information

### Synchronization
- **BuildSync.cs**: Synchronization wrapper for Build with priority-based update scheduling
- **RunSync.cs**: Synchronization wrapper for Run with adaptive update intervals
- **RequestPriority.cs**: Priority enum and BuildSync class with priority logic

### Estimation
- **DurationEstimator.cs**: Statistical duration estimation with IQR outlier removal and exponential weighting

## Provider Implementation Details

### GitHub Provider

**API Client:**
- Uses Octokit library (`Octokit` NuGet package)
- Maintains per-token client cache to avoid race conditions
- Creates `GitHubClient` with `ProductHeaderValue` and `Credentials`
- Token format: Personal Access Token (PAT) with `repo` and `workflow` scopes

**Repository Discovery:**
- For authenticated user: `GitHubRepository.GetAllForCurrent()` (includes private repos)
  - Filters to only repos owned by the user (not all accessible repos)
- For other users: `GitHubRepository.GetAllForUser(owner)` (public repos only)
- For organizations: `GitHubRepository.GetAllForOrg(owner)` (respects org visibility)
- Uses `GetOrAdd()` to get existing or create new repository
- Updates properties: `IsPrivate`, `IsArchived`, `IsFork` on each refresh
- Archived repositories are removed from tracking
- Logs: `"{newRepos} new, {updatedRepos} updated, {archivedRepos} archived removed"`

**Workflow/Build Discovery:**
- `GitHubWorkflows.List(owner, repo)`: Gets all workflows in repository
- Uses `GetOrAdd()` to get existing or create new build
- Updates `BuildName` if workflow file is renamed
- Maps `Workflow.Id` to `BuildId` and `Workflow.Name` to `BuildName`

**Run Fetching:**
- `GitHubRuns.ListByWorkflow()`: Gets last 10 runs for a workflow
- Maps workflow run status/conclusion to `RunStatus` enum
- Logs status transitions (started, succeeded, failed)

**Run Updates:**
- `GitHubRuns.Get()`: Fetches single run by ID
- Updates on status change: Pending → Running → Success/Failure/Canceled

**Error Fetching:**
- `GitHubJobs.List()`: Gets all jobs for a run
- `GitHubJobs.GetLogs()`: Gets log text for each failed job
- Parses logs for `##[error]` annotations and error patterns

**Rate Limit Handling:**
- Updates budget from `ApiInfo.RateLimit` after each successful request
- Detects rate limits via 403 Forbidden + `X-RateLimit-Remaining: 0` header
- Parses reset time from `X-RateLimit-Reset` header (Unix timestamp)
- Distinguishes 403 rate limit from 403 auth failure by checking headers

**GitHub-Specific Actions:**
- `RerunWorkflowAsync()`: `GitHubRuns.Rerun()`
- `CancelWorkflowAsync()`: `GitHubRuns.Cancel()`
- `TriggerWorkflowAsync()`: `GitHubActions.Workflows.CreateDispatch()` with branch reference

### Azure DevOps Provider

**API Client:**
- Uses Microsoft.TeamFoundation libraries (`Microsoft.TeamFoundationServer.Client`, etc.)
- Creates `VssConnection` with organization URI: `https://dev.azure.com/{AccountId}`
- Uses `VssBasicCredential` with empty username and Personal Access Token (PAT)
- Gets `ProjectHttpClient` for project/repository operations
- Gets `BuildHttpClient` for build definition and run operations

**Project/Owner Discovery:**
- `ProjectClient.GetProjects()`: Gets all projects in the organization
- Each project becomes an owner (project name → `OwnerName`)
- Each project also creates a repository entry (project acts as both)
- Uses `GetOrAdd()` to ensure repository entry exists for each project
- Repository ID uses project GUID

**Build Definition Discovery:**
- `BuildClient.GetDefinitionsAsync(projectName)`: Gets all build definitions in project
- Uses `GetOrAdd()` to get existing or create new build
- Updates `BuildName` if build definition is renamed
- Maps `BuildDefinitionReference.Id` to `BuildId`
- Maps `BuildDefinitionReference.Name` to `BuildName`
- Logs when new build definitions are discovered

**Build/Run Fetching:**
- `BuildClient.GetBuildsAsync(project, definitions, top)`: Gets last 10 builds for a definition
- Maps `Build.Id` to `RunId` and `Build.BuildNumber` to `RunName`
- Branch name extracted from `SourceBranch` (removes `refs/heads/` prefix)

**Run Updates:**
- `BuildClient.GetBuildAsync(project, buildId)`: Fetches single build by ID
- Uses `StartTime` or falls back to `QueueTime` for run start
- Uses `FinishTime` or current time for ongoing runs
- Maps `BuildStatus` and `BuildResult` to `RunStatus`:
  - `NotStarted` → Pending
  - `InProgress`/`Cancelling` → Running
  - `Completed` + `Succeeded`/`PartiallySucceeded` → Success
  - `Completed` + `Failed` → Failure
  - `Completed` + `Canceled` → Canceled

**Error Handling:**
- Catches `VssServiceResponseException` for HTTP errors
- 401 Unauthorized → `OnAuthenticationFailure()`
- 429 Too Many Requests → `OnRateLimitExceeded()`
- Other `VssServiceException` → Sets Error status
- `HttpRequestException` → Sets Error status

**Limitations:**
- No error log fetching implemented yet (Azure DevOps uses different log API)
- No workflow actions (rerun, cancel, trigger) - Azure DevOps API differs from GitHub

## Project Structure

- Uses ktsu custom SDK (`ktsu.Sdk`, `ktsu.Sdk.App`) in the .csproj
- Targets .NET 10.0
- Requires `AllowUnsafeBlocks` for ImGui interop (column width pointer arithmetic)
- Dependencies managed via Central Package Management (Directory.Packages.props)

**Key Dependencies:**
- `Hexa.NET.ImGui`: Dear ImGui bindings for .NET
- `ktsu.ImGui.*`: ktsu wrappers and widgets for ImGui
- `Octokit`: GitHub API client library
- `Microsoft.TeamFoundationServer.Client`: Azure DevOps API client
- `Microsoft.VisualStudio.Services.Client`: Azure DevOps service connection
- `ktsu.Semantics.Strings`: Type-safe string wrappers
- `ktsu.AppDataStorage`: JSON persistence for application state

## Common Development Scenarios

### Adding a New Build Provider

1. Create a new class inheriting from `BuildProvider`
2. Add `[JsonDerivedType(typeof(NewProvider), nameof(NewProvider))]` to `BuildProvider.cs`
3. Implement required abstract methods:
   - `UpdateRepositoriesAsync`: Query provider API for repositories
   - `UpdateBuildsAsync`: Query for build definitions/workflows
   - `UpdateBuildAsync`: Fetch recent runs for a build
   - `UpdateRunAsync`: Update a single run's status
4. Override `ShowMenu()` for provider-specific menu items
5. Implement authentication via inherited credential popup or custom logic
6. Handle provider-specific rate limiting in request wrapper method
7. Register provider in `BuildMonitor.OnStart()`:
   ```csharp
   needsSave |= AppData.BuildProviders.TryAdd(NewProvider.BuildProviderName, new NewProvider());
   ```

**IMPORTANT: Use GetOrAdd Pattern for Entity Updates**

When implementing provider methods, always use `GetOrAdd()` instead of `TryAdd()` to ensure existing entities are updated:

```csharp
// ❌ WRONG - Only adds new, doesn't update existing
Repository repository = owner.CreateRepository(name, id);
repository.IsPrivate = apiRepo.Private;
if (owner.Repositories.TryAdd(id, repository))
{
    BuildMonitor.QueueSaveAppData();
}

// ✅ CORRECT - Updates existing or creates new
bool isNew = false;
Repository repository = owner.Repositories.GetOrAdd(id, _ =>
{
    isNew = true;
    return owner.CreateRepository(name, id);
});

// Always update mutable properties
bool hasChanges = false;
if (repository.IsPrivate != apiRepo.Private)
{
    repository.IsPrivate = apiRepo.Private;
    hasChanges = true;
}

// Only save when changes occur
if (isNew || hasChanges)
{
    BuildMonitor.QueueSaveAppData();
}
```

This pattern ensures:
- Existing entities receive property updates (e.g., repository visibility changes)
- No duplicate entities are created
- Efficient saves (only when actual changes occur)
- Proper logging of new vs. updated entities

### Debugging Rate Limit Issues

- Check provider status bar for rate limit consumption (e.g., "4532/5000")
- Hover over status for detailed tooltip with reset time
- Check Logs tab for rate limit messages:
  - "Rate limit pacing - waiting Xms"
  - "Skipping discovery due to low budget (X% remaining)"
  - "Rate limited - resets at HH:mm:ss"
- Adjust thresholds in `BuildProvider.cs`:
  - `LowBudgetThreshold` (currently 15%)
  - `CriticalBudgetThreshold` (currently 5%)
- Adjust pacing parameters:
  - `BaseRequestDelay` (currently 500ms)
  - `MinRequestDelay` (currently 100ms)
  - Reserve budget percentage in `CalculateAdaptivePacing()` (currently 10%)

### Debugging Priority/Update Issues

- Check `BuildSync.Priority` and `RunSync.Priority` properties
- Verify `ShouldUpdate` is returning true for the entity
- Check if provider's `MaxAllowedPriority` is filtering the entity
- Check if entity is orphaned via `IsOrphaned` property
- Verify `UpdateTimer` is running and interval has elapsed
- Check active requests in `BuildMonitor.ActiveRequests` dictionary
- Use "Refresh Build Data" context menu to force immediate update

### Modifying Duration Estimation

Key parameters in `DurationEstimator.cs`:
- `MinSamplesForEstimate`: Minimum successful runs required (currently 3)
- `MaxSamplesToConsider`: Maximum recent runs to analyze (currently 20)
- `ExponentialDecayFactor`: Weight decay per older sample (currently 0.3 = 70% decay)
- `IqrMultiplier`: Outlier detection sensitivity (currently 1.5 = standard Tukey fence)

To favor more recent runs: Increase `ExponentialDecayFactor` (e.g., 0.5 = 50% decay)
To be more aggressive with outlier removal: Decrease `IqrMultiplier` (e.g., 1.0)
To use more history: Increase `MaxSamplesToConsider`
