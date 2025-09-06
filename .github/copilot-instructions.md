# Orcanode Monitor - GitHub Copilot Instructions

Orcanode Monitor is an ASP.NET Core 8.0 web application that monitors the liveness of [orcanode](https://github.com/orcasound/orcanode) audio streaming nodes. It provides a web dashboard for tracking orcanode status, uptime history, and real-time notifications via IFTTT when problems are detected.

**ALWAYS reference these instructions first and fallback to search or bash commands only when you encounter unexpected information that does not match the info here.**

## Technology Stack
- **Backend**: ASP.NET Core 8.0 with Razor Pages
- **Database**: Azure Cosmos DB with Entity Framework Core
- **Audio Processing**: FFMpegCore, NAudio, FftSharp for stream analysis
- **Deployment**: Azure App Service (Production: https://orcanodemonitor.azurewebsites.net/, Staging: https://orcanodemonitorstaging.azurewebsites.net/)

## Working Effectively

### Prerequisites
- .NET 8.0 SDK (available by default in GitHub Actions)
- FFmpeg binaries (automatically installed via FFMpegInstaller.Windows.x64 NuGet package during build)
- Azure Cosmos DB credentials (for full functionality)

### Building and Testing
- **Restore packages**: `dotnet restore` -- takes ~33 seconds. NEVER CANCEL. Set timeout to 60+ seconds.
- **Build solution**: `dotnet build` -- takes ~16 seconds. NEVER CANCEL. Set timeout to 45+ seconds.
- **Format code**: `dotnet format` -- takes ~15 seconds. NEVER CANCEL. Set timeout to 30+ seconds.
- **Lint YAML**: `yamllint .` -- takes <1 second
- **Run tests**: `dotnet test --no-build --verbosity normal` -- takes ~3 seconds. NEVER CANCEL. Set timeout to 30+ seconds.

### Test Requirements
Tests require Azure Cosmos DB credentials as environment variables:
```bash
export AZURE_COSMOS_CONNECTIONSTRING="your-connection-string"
export AZURE_COSMOS_DATABASENAME="your-database-name"
```

**Tests that will fail without credentials:**
- `CanReadProductionAsync`, `CanReadStagingAsync`, `CanReadDevelopmentAsync` - require Azure Cosmos DB access
- `TestSilentSample`, `TestHysteresisBehavior` - require FFmpeg binaries (auto-installed during build)

**Tests that pass without credentials:**
- `TestHumFrequencies_*` tests - frequency analysis unit tests

### Running the Application
```bash
cd OrcanodeMonitor
dotnet run --urls "http://localhost:5000"
```

**Application will start with warnings but run in development mode** even without Azure credentials. It will use dummy/placeholder data.

### Environment Variables
- `AZURE_COSMOS_CONNECTIONSTRING` - Azure Cosmos DB connection string
- `AZURE_COSMOS_DATABASENAME` - Database name (defaults to "orcasound-cosmosdb")
- `ORCANODE_MONITOR_READONLY` - Set to "true" for read-only mode
- `ORCASOUND_DATAPLICITY_TOKEN` - Required for Dataplicity API access
- `ASPNETCORE_ENVIRONMENT` - Set to "Development", "Staging", or "Production"

## Project Structure
```
OrcanodeMonitor/           # Main web application
├── Api/                   # API controllers
├── Core/                  # Business logic (Fetcher, PeriodicTasks, etc.)
├── Data/                  # Entity Framework contexts
├── Models/                # Data models (Orcanode, OrcanodeEvent, etc.)
├── Pages/                 # Razor Pages UI
└── Program.cs             # Application entry point

Test/                      # Unit tests
├── CosmosTests.cs         # Database integration tests
├── UnintelligibilityTests.cs # Audio analysis tests
└── samples/               # Test audio files

docs/                      # Documentation
├── Design.md              # Architecture overview
└── Debugging.md           # Troubleshooting guide
```

## Common Tasks and Validation

### Always run these steps before committing:
1. **Format code**: `dotnet format`
2. **Build solution**: `dotnet build`
3. **Run tests**: `dotnet test` (expect some failures without Azure credentials)
4. **Lint YAML**: `yamllint .`

### Validation Scenarios
After making changes, ALWAYS test these scenarios:

1. **Build Validation**: Ensure solution builds without errors
   ```bash
   dotnet build
   # Should complete successfully with only warnings
   ```

2. **Application Startup**: Verify app starts and basic pages load
   ```bash
   cd OrcanodeMonitor
   timeout 30 dotnet run --urls "http://localhost:5000"
   # Should show "Background task executed" and database connection attempts
   ```

3. **Unit Tests**: Run non-Azure dependent tests
   ```bash
   dotnet test --filter "TestHumFrequencies"
   # Should pass all frequency analysis tests
   ```

## Build Timing and Warnings

### Expected Build Times (with 50% buffer for timeouts):
- **dotnet restore**: 33 seconds (set timeout: 60+ seconds)
- **dotnet build**: 16 seconds (set timeout: 45+ seconds)  
- **dotnet format**: 15 seconds (set timeout: 30+ seconds)
- **dotnet test**: 3 seconds (set timeout: 30+ seconds)
- **yamllint**: <1 second

### **CRITICAL: NEVER CANCEL builds or tests.** Always wait for completion.

### Expected Build Warnings
The build will show ~15 warnings related to:
- Nullable reference types (CS8604, CS8603, CS8600, CS8618, CS8629, CS8605)
- Obsolete test attributes (MSTEST0044)
- Formatting issues (WHITESPACE errors if not formatted)

These warnings are expected and do not prevent successful builds.

## Troubleshooting Common Issues

### Application Won't Start
- **Missing Azure credentials**: Expected - app will run with warnings but work in development mode
- **FFmpeg not found**: Run `dotnet build` first to install FFmpeg via NuGet package
- **Port conflicts**: Use `--urls "http://localhost:XXXX"` with different port

### Test Failures
- **Cosmos DB tests fail**: Expected without `AZURE_COSMOS_CONNECTIONSTRING` environment variable
- **FFmpeg tests fail**: Run `dotnet build` first to ensure FFmpeg binaries are installed
- **"File not found" errors**: Ensure you're in the repository root directory

### Code Formatting Issues
```bash
# Check formatting
dotnet format --verify-no-changes

# Fix formatting automatically  
dotnet format
```

## CI/CD Integration
The repository uses GitHub Actions workflows:
- **build.yml**: Builds and tests on Windows with Azure credentials
- **validate-yaml.yml**: YAML linting validation
- Automatic deployments to staging (on PR merge) and production (on tagged releases)

## Key Dependencies
- **Microsoft.EntityFrameworkCore.Cosmos**: Database access
- **FFMpegCore**: Audio stream processing  
- **NAudio**: Audio analysis
- **FftSharp**: Frequency analysis
- **Microsoft.AspNetCore.Diagnostics.EntityFrameworkCore**: Development diagnostics

## Development Notes
- The application monitors multiple data sources: Dataplicity, Orcasound live feeds, S3 buckets, OrcaHello, and Mezmo
- Background tasks run periodically to update orcanode status
- Audio analysis detects unintelligible streams using standard deviation calculations
- Web UI provides real-time dashboard and historical uptime data

## Manual Testing After Changes
1. Build and run the application locally
2. Navigate to http://localhost:5000 
3. Verify the main dashboard loads (may show placeholder data without Azure credentials)
4. Check that no unhandled exceptions occur in the console output
5. Test any specific functionality you modified

Remember: **ALWAYS validate your changes work before committing.** Run the build, format, and test commands to ensure code quality.