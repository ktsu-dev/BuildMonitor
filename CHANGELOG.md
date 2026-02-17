## v1.3.4-pre.1 (prerelease)

Changes since v1.3.3:

- Sync .github\workflows\dotnet.yml ([@KtsuTools](https://github.com/KtsuTools))

## v1.3.3 (patch)

Changes since v1.3.2:

- Add SonarLint configuration for connected mode ([@matt-edmondson](https://github.com/matt-edmondson))

## v1.3.2 (patch)

Changes since v1.3.1:

- Merge remote-tracking branch 'refs/remotes/origin/main' ([@ktsu[bot]](https://github.com/ktsu[bot]))
- Sync .github\workflows\dotnet.yml ([@ktsu[bot]](https://github.com/ktsu[bot]))
- Sync .github\workflows\dotnet.yml ([@ktsu[bot]](https://github.com/ktsu[bot]))
- Sync .github\workflows\dotnet.yml ([@ktsu[bot]](https://github.com/ktsu[bot]))
- Sync .github\workflows\dotnet.yml ([@ktsu[bot]](https://github.com/ktsu[bot]))

## v1.3.2-pre.1 (prerelease)

No significant changes detected since v1.3.2.

## v1.3.1 (patch)

Changes since v1.3.0:

- Enhance entity update strategy: implement GetOrAdd pattern for repositories, builds, and runs to ensure data consistency and prevent duplicates ([@matt-edmondson](https://github.com/matt-edmondson))

## v1.3.0 (minor)

Changes since v1.2.0:

- Enhance architecture documentation: update provider support, add adaptive pacing details, and clarify error handling ([@matt-edmondson](https://github.com/matt-edmondson))
- Add .gitignore and project configuration; enhance repository handling in BuildMonitor ([@matt-edmondson](https://github.com/matt-edmondson))
- Enhance build status display with radial progress indicators and adjust column widths ([@matt-edmondson](https://github.com/matt-edmondson))
- Remove legacy build scripts ([@matt-edmondson](https://github.com/matt-edmondson))
- Enhance winget manifest update script by restoring packages for SDK properties and improving packageId resolution logic ([@matt-edmondson](https://github.com/matt-edmondson))
- Enhance build visibility checks and update filtering logic for runs and builds ([@matt-edmondson](https://github.com/matt-edmondson))
- Add Next Update column to build table and enhance BuildSync with progress tracking ([@matt-edmondson](https://github.com/matt-edmondson))
- Fix winget manafest generator ([@matt-edmondson](https://github.com/matt-edmondson))
- Implement budget-based request prioritization for builds and runs, enhancing update logic and efficiency ([@matt-edmondson](https://github.com/matt-edmondson))
- Enhance sync management by adding orphan detection for builds and runs, improving cleanup logic and update conditions ([@matt-edmondson](https://github.com/matt-edmondson))
- Implement branch-specific duration estimation and add DurationEstimator class for improved accuracy ([@matt-edmondson](https://github.com/matt-edmondson))
- Add Estimate column and update related rendering logic in BuildMonitor ([@matt-edmondson](https://github.com/matt-edmondson))
- Refactor project references to package references for ImGui components ([@matt-edmondson](https://github.com/matt-edmondson))
- Add functionality to display empty repositories and implement related filtering logic ([@matt-edmondson](https://github.com/matt-edmondson))
- Enhance owner tab management by creating tabs based on provider type and updating tab visibility logic ([@matt-edmondson](https://github.com/matt-edmondson))
- Add owner tab filtering functionality and update related UI components ([@matt-edmondson](https://github.com/matt-edmondson))
- Refactor Azure DevOps provider to streamline status handling and remove unused variable ([@matt-edmondson](https://github.com/matt-edmondson))
- Enhance error handling in Azure DevOps provider and update package vulnerability suppression ([@matt-edmondson](https://github.com/matt-edmondson))
- Update project to target .NET 10.0 and adjust package versions ([@matt-edmondson](https://github.com/matt-edmondson))
- Enhance CLAUDE.md with context menu actions and provider status tracking details ([@matt-edmondson](https://github.com/matt-edmondson))
- rate limit display ([@matt-edmondson](https://github.com/matt-edmondson))
- update deps ([@matt-edmondson](https://github.com/matt-edmondson))
- Fix an issue where token would get erased when you were rate limited ([@matt-edmondson](https://github.com/matt-edmondson))
- Allow concurrent requests ([@matt-edmondson](https://github.com/matt-edmondson))
- Enhance ClearData method to clear repositories for each owner ([@matt-edmondson](https://github.com/matt-edmondson))
- Make error display text clickable with unique widget ID ([@matt-edmondson](https://github.com/matt-edmondson))
- Add error handling and display for build runs ([@matt-edmondson](https://github.com/matt-edmondson))
- Add LastRun column to BuildMonitor table and update Strings ([@matt-edmondson](https://github.com/matt-edmondson))
- Add persistence for column filters ([@matt-edmondson](https://github.com/matt-edmondson))
- Refactor column width handling to use a dictionary and improve width retrieval logic in build monitor ([@matt-edmondson](https://github.com/matt-edmondson))
- Validate column widths before saving to prevent corrupted values in build monitor ([@matt-edmondson](https://github.com/matt-edmondson))
- Refactor SaveColumnWidths method to use ImGuiTablePtr for column width retrieval ([@matt-edmondson](https://github.com/matt-edmondson))
- Set search box widths to dynamic sizing in build monitor table ([@matt-edmondson](https://github.com/matt-edmondson))
- Fix progress bar width in build monitor to allow for dynamic sizing ([@matt-edmondson](https://github.com/matt-edmondson))
- Fix color indicator logic for build status in the build monitor ([@matt-edmondson](https://github.com/matt-edmondson))
- Implement dynamic column width management in build monitor table ([@matt-edmondson](https://github.com/matt-edmondson))
- Refactor ClearData method to use expression-bodied member syntax ([@matt-edmondson](https://github.com/matt-edmondson))
- Add build updating indicator to the build monitor ([@matt-edmondson](https://github.com/matt-edmondson))
- Add Clear Data functionality to the build monitor ([@matt-edmondson](https://github.com/matt-edmondson))
- Add imgui.ini to .gitignore to exclude ImGui configuration files ([@matt-edmondson](https://github.com/matt-edmondson))
- Add branch filtering to build monitor and update related structures ([@matt-edmondson](https://github.com/matt-edmondson))
- Refactor classes to use SemanticString for type safety and improve code clarity ([@matt-edmondson](https://github.com/matt-edmondson))
- Refactor project files to manage package versions centrally and update SDK references ([@matt-edmondson](https://github.com/matt-edmondson))
- Update README.md to clarify NuGetApiKey parameter as optional for publishing ([@matt-edmondson](https://github.com/matt-edmondson))
- Add CLAUDE.md for project guidance and architecture overview ([@matt-edmondson](https://github.com/matt-edmondson))
- Update .editorconfig, .gitignore, and .runsettings for improved settings and new files; modify PSBuild.psm1 for enhanced functionality and error handling. ([@matt-edmondson](https://github.com/matt-edmondson))
- Update ktsu.AppDataStorage package version to 1.15.5 ([@matt-edmondson](https://github.com/matt-edmondson))
- Remove Directory.Build.props and Directory.Build.targets files; add copyright notices to BuildMonitor source files. ([@matt-edmondson](https://github.com/matt-edmondson))
- Update DESCRIPTION.md to clarify application purpose and modify BuildMonitor.csproj to use ktsu.Sdk.App/1.8.0 ([@matt-edmondson](https://github.com/matt-edmondson))
- Update README to match standard template format ([@matt-edmondson](https://github.com/matt-edmondson))
- Update packages ([@matt-edmondson](https://github.com/matt-edmondson))

## v1.2.30 (patch)

Changes since v1.2.29:

- Enhance build status display with radial progress indicators and adjust column widths ([@matt-edmondson](https://github.com/matt-edmondson))

## v1.2.29 (patch)

Changes since v1.2.28:

- Remove legacy build scripts ([@matt-edmondson](https://github.com/matt-edmondson))

## v1.2.28 (patch)

Changes since v1.2.27:

- Sync .github\workflows\dotnet.yml ([@ktsu[bot]](https://github.com/ktsu[bot]))
- Sync global.json ([@ktsu[bot]](https://github.com/ktsu[bot]))
- Bump the microsoft group with 2 updates ([@dependabot[bot]](https://github.com/dependabot[bot]))
- Bump the ktsu group with 4 updates ([@dependabot[bot]](https://github.com/dependabot[bot]))
- Bump the ktsu group with 4 updates ([@dependabot[bot]](https://github.com/dependabot[bot]))

## v1.2.28-pre.2 (prerelease)

Changes since v1.2.28-pre.1:

- Bump the microsoft group with 2 updates ([@dependabot[bot]](https://github.com/dependabot[bot]))
- Bump the ktsu group with 4 updates ([@dependabot[bot]](https://github.com/dependabot[bot]))

## v1.2.28-pre.1 (prerelease)

No significant changes detected since v1.2.28.

## v1.2.27 (patch)

Changes since v1.2.26:

- Add logging and fix an issue where github repos would be fetched for the wrong owners ([@matt-edmondson](https://github.com/matt-edmondson))
- Bump the ktsu group with 1 update ([@dependabot[bot]](https://github.com/dependabot[bot]))

## v1.2.27-pre.1 (prerelease)

No significant changes detected since v1.2.27.

## v1.2.26 (patch)

Changes since v1.2.25:

- Enhance winget manifest update script by restoring packages for SDK properties and improving packageId resolution logic ([@matt-edmondson](https://github.com/matt-edmondson))
- Enhance build visibility checks and update filtering logic for runs and builds ([@matt-edmondson](https://github.com/matt-edmondson))

## v1.2.25 (patch)

Changes since v1.2.24:

- Add Next Update column to build table and enhance BuildSync with progress tracking ([@matt-edmondson](https://github.com/matt-edmondson))

## v1.2.24 (patch)

Changes since v1.2.23:

- Fix winget manafest generator ([@matt-edmondson](https://github.com/matt-edmondson))

## v1.2.24-pre.5 (prerelease)

Changes since v1.2.24-pre.4:

- Bump Polyfill from 9.8.0 to 9.8.1 ([@dependabot[bot]](https://github.com/dependabot[bot]))
- Bump MSTest.Sdk from 4.0.2 to 4.1.0 ([@dependabot[bot]](https://github.com/dependabot[bot]))

## v1.2.24-pre.4 (prerelease)

Changes since v1.2.24-pre.3:

- Bump the ktsu group with 4 updates ([@dependabot[bot]](https://github.com/dependabot[bot]))

## v1.2.24-pre.3 (prerelease)

Changes since v1.2.24-pre.2:

- Merge remote-tracking branch 'refs/remotes/origin/main' ([@ktsu[bot]](https://github.com/ktsu[bot]))
- Sync scripts\PSBuild.psm1 ([@ktsu[bot]](https://github.com/ktsu[bot]))

## v1.2.24-pre.2 (prerelease)

Changes since v1.2.24-pre.1:

- Sync scripts\PSBuild.psm1 ([@ktsu[bot]](https://github.com/ktsu[bot]))
- Sync global.json ([@ktsu[bot]](https://github.com/ktsu[bot]))

## v1.2.24-pre.1 (prerelease)

No significant changes detected since v1.2.24.

## v1.2.23 (patch)

Changes since v1.2.22:

- Implement budget-based request prioritization for builds and runs, enhancing update logic and efficiency ([@matt-edmondson](https://github.com/matt-edmondson))

## v1.2.22 (patch)

Changes since v1.2.21:

- Enhance sync management by adding orphan detection for builds and runs, improving cleanup logic and update conditions ([@matt-edmondson](https://github.com/matt-edmondson))
- Implement branch-specific duration estimation and add DurationEstimator class for improved accuracy ([@matt-edmondson](https://github.com/matt-edmondson))
- Add Estimate column and update related rendering logic in BuildMonitor ([@matt-edmondson](https://github.com/matt-edmondson))

## v1.2.21 (patch)

Changes since v1.2.20:

- Refactor GitHub client management to use owner-specific instances for improved concurrency handling ([@matt-edmondson](https://github.com/matt-edmondson))

## v1.2.20 (patch)

Changes since v1.2.19:

- Refactor GitHub client credential updates to use owner-specific tokens in request methods ([@matt-edmondson](https://github.com/matt-edmondson))

## v1.2.19 (patch)

Changes since v1.2.18:

- Add owner-specific token management and related UI updates for GitHub provider ([@matt-edmondson](https://github.com/matt-edmondson))

## v1.2.18 (patch)

Changes since v1.2.17:

- Refactor project references to package references for ImGui components ([@matt-edmondson](https://github.com/matt-edmondson))
- Add functionality to display empty repositories and implement related filtering logic ([@matt-edmondson](https://github.com/matt-edmondson))
- Enhance owner tab management by creating tabs based on provider type and updating tab visibility logic ([@matt-edmondson](https://github.com/matt-edmondson))
- Add owner tab filtering functionality and update related UI components ([@matt-edmondson](https://github.com/matt-edmondson))

## v1.2.17 (patch)

Changes since v1.2.16:

- Add filtering options for owner and repository in AppData, enhance Azure DevOps and GitHub provider menus, and implement project discovery functionality ([@matt-edmondson](https://github.com/matt-edmondson))

## v1.2.16 (patch)

Changes since v1.2.15:

- Refactor Azure DevOps provider to streamline status handling and remove unused variable ([@matt-edmondson](https://github.com/matt-edmondson))
- Enhance error handling in Azure DevOps provider and update package vulnerability suppression ([@matt-edmondson](https://github.com/matt-edmondson))

## v1.2.15 (patch)

Changes since v1.2.14:

- Update project to target .NET 10.0 and adjust package versions ([@matt-edmondson](https://github.com/matt-edmondson))
- Enhance CLAUDE.md with context menu actions and provider status tracking details ([@matt-edmondson](https://github.com/matt-edmondson))

## v1.2.14 (patch)

Changes since v1.2.13:

- rate limit display ([@matt-edmondson](https://github.com/matt-edmondson))
- update deps ([@matt-edmondson](https://github.com/matt-edmondson))

## v1.2.14-pre.3 (prerelease)

Changes since v1.2.14-pre.2:

- Merge remote-tracking branch 'refs/remotes/origin/main' ([@ktsu[bot]](https://github.com/ktsu[bot]))
- Sync global.json ([@ktsu[bot]](https://github.com/ktsu[bot]))
- Sync COPYRIGHT.md ([@ktsu[bot]](https://github.com/ktsu[bot]))

## v1.2.14-pre.2 (prerelease)

Changes since v1.2.14-pre.1:

- Merge remote-tracking branch 'refs/remotes/origin/main' ([@ktsu[bot]](https://github.com/ktsu[bot]))
- Sync scripts\PSBuild.psm1 ([@ktsu[bot]](https://github.com/ktsu[bot]))

## v1.2.14-pre.1 (prerelease)

No significant changes detected since v1.2.14.

## v1.2.13 (patch)

Changes since v1.2.12:

- Fix an issue where token would get erased when you were rate limited ([@matt-edmondson](https://github.com/matt-edmondson))

## v1.2.12 (patch)

Changes since v1.2.11:

- Allow concurrent requests ([@matt-edmondson](https://github.com/matt-edmondson))

## v1.2.11 (patch)

Changes since v1.2.10:

- Enhance ClearData method to clear repositories for each owner ([@matt-edmondson](https://github.com/matt-edmondson))

## v1.2.10 (patch)

Changes since v1.2.9:

- Make error display text clickable with unique widget ID ([@matt-edmondson](https://github.com/matt-edmondson))
- Add error handling and display for build runs ([@matt-edmondson](https://github.com/matt-edmondson))

## v1.2.9 (patch)

Changes since v1.2.8:

- Add LastRun column to BuildMonitor table and update Strings ([@matt-edmondson](https://github.com/matt-edmondson))

## v1.2.9-pre.3 (prerelease)

Changes since v1.2.9-pre.2:

- Bump the ktsu group with 3 updates ([@dependabot[bot]](https://github.com/dependabot[bot]))

## v1.2.9-pre.2 (prerelease)

Changes since v1.2.9-pre.1:

- Merge remote-tracking branch 'refs/remotes/origin/main' ([@ktsu[bot]](https://github.com/ktsu[bot]))
- Sync global.json ([@ktsu[bot]](https://github.com/ktsu[bot]))

## v1.2.9-pre.1 (prerelease)

No significant changes detected since v1.2.9.

## v1.2.8 (patch)

Changes since v1.2.7:

- Add persistence for column filters ([@matt-edmondson](https://github.com/matt-edmondson))

## v1.2.7 (patch)

Changes since v1.2.6:

- Refactor column width handling to use a dictionary and improve width retrieval logic in build monitor ([@matt-edmondson](https://github.com/matt-edmondson))
- Validate column widths before saving to prevent corrupted values in build monitor ([@matt-edmondson](https://github.com/matt-edmondson))
- Refactor SaveColumnWidths method to use ImGuiTablePtr for column width retrieval ([@matt-edmondson](https://github.com/matt-edmondson))
- Set search box widths to dynamic sizing in build monitor table ([@matt-edmondson](https://github.com/matt-edmondson))
- Fix progress bar width in build monitor to allow for dynamic sizing ([@matt-edmondson](https://github.com/matt-edmondson))
- Fix color indicator logic for build status in the build monitor ([@matt-edmondson](https://github.com/matt-edmondson))
- Implement dynamic column width management in build monitor table ([@matt-edmondson](https://github.com/matt-edmondson))

## v1.2.6 (patch)

Changes since v1.2.5:

- Refactor ClearData method to use expression-bodied member syntax ([@matt-edmondson](https://github.com/matt-edmondson))
- Add build updating indicator to the build monitor ([@matt-edmondson](https://github.com/matt-edmondson))
- Add Clear Data functionality to the build monitor ([@matt-edmondson](https://github.com/matt-edmondson))
- Add imgui.ini to .gitignore to exclude ImGui configuration files ([@matt-edmondson](https://github.com/matt-edmondson))
- Add branch filtering to build monitor and update related structures ([@matt-edmondson](https://github.com/matt-edmondson))

## v1.2.6-pre.3 (prerelease)

Changes since v1.2.6-pre.2:

- Bump the ktsu group with 1 update ([@dependabot[bot]](https://github.com/dependabot[bot]))

## v1.2.6-pre.2 (prerelease)

Changes since v1.2.6-pre.1:

- Merge remote-tracking branch 'refs/remotes/origin/main' ([@ktsu[bot]](https://github.com/ktsu[bot]))
- Sync scripts\update-winget-manifests.ps1 ([@ktsu[bot]](https://github.com/ktsu[bot]))

## v1.2.6-pre.1 (prerelease)

No significant changes detected since v1.2.6.

## v1.2.5 (patch)

Changes since v1.2.4:

- Add handling for archived repositories in GitHub provider ([@matt-edmondson](https://github.com/matt-edmondson))

## v1.2.4 (patch)

Changes since v1.2.3:

- Refactor classes to use SemanticString for type safety and improve code clarity ([@matt-edmondson](https://github.com/matt-edmondson))
- Refactor project files to manage package versions centrally and update SDK references ([@matt-edmondson](https://github.com/matt-edmondson))
- Update README.md to clarify NuGetApiKey parameter as optional for publishing ([@matt-edmondson](https://github.com/matt-edmondson))
- Add CLAUDE.md for project guidance and architecture overview ([@matt-edmondson](https://github.com/matt-edmondson))
- Update .editorconfig, .gitignore, and .runsettings for improved settings and new files; modify PSBuild.psm1 for enhanced functionality and error handling. ([@matt-edmondson](https://github.com/matt-edmondson))
- Update ktsu.AppDataStorage package version to 1.15.5 ([@matt-edmondson](https://github.com/matt-edmondson))

## v1.2.4-pre.21 (prerelease)

Changes since v1.2.4-pre.20:

- Update: - ktsu.AppDataStorage to 1.15.6 - ktsu.Extensions to 1.5.6 - ktsu.TextFilter to 1.5.3 ([@dependabot[bot]](https://github.com/dependabot[bot]))

## v1.2.4-pre.20 (prerelease)

Changes since v1.2.4-pre.19:

- Update ktsu.AppDataStorage package version to 1.15.5 ([@matt-edmondson](https://github.com/matt-edmondson))

## v1.2.4-pre.19 (prerelease)

Changes since v1.2.4-pre.18:

- Update ktsu.AppDataStorage to 1.15.4 ([@dependabot[bot]](https://github.com/dependabot[bot]))

## v1.2.4-pre.18 (prerelease)

Changes since v1.2.4-pre.17:

- Merge remote-tracking branch 'refs/remotes/origin/main' ([@ktsu[bot]](https://github.com/ktsu[bot]))
- Sync scripts\PSBuild.psm1 ([@ktsu[bot]](https://github.com/ktsu[bot]))
- Sync .editorconfig ([@ktsu[bot]](https://github.com/ktsu[bot]))
- Sync .gitattributes ([@ktsu[bot]](https://github.com/ktsu[bot]))
- Sync .gitignore ([@ktsu[bot]](https://github.com/ktsu[bot]))
- Sync .mailmap ([@ktsu[bot]](https://github.com/ktsu[bot]))
- Sync .runsettings ([@ktsu[bot]](https://github.com/ktsu[bot]))

## v1.2.4-pre.17 (prerelease)

Changes since v1.2.4-pre.16:

- Update ktsu.Extensions to 1.5.5 ([@dependabot[bot]](https://github.com/dependabot[bot]))

## v1.2.4-pre.16 (prerelease)

Changes since v1.2.4-pre.15:

- Sync scripts\PSBuild.psm1 ([@ktsu[bot]](https://github.com/ktsu[bot]))
- Sync .editorconfig ([@ktsu[bot]](https://github.com/ktsu[bot]))
- Sync .gitattributes ([@ktsu[bot]](https://github.com/ktsu[bot]))
- Sync .gitignore ([@ktsu[bot]](https://github.com/ktsu[bot]))
- Sync .mailmap ([@ktsu[bot]](https://github.com/ktsu[bot]))
- Sync .runsettings ([@ktsu[bot]](https://github.com/ktsu[bot]))

## v1.2.4-pre.15 (prerelease)

Changes since v1.2.4-pre.14:


## v1.2.4-pre.14 (prerelease)

Changes since v1.2.4-pre.13:


## v1.2.4-pre.13 (prerelease)

Changes since v1.2.4-pre.12:


## v1.2.4-pre.12 (prerelease)

Changes since v1.2.4-pre.11:


## v1.2.4-pre.11 (prerelease)

Changes since v1.2.4-pre.10:


## v1.2.4-pre.10 (prerelease)

Changes since v1.2.4-pre.9:


## v1.2.4-pre.9 (prerelease)

Changes since v1.2.4-pre.8:


## v1.2.4-pre.8 (prerelease)

Changes since v1.2.4-pre.7:


## v1.2.4-pre.7 (prerelease)

Changes since v1.2.4-pre.6:


## v1.2.4-pre.6 (prerelease)

Changes since v1.2.4-pre.5:


## v1.2.4-pre.5 (prerelease)

Changes since v1.2.4-pre.4:


## v1.2.4-pre.4 (prerelease)

Changes since v1.2.4-pre.3:


## v1.2.4-pre.3 (prerelease)

Changes since v1.2.4-pre.2:

- Update: - ktsu.Extensions to 1.5.3 - ktsu.ImGuiPopups to 1.3.2 - ktsu.TextFilter to 1.5.2 ([@dependabot[bot]](https://github.com/dependabot[bot]))

## v1.2.4-pre.2 (prerelease)

Changes since v1.2.4-pre.1:


## v1.2.4-pre.1 (prerelease)

No significant changes detected since v1.2.4.

## v1.2.3 (patch)

Changes since v1.2.2:

- Remove Directory.Build.props and Directory.Build.targets files; add copyright notices to BuildMonitor source files. ([@matt-edmondson](https://github.com/matt-edmondson))
- Update DESCRIPTION.md to clarify application purpose and modify BuildMonitor.csproj to use ktsu.Sdk.App/1.8.0 ([@matt-edmondson](https://github.com/matt-edmondson))

## v1.2.3-pre.4 (prerelease)

Changes since v1.2.3-pre.3:

- Bump the ktsu group with 3 updates ([@dependabot[bot]](https://github.com/dependabot[bot]))

## v1.2.3-pre.3 (prerelease)

Changes since v1.2.3-pre.2:

- Bump the ktsu group with 2 updates ([@dependabot[bot]](https://github.com/dependabot[bot]))

## v1.2.3-pre.2 (prerelease)

Changes since v1.2.3-pre.1:

- Sync .github\workflows\dotnet.yml ([@ktsu[bot]](https://github.com/ktsu[bot]))
- Sync .editorconfig ([@ktsu[bot]](https://github.com/ktsu[bot]))
- Sync .runsettings ([@ktsu[bot]](https://github.com/ktsu[bot]))

## v1.2.3-pre.1 (prerelease)

No significant changes detected since v1.2.3.

## v1.2.2 (patch)

Changes since v1.2.1:

- Update README to match standard template format ([@matt-edmondson](https://github.com/matt-edmondson))

## v1.2.2-pre.4 (prerelease)

Changes since v1.2.2-pre.3:

- Bump Microsoft.DotNet.ILCompiler in the microsoft group ([@dependabot[bot]](https://github.com/dependabot[bot]))

## v1.2.2-pre.3 (prerelease)

Changes since v1.2.2-pre.2:

- Bump the ktsu group with 2 updates ([@dependabot[bot]](https://github.com/dependabot[bot]))

## v1.2.2-pre.2 (prerelease)

Changes since v1.2.2-pre.1:

- Sync .editorconfig ([@ktsu[bot]](https://github.com/ktsu[bot]))

## v1.2.2-pre.1 (prerelease)

No significant changes detected since v1.2.2.

## v1.2.1 (patch)

Changes since v1.2.0:

- Update packages ([@matt-edmondson](https://github.com/matt-edmondson))

## v1.2.0 (minor)

Changes since v1.1.0:

- Add LICENSE template ([@matt-edmondson](https://github.com/matt-edmondson))
- Update packages ([@matt-edmondson](https://github.com/matt-edmondson))

## v1.1.1 (patch)

Changes since v1.1.0:

- Update packages ([@matt-edmondson](https://github.com/matt-edmondson))

## v1.1.0 (minor)

Changes since v1.0.0:

- [minor] Fix changelog tag reading in the case where there is only 1 tag present ([@matt-edmondson](https://github.com/matt-edmondson))
- [minor] Update packages and fix versioning script ([@matt-edmondson](https://github.com/matt-edmondson))

## v1.0.0 (major)

- Update make-version.ps1 ([@matt-edmondson](https://github.com/matt-edmondson))
- Update make-version.ps1 ([@matt-edmondson](https://github.com/matt-edmondson))
- Apply new editorconfig ([@matt-edmondson](https://github.com/matt-edmondson))
- Add build filtering and improve async handling ([@matt-edmondson](https://github.com/matt-edmondson))
- Add automation scripts for metadata and version management ([@matt-edmondson](https://github.com/matt-edmondson))
- Renamed metadata files ([@matt-edmondson](https://github.com/matt-edmondson))
- Rename LICENSE to LICENSE.md and update copyright information; remove obsolete RELEASES, azure-pipelines.yml, global.json, and setup.nsi files ([@matt-edmondson](https://github.com/matt-edmondson))
- Bump ktsu.AppDataStorage, ImGuiApp, ImGuiPopups, and ImGuiWidgets package versions ([@matt-edmondson](https://github.com/matt-edmondson))
- Bump ktsu.Extensions, ImGuiPopups, and ImGuiWidgets package versions ([@matt-edmondson](https://github.com/matt-edmondson))
- Bump ktsu package versions and add ILCompiler and ILLink.Tasks ([@matt-edmondson](https://github.com/matt-edmondson))
- Dont include cancelled builds in calcs ([@matt-edmondson](https://github.com/matt-edmondson))
- Update l;braries ([@matt-edmondson](https://github.com/matt-edmondson))
- Migrate ktsu.io to ktsu namespace ([@matt-edmondson](https://github.com/matt-edmondson))
- Update RELEASES ([@matt-edmondson](https://github.com/matt-edmondson))
- Include vcredist in the installer ([@matt-edmondson](https://github.com/matt-edmondson))
- Update RELEASES ([@matt-edmondson](https://github.com/matt-edmondson))
- Update setup.nsi ([@matt-edmondson](https://github.com/matt-edmondson))
- Add vcredist to installer for the native dependencies ([@matt-edmondson](https://github.com/matt-edmondson))
- Update build scripts ([@matt-edmondson](https://github.com/matt-edmondson))
- Fix an issue where you could not add new repo owners ([@matt-edmondson](https://github.com/matt-edmondson))
- Delete .gitmodules ([@matt-edmondson](https://github.com/matt-edmondson))
- Update RELEASES ([@matt-edmondson](https://github.com/matt-edmondson))
- Update RELEASES ([@matt-edmondson](https://github.com/matt-edmondson))
- Update setup.nsi ([@matt-edmondson](https://github.com/matt-edmondson))
- Update azure-pipelines.yml for Azure Pipelines ([@matt-edmondson](https://github.com/matt-edmondson))
- Update azure-pipelines.yml for Azure Pipelines ([@matt-edmondson](https://github.com/matt-edmondson))
- Update azure-pipelines.yml for Azure Pipelines ([@matt-edmondson](https://github.com/matt-edmondson))
- Update RELEASES ([@matt-edmondson](https://github.com/matt-edmondson))
- Change namespace to ktsu from ktsu.io ([@matt-edmondson](https://github.com/matt-edmondson))
- Update RELEASES ([@matt-edmondson](https://github.com/matt-edmondson))
- Update Directory.Build.props ([@matt-edmondson](https://github.com/matt-edmondson))
- Update Directory.Build.props ([@matt-edmondson](https://github.com/matt-edmondson))
- Update azure-pipelines.yml for Azure Pipelines ([@matt-edmondson](https://github.com/matt-edmondson))
- Update azure-pipelines.yml for Azure Pipelines ([@matt-edmondson](https://github.com/matt-edmondson))
- Update global.json ([@matt-edmondson](https://github.com/matt-edmondson))
- Update azure-pipelines.yml for Azure Pipelines ([@matt-edmondson](https://github.com/matt-edmondson))
- Update azure-pipelines.yml for Azure Pipelines ([@matt-edmondson](https://github.com/matt-edmondson))
- Update azure-pipelines.yml for Azure Pipelines ([@matt-edmondson](https://github.com/matt-edmondson))
- Update azure-pipelines.yml for Azure Pipelines ([@matt-edmondson](https://github.com/matt-edmondson))
- Update azure-pipelines.yml ([@matt-edmondson](https://github.com/matt-edmondson))
- Create global.json ([@matt-edmondson](https://github.com/matt-edmondson))
- Update azure-pipelines.yml ([@matt-edmondson](https://github.com/matt-edmondson))
- Update azure-pipelines.yml ([@matt-edmondson](https://github.com/matt-edmondson))
- Update azure-pipelines.yml ([@matt-edmondson](https://github.com/matt-edmondson))
- Update azure-pipelines.yml ([@matt-edmondson](https://github.com/matt-edmondson))
- Update azure-pipelines.yml ([@matt-edmondson](https://github.com/matt-edmondson))
- Delete RELEASE file ([@matt-edmondson](https://github.com/matt-edmondson))
- Update setup.nsi ([@matt-edmondson](https://github.com/matt-edmondson))
- Update RELEASE ([@matt-edmondson](https://github.com/matt-edmondson))
- Update setup.nsi ([@matt-edmondson](https://github.com/matt-edmondson))
- Update RELEASE from  to 1.0.0-alpha.1 ([@Azure Pipeline](https://github.com/Azure Pipeline))
- Update RELEASE ([@matt-edmondson](https://github.com/matt-edmondson))
- Update setup.nsi ([@matt-edmondson](https://github.com/matt-edmondson))
- Update RELEASE from  to 1.0.0-alpha.1 ([@Azure Pipeline](https://github.com/Azure Pipeline))
- Add script for installing dotnet 8 during installation ([@matt-edmondson](https://github.com/matt-edmondson))
- Update azure-pipelines.yml for Azure Pipelines ([@matt-edmondson](https://github.com/matt-edmondson))
- Update RELEASE from  to 1.0.0-alpha.1 ([@Azure Pipeline](https://github.com/Azure Pipeline))
- Update azure-pipelines.yml for Azure Pipelines ([@matt-edmondson](https://github.com/matt-edmondson))
- Update azure-pipelines.yml for Azure Pipelines ([@matt-edmondson](https://github.com/matt-edmondson))
- Update azure-pipelines.yml for Azure Pipelines ([@matt-edmondson](https://github.com/matt-edmondson))
- Update azure-pipelines.yml for Azure Pipelines ([@matt-edmondson](https://github.com/matt-edmondson))
- Update azure-pipelines.yml for Azure Pipelines ([@matt-edmondson](https://github.com/matt-edmondson))
- Update azure-pipelines.yml for Azure Pipelines ([@matt-edmondson](https://github.com/matt-edmondson))
- Update azure-pipelines.yml for Azure Pipelines ([@matt-edmondson](https://github.com/matt-edmondson))
- Update azure-pipelines.yml for Azure Pipelines ([@matt-edmondson](https://github.com/matt-edmondson))
- Update azure-pipelines.yml for Azure Pipelines ([@matt-edmondson](https://github.com/matt-edmondson))
- Update azure-pipelines.yml for Azure Pipelines ([@matt-edmondson](https://github.com/matt-edmondson))
- Update azure-pipelines.yml for Azure Pipelines ([@matt-edmondson](https://github.com/matt-edmondson))
- new file:   RELEASE ([@matt-edmondson](https://github.com/matt-edmondson))
- Update azure-pipelines.yml for Azure Pipelines ([@matt-edmondson](https://github.com/matt-edmondson))
- Update azure-pipelines.yml for Azure Pipelines ([@matt-edmondson](https://github.com/matt-edmondson))
- Update azure-pipelines.yml for Azure Pipelines ([@matt-edmondson](https://github.com/matt-edmondson))
- Update azure-pipelines.yml for Azure Pipelines ([@matt-edmondson](https://github.com/matt-edmondson))
- Update azure-pipelines.yml for Azure Pipelines ([@matt-edmondson](https://github.com/matt-edmondson))
- Update azure-pipelines.yml for Azure Pipelines ([@matt-edmondson](https://github.com/matt-edmondson))
- Sync main (#5) ([@matt-edmondson](https://github.com/matt-edmondson))
- Add Azure DevOps build scripts (#4) ([@matt-edmondson](https://github.com/matt-edmondson))
- Update azure-pipelines.yml for Azure Pipelines ([@matt-edmondson](https://github.com/matt-edmondson))
- Update azure-pipelines.yml for Azure Pipelines ([@matt-edmondson](https://github.com/matt-edmondson))
- Update azure-pipelines.yml for Azure Pipelines ([@matt-edmondson](https://github.com/matt-edmondson))
- Update azure-pipelines.yml for Azure Pipelines ([@matt-edmondson](https://github.com/matt-edmondson))
- Update azure-pipelines.yml for Azure Pipelines ([@matt-edmondson](https://github.com/matt-edmondson))
- Update azure-pipelines.yml for Azure Pipelines ([@matt-edmondson](https://github.com/matt-edmondson))
- Update azure-pipelines.yml for Azure Pipelines ([@matt-edmondson](https://github.com/matt-edmondson))
- Update azure-pipelines.yml for Azure Pipelines ([@matt-edmondson](https://github.com/matt-edmondson))
- Fix build script ([@matt-edmondson](https://github.com/matt-edmondson))
- Fix build script ([@matt-edmondson](https://github.com/matt-edmondson))
- Fix build script ([@matt-edmondson](https://github.com/matt-edmondson))
- Fix build script ([@matt-edmondson](https://github.com/matt-edmondson))
- Fix build script ([@matt-edmondson](https://github.com/matt-edmondson))
- Fix build script ([@matt-edmondson](https://github.com/matt-edmondson))
- Fix build script ([@matt-edmondson](https://github.com/matt-edmondson))
- Fix build script ([@matt-edmondson](https://github.com/matt-edmondson))
- Fix build script ([@matt-edmondson](https://github.com/matt-edmondson))
- Fix build script ([@matt-edmondson](https://github.com/matt-edmondson))
- Fix build script ([@matt-edmondson](https://github.com/matt-edmondson))
- Fix build script ([@matt-edmondson](https://github.com/matt-edmondson))
- Fix build script ([@matt-edmondson](https://github.com/matt-edmondson))
- Remove unneeded humanizer packages ([@matt-edmondson](https://github.com/matt-edmondson))
- Fix build script ([@matt-edmondson](https://github.com/matt-edmondson))
- Fix build script ([@matt-edmondson](https://github.com/matt-edmondson))
- Fix build script ([@matt-edmondson](https://github.com/matt-edmondson))
- Fix build script ([@matt-edmondson](https://github.com/matt-edmondson))
- Fix build script ([@matt-edmondson](https://github.com/matt-edmondson))
- Fix build script ([@matt-edmondson](https://github.com/matt-edmondson))
- Fix build script ([@matt-edmondson](https://github.com/matt-edmondson))
- Fix build script ([@matt-edmondson](https://github.com/matt-edmondson))
- Fix build script ([@matt-edmondson](https://github.com/matt-edmondson))
- Fix build script ([@matt-edmondson](https://github.com/matt-edmondson))
- Fix build script ([@matt-edmondson](https://github.com/matt-edmondson))
- Fix build script ([@matt-edmondson](https://github.com/matt-edmondson))
- Fix build script ([@matt-edmondson](https://github.com/matt-edmondson))
- Fix name/displayName in build script ([@matt-edmondson](https://github.com/matt-edmondson))
- Fix indenting on build script ([@matt-edmondson](https://github.com/matt-edmondson))
- Add mailmap file ([@matt-edmondson](https://github.com/matt-edmondson))
- Add nsis installer script ([@matt-edmondson](https://github.com/matt-edmondson))
- Improve status tracking of async requests (#3) ([@matt-edmondson](https://github.com/matt-edmondson))
- Sync main (#2) ([@matt-edmondson](https://github.com/matt-edmondson))
- Update azure-pipelines.yml for Azure Pipelines ([@matt-edmondson](https://github.com/matt-edmondson))
- Update azure-pipelines.yml for Azure Pipelines ([@matt-edmondson](https://github.com/matt-edmondson))
- Update azure-pipelines.yml for Azure Pipelines ([@matt-edmondson](https://github.com/matt-edmondson))
- Update azure-pipelines.yml for Azure Pipelines ([@matt-edmondson](https://github.com/matt-edmondson))
- Update azure-pipelines.yml for Azure Pipelines ([@matt-edmondson](https://github.com/matt-edmondson))
- Update azure-pipelines.yml for Azure Pipelines ([@matt-edmondson](https://github.com/matt-edmondson))
- Update azure-pipelines.yml for Azure Pipelines ([@matt-edmondson](https://github.com/matt-edmondson))
- Set up CI with Azure Pipelines ([@matt-edmondson](https://github.com/matt-edmondson))
- update application build script ([@matt-edmondson](https://github.com/matt-edmondson))
- Update build script ([@matt-edmondson](https://github.com/matt-edmondson))
- Application build script ([@matt-edmondson](https://github.com/matt-edmondson))
- Improve status tracking of async requests ([@matt-edmondson](https://github.com/matt-edmondson))
- Refactor to use concurrent dictionaries instead of manual locks ([@matt-edmondson](https://github.com/matt-edmondson))
- Improve debug output ([@matt-edmondson](https://github.com/matt-edmondson))
- Use pagination on the runs sync ([@matt-edmondson](https://github.com/matt-edmondson))
- Fix syncing ([@matt-edmondson](https://github.com/matt-edmondson))
- Use sequential syncs to avoid being rate limited ([@matt-edmondson](https://github.com/matt-edmondson))
- Add some thread sychronization ([@matt-edmondson](https://github.com/matt-edmondson))
- Time formatting ([@matt-edmondson](https://github.com/matt-edmondson))
- Give duration its own dedicated column ([@matt-edmondson](https://github.com/matt-edmondson))
- Update progress in realtime ([@matt-edmondson](https://github.com/matt-edmondson))
- Add project files. ([@matt-edmondson](https://github.com/matt-edmondson))

