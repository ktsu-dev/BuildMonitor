# ktsu.BuildMonitor

> A desktop application for monitoring CI/CD build statuses across multiple providers.

[![License](https://img.shields.io/github/license/ktsu-dev/BuildMonitor)](https://github.com/ktsu-dev/BuildMonitor/blob/main/LICENSE.md)
[![Build Status](https://github.com/ktsu-dev/BuildMonitor/workflows/build/badge.svg)](https://github.com/ktsu-dev/BuildMonitor/actions)
[![GitHub Stars](https://img.shields.io/github/stars/ktsu-dev/BuildMonitor?style=social)](https://github.com/ktsu-dev/BuildMonitor/stargazers)

## Introduction

BuildMonitor is a desktop application that provides real-time monitoring of CI/CD builds across multiple providers. It visualizes build statuses, progress, history, and estimated completion times in a user-friendly interface, helping developers keep track of their builds without constantly checking web dashboards.

## Features

- **Real-Time Monitoring**: Track build statuses as they happen with adaptive polling
- **Multiple Provider Support**: Monitor builds from different CI/CD systems
- **Visual Status Indicators**: Color-coded status display for quick assessment
- **Build Progress Tracking**: Progress bars showing completion percentage for ongoing builds
- **Build History Visualization**: See recent build statuses at a glance (last 5 runs per branch)
- **ETA Calculation**: Estimated completion times based on historical build durations
- **Error Details**: View actual error messages from failed builds, parsed from job logs
- **Filtering Capabilities**: Filter builds by repository, branch, name, or status
- **Context Menu Actions**: Right-click to open URLs, copy links, or trigger workflow actions
- **GitHub Workflow Control**: Re-run failed workflows, cancel running builds, or trigger new runs
- **Provider Status Display**: Visual indicators showing API health, rate limits, and auth status

## Screenshots

[Screenshot placeholder]

## Installation

### Windows

1. Download the latest release from the [Releases](https://github.com/ktsu-dev/BuildMonitor/releases) page
2. Extract the ZIP file to a location of your choice
3. Run `BuildMonitor.exe`

### Building from Source

Requires [.NET 10.0 SDK](https://dotnet.microsoft.com/download) or later.

```bash
git clone https://github.com/ktsu-dev/BuildMonitor.git
cd BuildMonitor
dotnet build
```

## Usage Guide

### Initial Setup

When first launching the application, you'll need to configure your build providers:

1. Go to the "Providers" menu
2. Select the provider you want to configure (e.g., GitHub)
3. Enter your authentication credentials or API token
4. Add the repositories you want to monitor

### Interface Overview

The main interface displays a table of all your builds grouped by workflow and branch:

- **Status Indicator**: Color-coded dot showing build status (cyan when updating)
- **Repository**: Owner and repository name (e.g., `ktsu-dev/BuildMonitor`)
- **Build Name**: Name of the workflow or build definition
- **Branch**: The branch this build ran on
- **Status**: Current status (Pending, Running, Success, Failure, Canceled)
- **Last Run**: Timestamp of when the build started
- **Duration**: How long the build has been running or took to complete
- **History**: Visual history of the last 5 builds on this branch
- **Progress**: Progress bar for ongoing builds
- **ETA**: Estimated time to completion for ongoing builds
- **Errors**: Error messages from failed builds (click to view full details)

A status bar at the top shows the health of each configured provider.

### Filtering Builds

Use the filter inputs below the column headers to narrow down the displayed builds:

- Filter by repository name
- Filter by build/workflow name
- Filter by branch name
- Filter by status

Filters support wildcards and are case-insensitive.

### Context Menu Actions

Right-click on any build row to access quick actions:

**Navigation:**

- Open repository, workflow, branch, or run in your browser
- Copy URLs to clipboard

**GitHub Actions** (when using GitHub provider):

- **Re-run Workflow**: Re-run a completed or failed workflow
- **Cancel Workflow**: Stop a running workflow
- **Trigger Workflow**: Dispatch a new workflow run on the selected branch

**Data:**

- **Refresh Build Data**: Force an immediate update of the build status

### Configuration Options

Access configuration through the menu bar:

- **File > Clear Data**: Remove all cached build data
- **Providers > [Provider] > Set Credentials**: Configure API authentication
- **Providers > [Provider] > Add Owner**: Add a GitHub user or organization to monitor

## Supported CI/CD Providers

### GitHub Actions

Full support for GitHub Actions including:

- Monitor workflows across multiple users and organizations
- View workflow run history per branch
- See actual error messages from failed jobs (parsed from build logs)
- Re-run, cancel, or trigger workflows directly from the app
- Automatic rate limit handling with backoff

**Setup:**

1. Go to **Providers > GitHub > Set Credentials**
2. Enter your GitHub username
3. Enter a Personal Access Token (PAT) with `repo` and `workflow` scopes
4. Add owners via **Providers > GitHub > Add Owner**

Additional providers are planned for future releases.

## API Reference

BuildMonitor is primarily an end-user application, not a library. However, it uses a modular architecture that could be extended for custom providers.

## Contributing

Contributions are welcome! Here's how you can help:

1. Fork the repository
2. Create your feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add some amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

Areas that would particularly benefit from contributions:
- Additional CI/CD provider implementations
- Performance improvements
- UI enhancements
- Documentation

## License

This project is licensed under the MIT License - see the [LICENSE.md](LICENSE.md) file for details.

## Acknowledgements

- Built with [Dear ImGui](https://github.com/ocornut/imgui) via [Hexa.NET.ImGui](https://github.com/HexaEngine/Hexa.NET.ImGui)
- GitHub API integration via [Octokit.net](https://github.com/octokit/octokit.net)
- Uses [ktsu](https://github.com/ktsu-dev) libraries for UI framework, text filtering, and semantic types
