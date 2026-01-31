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

BuildMonitor is a desktop ImGui-based application that monitors CI/CD build statuses across multiple providers (currently GitHub Actions). The architecture follows a hierarchical data model with concurrent updating and synchronization.

### Core Data Hierarchy

The application maintains a nested hierarchy of build data:

- **BuildProvider** (e.g., GitHub) → **Owner** → **Repository** → **Build** → **Run**

Each level is stored in `ConcurrentDictionary` collections to support thread-safe updates from async operations.

### Dual-Entity Pattern

The codebase uses a "sync" pattern where domain entities (Build, Run) are paired with synchronization classes:

- **Build** / **BuildSync**: The Build entity stores data; BuildSync manages polling intervals and async updates
- **Run** / **RunSync**: Same pattern for individual workflow runs

BuildSync and RunSync objects track when entities should be updated based on their state (ongoing builds update more frequently).

### Update Flow

1. **Main Loop** (BuildMonitor.cs): Calls `UpdateAsync()` every frame
2. **Provider Refresh** (every 300s): Updates repositories and builds from providers
3. **Build Updates** (every 60s): Polls for new runs on each build
4. **Run Updates** (10-60s, adaptive): Updates ongoing runs with adaptive intervals based on ETA

The update cycle creates BuildSync/RunSync entries in global collections (`BuildSyncCollection`, `RunSyncCollection`). Each sync object decides when to poll based on its timer and the entity state.

### Provider Architecture

BuildProvider is an abstract base class with JSON polymorphic serialization support. New providers should:

- Inherit from BuildProvider
- Add `[JsonDerivedType]` attribute to BuildProvider base class
- Implement the four abstract update methods:
  - `UpdateRepositoriesAsync(Owner)`
  - `UpdateBuildsAsync(Repository)`
  - `UpdateBuildAsync(Build)`
  - `UpdateRunAsync(Run)`
- Register in `BuildMonitor.Start()` by adding to `AppData.BuildProviders`

### Provider Status Tracking

Each BuildProvider tracks its operational status with visual indicators:

- **ProviderStatus.OK** (green): Operating normally
- **ProviderStatus.RateLimited** (yellow): API rate limit hit, includes backoff delay and reset time
- **ProviderStatus.AuthFailed** (red): Authentication failed, credentials cleared
- **ProviderStatus.Error** (magenta): Connection or other error

Status is displayed in a status bar at the top of the UI with tooltips showing detailed messages.

### Threading and Synchronization

- All data updates happen asynchronously via Task-based operations
- `ConcurrentDictionary` is used throughout for thread-safe collection access
- `BuildMonitor.SyncLock` exists but is only used for menu rendering
- Rate limiting is handled per-provider via `RateLimitSleep` which increases when rate limits are hit
- `RequestSemaphore` limits concurrent requests per provider (default: 5)

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

- Refresh Build Data: Forces immediate update of the selected build

### Error Display

Failed runs display error information fetched from GitHub job logs:

- Errors are fetched via `FetchRunErrorsAsync()` when a run transitions to failure state
- Job logs are parsed for GitHub Actions error annotations (`##[error]`) and common error patterns
- Errors are deduplicated and limited to 10 per run
- Clicking error text opens a popup with full error details
- Error text is ellipsized in the table column with full text in tooltip

### UI and State Management

- Built on ktsu.ImGui.App framework (wraps Dear ImGui)
- Single table showing all builds across all providers, grouped by (Build, Branch)
- Filtering via text inputs at column headers (uses ktsu.TextFilter)
- Column widths are persisted and restorable
- AppData persists via ktsu.AppDataStorage (includes window state, providers, credentials, filters)
- `QueueSaveAppData()` defers saves until the next frame to batch updates

### Semantic String Types

The codebase uses ktsu.Semantics.Strings for type-safe string identifiers:

- BuildProviderName, BuildId, BuildName
- OwnerName, OwnerId
- RepositoryName, RepositoryId
- RunName, RunId, BranchName

These prevent mixing up different types of identifiers and enable type-safe conversions via `.As<T>()`.

## Key Files

- **BuildMonitor.cs**: Main application class with UI rendering and update orchestration
- **BuildProvider.cs**: Abstract base class for CI/CD providers with status tracking
- **Providers/GitHub.cs**: GitHub Actions implementation with Octokit API integration
- **Build.cs / BuildSync.cs**: Build entity and its synchronization wrapper
- **Run.cs / RunSync.cs**: Run entity and its synchronization wrapper
- **AppData.cs**: Persistent application state and settings

## Project Structure

- Uses ktsu custom SDK (`ktsu.Sdk`, `ktsu.Sdk.App`) in the .csproj
- Targets .NET 10.0
- Requires `AllowUnsafeBlocks` for ImGui interop
- Dependencies managed via Central Package Management (Directory.Packages.props)
