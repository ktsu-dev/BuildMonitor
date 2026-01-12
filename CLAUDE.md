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

### Threading and Synchronization

- All data updates happen asynchronously via Task-based operations
- `ConcurrentDictionary` is used throughout for thread-safe collection access
- `BuildMonitor.SyncLock` exists but is only used for menu rendering
- Rate limiting is handled per-provider via `RateLimitSleep` which increases when rate limits are hit

### UI and State Management

- Built on ktsu.ImGui.App framework (wraps Dear ImGui)
- Single table showing all builds across all providers
- Filtering via text inputs at column headers (uses ktsu.TextFilter)
- AppData persists via ktsu.AppDataStorage (includes window state, providers, credentials)
- `QueueSaveAppData()` defers saves until the next frame to batch updates

### Semantic String Types

The codebase uses ktsu.Semantics.Strings for type-safe string identifiers:
- BuildProviderName, BuildId, BuildName
- OwnerName, OwnerId
- RepositoryName, RepositoryId
- RunName, RunId

These prevent mixing up different types of identifiers and enable type-safe conversions via `.As<T>()`.

## Project Structure

- Uses ktsu custom SDK (`ktsu.Sdk`, `ktsu.Sdk.App`) in the .csproj
- Targets .NET 9.0
- Requires `AllowUnsafeBlocks` for ImGui interop
- Dependencies managed via Central Package Management (Directory.Packages.props)
