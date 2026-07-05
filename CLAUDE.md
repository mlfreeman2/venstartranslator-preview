# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

VenstarTranslator is an ASP.NET Core 10.0 application that fetches temperature readings from arbitrary JSON endpoints and translates them into the format expected by Venstar ColorTouch thermostats. It emulates up to 20 Venstar ACC-TSENWIFIPRO sensors by broadcasting UDP packets on port 5001. The application uses Protocol Buffers for data serialization and broadcasts packets to `255.255.255.255:5001`.

## Build and Run Commands

### Local Development
```bash
# Build the solution
dotnet build VenstarTranslator.sln

# Run the application (from the VenstarTranslator directory)
cd VenstarTranslator
dotnet run

# Clean build artifacts (removes SQLite databases too)
dotnet clean VenstarTranslator.sln
```

### Docker
```bash
# Build Docker image (multi-platform)
docker build --platform linux/amd64 -f Dockerfile -t venstartranslator .

# Run with Docker Compose (requires sensors.json file)
# See docker-compose.yml.sample in the root directory for reference configuration
docker compose up
```

The application runs on port 8080 by default (HTTP). The web UI is accessible at `http://localhost:8080`. HTTPS support is available on port 8443 when enabled via environment variables (see Configuration section).

## Architecture

### Core Components

**TranslatedVenstarSensor** (`Models/Db/TranslatedVenstarSensor.cs:20`)
- Main domain model representing a sensor configuration
- Extracts temperature values from fetched JSON documents via JSONPath
- Contains Protocol Buffer serialization logic for both pairing and data packets
- Manages HMAC-SHA256 signatures for authenticated sensor communications
- Temperature lookup tables map sensor readings to Venstar's expected format (Fahrenheit and Celsius)

**Services** (`Services/`)
- `SensorOperations`: Orchestrates the fetch → extract → build packet → broadcast flow for data, pairing, and resend packets; static `SyncToJsonFile()` writes the database back to `sensors.json`
- `HttpDocumentFetcher`: Fetches JSON from sensor URLs (10-second timeout, custom headers, optional SSL bypass)
- `UdpBroadcaster`: Broadcasts packets to `255.255.255.255:5001`
- `HangfireJobManager`: Adds/removes recurring Hangfire jobs
- `SettingsService`: Reads/writes `settings.json` (stored next to `sensors.json`); migrates the legacy `healthChecksBaseUrl` field to the mode-based format
- `HealthChecksClient`: healthchecks.io success/failure pings and management API operations (create/rename/pause/resume/delete checks)

**Program.cs**
- Application configuration and dependency injection setup
- On startup, loads `sensors.json` (configured via `SensorFilePath` environment variable)
- Validates all sensors (JSONPath syntax, URL format, duplicate names, 20 sensor limit)
- Syncs validated sensors to SQLite database (`VenstarTranslatorDataCache`)
- Configures Hangfire for scheduled UDP broadcasts
- Configures optional HTTPS support with user-provided or auto-generated self-signed certificates

**Tasks.cs** (`Tasks/Tasks.cs`)
- Background job definitions executed by Hangfire
- `SendDataPacket(uint sensorID)`: Fetches temperature from configured URL and broadcasts UDP packet
- Outdoor sensors broadcast every 5 minutes (`*/5 * * * *`)
- All other sensor types broadcast every minute (`* * * * *`)

**APIController.cs** (`Controllers/APIController.cs`)
- REST API for sensor CRUD operations and application settings
- All modifications to sensors automatically sync to `sensors.json` via `SensorOperations.SyncToJsonFile()`
- `/api/testjsonpath`: Test JSONPath queries against sample JSON documents
- `/api/fetchurl`: Fetch and display JSON from a configured sensor's URL
- `/api/sensors/{id}/pair`: Send pairing packet to thermostat
- `/api/sensors/{id}/latest`: Test temperature fetch from data source
- `/api/sensors/{id}/resend`: Rebroadcast the last cached packet
- `/api/settings` (GET/PUT): Application settings (instance name, healthchecks.io configuration)
- Sensor and settings changes drive the healthchecks.io check lifecycle (create/rename/pause/resume/delete) when a management API key is configured

### Data Flow

1. Sensors are loaded from `sensors.json` on startup (environment variable `SensorFilePath`)
2. Validated sensors are stored in SQLite (`VenstarTranslatorDataCache.db`)
3. Hangfire schedules recurring jobs for each enabled sensor
4. Background jobs fetch temperature via HTTP, extract value using JSONPath
5. Temperature is mapped to Venstar's lookup table format
6. Protocol Buffer packet is serialized with HMAC-SHA256 signature
7. UDP broadcast sent 5 times to `255.255.255.255:5001`
8. Success or failure is pinged to healthchecks.io after each broadcast attempt (when configured)

### Key Technologies

- **ASP.NET Core 10.0**: Web framework and API
- **Entity Framework Core**: SQLite database for sensor persistence
- **Hangfire**: Background job scheduling with SQLite storage
- **Newtonsoft.Json**: JSON parsing and JSONPath queries (`SelectToken`)
- **Protocol Buffers** (`protobuf-net` and `Google.Protobuf`): Sensor packet serialization
- **UnitsNet**: Temperature unit conversions

### Database Migrations

The application uses Entity Framework Core migrations for database schema management. Migrations are applied automatically on startup via `Database.Migrate()`.

**For Users:**
- Migrations are applied automatically on first run
- Existing databases created with `EnsureCreated()` will be seamlessly upgraded to use migrations
- No manual intervention required

**For Developers:**
When modifying the database schema:
```bash
# Install EF Core tools (one-time)
dotnet tool install --global dotnet-ef

# Create a new migration
cd VenstarTranslator
dotnet ef migrations add YourMigrationName --context VenstarTranslatorDataCache

# Migrations are applied automatically on app startup
# To manually apply migrations:
dotnet ef database update --context VenstarTranslatorDataCache
```

The `__EFMigrationsHistory` table tracks which migrations have been applied. The migration system is idempotent - running migrations multiple times is safe.

## Configuration

Environment variables (can be set in `appsettings.json` or Docker Compose):

### Required Configuration
- `SensorFilePath`: Path to sensors.json file (required, e.g., `/data/sensors.json`)

### HTTP/HTTPS Configuration
- `Kestrel__Endpoints__Http__Url`: HTTP endpoint (default: `http://*:8080`)
- `Kestrel__Endpoints__Https__Url`: HTTPS endpoint (optional, e.g., `https://*:8443`)
- `HTTPS_CERTIFICATE_PATH`: Path to custom SSL/TLS certificate in PFX format (optional)
- `HTTPS_CERTIFICATE_PASSWORD`: Password for custom certificate (optional)

**HTTPS Behavior:**
- HTTPS is **disabled by default**. Enable by setting `Kestrel__Endpoints__Https__Url`
- If HTTPS is enabled but no certificate is provided via `HTTPS_CERTIFICATE_PATH`, a self-signed certificate will be auto-generated and saved to `/data/self-signed-cert.pfx`
- Self-signed certificates are valid for 5 years and include Subject Alternative Names for localhost
- For production use, provide a proper CA-signed certificate via `HTTPS_CERTIFICATE_PATH`
- Self-signed certificates will trigger browser warnings and require manual trust/import

### Other Configuration
- `FakeMacPrefix`: 10-character hex prefix for fake MAC addresses (default: `428e0486d8`)
- `ConnectionStrings__Hangfire`: Hangfire SQLite database path
- `ConnectionStrings__DataCache`: Sensor data SQLite database path

**FakeMacPrefix**: Change the last character (e.g., `428e0486d7`, `428e0486d9`) to run multiple instances with different sensor ID ranges. Each instance supports 20 sensors (IDs 0-19).

## Application Settings

Global settings are edited via the web UI Settings dialog (gear icon) and persisted by `SettingsService` to `settings.json` in the same directory as `sensors.json`:
- **InstanceName**: Shown in the page header/browser tab; required when a healthchecks.io management API key is configured
- **HealthChecksMode**: `none` (default), `saas` (hosted healthchecks.io), or `selfhosted`
- **HealthChecksSelfHostedUrl**: Base URL of a self-hosted healthchecks instance (only used in `selfhosted` mode)
- **HealthChecksApiKey**: Optional management API key

### Healthchecks.io Integration
- Ping URL construction is centralized in `HealthChecksClient.GetPingUrl()`: SaaS pings go to `https://hc-ping.com/{uuid}` (no path prefix), self-hosted pings go to `{baseUrl}/ping/{uuid}`
- Success pings are `GET` to the ping URL; failure pings are `POST` to `{pingUrl}/fail` with error details in the body (truncated to 10 KB)
- Pings are fired by `BroadcastTrackingFilter` after every broadcast attempt for sensors with a `HealthCheckUuid`
- Management API: SaaS uses `https://healthchecks.io/api/v3`; self-hosted derives `{scheme}://{host}[:port]/api/v3` from the configured URL
- With an API key and instance name configured, checks are managed automatically: created for new sensors (timeout/grace 300s/1200s for Outdoor, 60s/300s for others), renamed when a sensor or the instance is renamed, paused/resumed on disable/enable, deleted with the sensor
- On startup and on settings save, checks are backfilled for any sensors missing a UUID

## Sensor Configuration

Sensors are defined in `sensors.json` and validated on startup. The web UI at port 8080 provides a CRUD interface and JSONPath tester.

### Sensor Properties
- **SensorID** (0-19): Auto-assigned by the application
- **Name**: Max 14 characters, must be unique
- **Enabled**: Controls Hangfire job scheduling
- **Purpose**: Affects broadcast frequency and thermostat behavior
  - `Outdoor` - Every 5 minutes (display only, NOT used for HVAC control)
  - `Remote` - Every minute (used for HVAC control when enabled on thermostat)
  - `Return` - Every minute (return air temperature monitoring)
  - `Supply` - Every minute (supply air temperature monitoring)
- **Scale**: `F` (Fahrenheit) or `C` (Celsius)
- **URL**: HTTP/HTTPS endpoint returning JSON
- **JSONPath**: Query to extract temperature value (supports Newtonsoft.Json syntax)
- **IgnoreSSLErrors**: Skip certificate validation (e.g. for self-signed HTTPS data sources)
- **Headers**: Array of HTTP headers (`Name`, `Value`) for authenticated endpoints
- **HealthCheckUuid**: Optional healthchecks.io check UUID; auto-populated when a management API key is configured, or set manually for hand-created checks

See `sensors.json.template`, `sensors.ecowitt.json.sample`, or `sensors.homeassistant.json.sample` in the root directory for examples.

## Important Implementation Details

### Sensor Validation
- Enforced via Data Annotations and `IValidatableObject`
- JSONPath validation uses custom `ValidJsonPath` attribute (`Models/Validation/ValidJsonPathAttribute.cs`)
- URL validation via `ValidAbsoluteUrl` attribute (`Models/Validation/ValidAbsoluteUrlAttribute.cs`)
- HTTP headers validated with `ValidHttpHeaders` attribute (`Models/Validation/ValidHttpHeadersAttribute.cs`)
- Validation orchestrated in `Program.cs` via `ValidateIndividualSensors()` method

### Temperature Packet Serialization
- Protobuf models defined in `Models/Protobuf/ProtobufNetModel.cs`
- Signature generation uses SHA256 hash of MAC address as HMAC key
- Sequence numbers increment with each packet (reset at 65000)
- Pairing packets use sequence number 1 with base64-encoded SHA256 signature

### Database Synchronization
The application maintains two sources of truth:
1. **sensors.json**: Human-editable configuration file (persisted to disk)
2. **VenstarTranslatorDataCache.db**: Runtime database for EF Core and Hangfire

Changes via API automatically sync to both. On startup, sensors.json is read, validated, synced to database, and rewritten with normalized formatting.

## Network Requirements

The application MUST run on the same VLAN/broadcast domain as the Venstar thermostat. Docker deployments require `network_mode: host` to enable UDP broadcast capabilities.

## Monitoring and Problem Detection

The web UI provides real-time sensor health monitoring that mirrors the thermostat's error detection:
- **Problem Indicators**: Sensors with stale broadcasts display an orange pulsing "Problem" badge in the Status column
- **Staleness Thresholds** (matches when thermostat shows sensor error):
  - Outdoor sensors: 20 minutes (broadcasts every 5 minutes)
  - Other sensor types: 5 minutes (broadcasts every 1 minute)
- **Details**: Hover over the problem badge to see the last successful broadcast timestamp
- **Logs**: Full exception details are logged to console/Docker logs at ERROR level
- **Database Tracking**: `LastSuccessfulBroadcast` timestamp tracked in database (not persisted to sensors.json)
- **Auto-Recovery**: Problem indicator automatically clears when broadcasts resume successfully

**BroadcastTrackingFilter** (`Filters/BroadcastTrackingFilter.cs`)
- Hangfire job filter attribute applied to `SendDataPacket` method in Tasks.cs
- Updates `LastSuccessfulBroadcast` timestamp on successful broadcasts
- Tracks `ConsecutiveFailures` counter and `LastErrorMessage` in database
- Caches `LastPacketBytes` for manual resend capability
- Fires healthchecks.io success/failure pings (fire-and-forget) for sensors with a `HealthCheckUuid`
- Distinguishes between user-friendly `VenstarTranslatorException` messages (stored in database) and system exceptions (logged only)
- Logs errors with exception details on failures
- Logs warnings when broadcasts become stale (matching thermostat error threshold)

**Manual Packet Resend**
- Each sensor caches the last broadcast packet in the database (`LastPacketBytes`)
- "Resend Last Packet" button available in web UI for enabled sensors
- Resends the exact packet from the last broadcast (same sequence number, same temperature data)
- Useful for troubleshooting thermostat connectivity issues
- API endpoint: `POST /api/sensors/{id}/resend`
- Returns error if sensor has never broadcast a packet

## Web UI

Located in `web/` directory:
- `index.html`: Main sensor management interface with real-time problem indicators and the Settings modal
- `jsonpath.html`: JSONPath query tester
- `sensors.js`: Sensor table rendering, tooltip initialization, and Settings dialog logic
- `modals.js`: Modal dialog management
- `style.css`: Custom styling including problem badge animations
- Static files served via ASP.NET Core FileServer middleware
