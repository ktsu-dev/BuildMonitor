# ktsu.BuildMonitor

> A desktop application for monitoring CI/CD build statuses across multiple providers.

[![License](https://img.shields.io/github/license/ktsu-dev/BuildMonitor)](https://github.com/ktsu-dev/BuildMonitor/blob/main/LICENSE.md)
[![Build Status](https://github.com/ktsu-dev/BuildMonitor/workflows/build/badge.svg)](https://github.com/ktsu-dev/BuildMonitor/actions)
[![GitHub Stars](https://img.shields.io/github/stars/ktsu-dev/BuildMonitor?style=social)](https://github.com/ktsu-dev/BuildMonitor/stargazers)

## Introduction

BuildMonitor is a desktop application that provides real-time monitoring of CI/CD builds across multiple providers. It visualizes build statuses, progress, history, and estimated completion times in a user-friendly interface, helping developers keep track of their builds without constantly checking web dashboards.

## Features

- **Real-Time Monitoring**: Track build statuses as they happen
- **Multiple Provider Support**: Monitor builds from different CI/CD systems
- **Visual Status Indicators**: Color-coded status display for quick assessment
- **Build Progress Tracking**: Progress bars showing completion percentage for ongoing builds
- **Build History Visualization**: See recent build statuses at a glance
- **ETA Calculation**: Estimated completion times for running builds
- **Filtering Capabilities**: Filter builds by repository, name, or status
- **Configurable Updates**: Adjust polling frequency for different providers

## Screenshots

[Screenshot placeholder]

## Installation

### Windows

1. Download the latest release from the [Releases](https://github.com/ktsu-dev/BuildMonitor/releases) page
2. Extract the ZIP file to a location of your choice
3. Run `BuildMonitor.exe`

### Building from Source

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

The main interface displays a table of all your builds with the following information:

- **Status**: Color-coded indicator showing build status
- **Repository**: Name of the repository
- **Build Name**: Name of the workflow or build definition
- **Status Text**: Textual representation of the current status
- **Duration**: How long the build has been running or took to complete
- **History**: Visual history of recent builds
- **Progress**: Progress bar for ongoing builds
- **ETA**: Estimated time to completion for ongoing builds

### Filtering Builds

Use the filter inputs at the top of each column to narrow down the displayed builds:

- Filter by repository name
- Filter by build name
- Filter by status

### Configuration Options

Access additional configuration options through the menu:

- **Providers**: Configure CI/CD providers and credentials
- **Refresh Rate**: Adjust how frequently build statuses are updated

## Supported CI/CD Providers

- **GitHub Actions**: Monitor workflows from GitHub repositories
- [Additional providers coming soon]

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

- Built with [Dear ImGui](https://github.com/ocornut/imgui) for the user interface
- Uses various ktsu libraries for additional functionality
