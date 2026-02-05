# Winget Manifests Updater Script

This PowerShell script automates the process of updating winget manifest files for your project when you release a new version. It automatically detects your project settings and fetches SHA256 hashes from GitHub releases or local build artifacts.

## Features

- **Auto-Detection**: Automatically detects repository information, project type, and executable names
- **MSBuild Integration**: Extracts project properties directly from C# projects using MSBuild
- **Standard Files Recognition**: Reads from common files like README.md, DESCRIPTION.md, TAGS.md, etc.
- **Project-Type Detection**: Recognizes C#, Node.js, and Rust projects and sets appropriate defaults
- **Minimal Configuration**: Most settings are inferred from your repository
- **GitHub Integration**: Fetches release artifacts and uploads manifest files
- **Release Support**: Downloads release artifacts to calculate SHA256 hashes (or uses local build hashes if available)

## Requirements

- PowerShell 7.0 or higher
- Git (for auto-detecting repository information)
- GitHub repository with releases
- MSBuild or .NET SDK (optional, for C# projects)
- GitHub CLI (optional, for uploading manifests to releases)

## Usage

### Basic Usage (Automatic Detection)

```powershell
./update-winget-manifests.ps1 -Version "1.2.3"
```

That's it! The script will:
1. Auto-detect your GitHub repository from git remotes or PROJECT_URL.url file
2. Determine project type by scanning files
3. Extract project properties from MSBuild projects
4. Find executables and set appropriate defaults
5. Read metadata from standard files in the repo
6. Download release assets and calculate hashes
7. Generate and upload winget manifests

### Overriding Auto-Detection

If you need to override some auto-detected settings:

```powershell
./update-winget-manifests.ps1 -Version "1.2.3" -GitHubRepo "myorg/myrepo" -PackageId "myorg.MyApp" -ExecutableName "MyApp.exe"
```

## Auto-Detection Features

### Standard Files

The script automatically looks for and extracts data from these files:

- **README.md**: Project name, short description, and basic info
- **DESCRIPTION.md**: Detailed project description
- **VERSION.md**: Current version number
- **AUTHORS.md**: Publisher information
- **TAGS.md**: Tags for the package (semicolon-separated)
- **PROJECT_URL.url**: GitHub repo info

### MSBuild Integration

For .NET projects, the script extracts properties directly from project files using MSBuild:

- **RootNamespace**: Used for the winget Package ID (e.g., `ktsu.BuildMonitor`)
- **AssemblyName/Product**: Used for package name
- **Authors**: For publisher information
- **Description**: For package description
- **Version**: For package version
- **PackageTags**: For winget tags

The script uses `dotnet msbuild /getProperty:PropertyName` to evaluate MSBuild properties, which correctly resolves properties set by custom SDKs.

### Custom MSBuild Properties

You can add these properties to your .csproj file for direct winget integration:

```xml
<PropertyGroup>
  <!-- Standard MSBuild properties -->
  <RootNamespace>MyOrg.MyAwesomeApp</RootNamespace>  <!-- Used as Package ID -->
  <AssemblyName>MyAwesomeApp</AssemblyName>
  <Authors>My Organization</Authors>
  <Description>This is my awesome application that does amazing things.</Description>
  <Version>1.2.3</Version>
  <PackageTags>utility;productivity;awesome</PackageTags>

  <!-- Custom winget properties -->
  <WinGetPackageExecutable>MyAwesomeApp.exe</WinGetPackageExecutable>
  <WinGetCommandAlias>awesome</WinGetCommandAlias>
  <FileExtensions>txt;json;xml</FileExtensions>
</PropertyGroup>
```

### Project Type Detection

| Project Type | Detection Method | Properties Extracted |
|-------------|-----------------|-------------|
| C# | .csproj files | MSBuild properties, assembly info |
| Node.js | package.json | name, description, version, author |
| Rust | Cargo.toml | name, version, description |

## Example Workflow

After creating a release on GitHub:

```powershell
cd MyProject
./scripts/update-winget-manifests.ps1 -Version "1.2.3"
```

## Using Across Multiple Projects

Simply copy `update-winget-manifests.ps1` to your projects' scripts directory and run it - the script will auto-detect each project's settings.

## Advanced Features

### Customizing File Extensions

The script looks for supported file extensions in several places:

1. `FileExtensions` element in .csproj for .NET projects
2. `<None Include="**/*.ext">` patterns in project files
3. File extensions in TAGS.md
4. Default extensions based on project type

### Release Asset Pattern

By default, the script expects release assets to follow this pattern:
`{repo}-{version}-{arch}.zip` 

You can customize this with the `-ArtifactNamePattern` parameter.

## Package ID Resolution

The winget Package ID is determined in this priority order:

1. `-PackageId` parameter (explicit override)
2. `packageId` from config file
3. `RootNamespace` from .csproj (via MSBuild evaluation)
4. Fallback to `{GitHubOwner}.{RepoName}`

For ktsu projects using `ktsu.Sdk`, the RootNamespace is automatically set to `ktsu.{ProjectName}`, resulting in package IDs like `ktsu.BuildMonitor`.

## .NET Runtime Dependency

For C# projects, the script automatically adds a dependency on `Microsoft.DotNet.DesktopRuntime.10` in the installer manifest.

## Configuration Options (If Needed)

A configuration file is optional but can be used to override auto-detected settings:

```json
{
  "packageId": "myorg.MyApp",
  "githubRepo": "myorg/myapp",
  "artifactNamePattern": "{name}-{version}-{arch}.zip",
  "executableName": "MyApp.exe",
  "commandAlias": "myapp",
  "packageName": "My Application",
  "publisher": "My Organization",
  "shortDescription": "Short description of the application",
  "description": "Detailed multi-line description of the application",
  "fileExtensions": ["txt", "json"],
  "tags": ["utility", "productivity"]
}
```

## Placeholders in artifactNamePattern

The `artifactNamePattern` supports the following placeholders:

- `{name}`: Repository name 
- `{version}`: Version number
- `{arch}`: Architecture (win-x64, win-x86, win-arm64)

## Next Steps After Running the Script

1. Review the updated manifest files
2. Test the manifests locally with: `winget install --manifest ./winget`
3. Submit to winget-pkgs repository: https://github.com/microsoft/winget-pkgs
4. Create a PR following the winget contribution guidelines
