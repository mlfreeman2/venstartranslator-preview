# Venstar Translator

## What is this?

Venstar Translator bridges the gap between your existing temperature sensors and Venstar ColorTouch thermostats. If you have temperature data from Home Assistant, Ecowitt weather stations, or any other system with a JSON API, this app lets you feed that data directly to your Venstar thermostat as if they were official Venstar ACC-TSENWIFIPRO sensors.

**Key Features:**
- Emulate up to 20 Venstar wireless temperature sensors per instance
- Pull temperature data from any JSON API endpoint (Home Assistant, Ecowitt, custom APIs, etc.)
- Web UI for easy sensor configuration and testing
- Support for custom HTTP headers (authentication tokens, API keys)
- Both Fahrenheit and Celsius supported
- Multiple sensor purposes: Outdoor, Remote, Return, Supply

## Quick Start

### Prerequisites

- Docker and Docker Compose installed
- Your Docker host **must be on the same VLAN/network** as your Venstar thermostat (UDP broadcast requirement)
- A source for temperature data with a JSON API (Home Assistant, weather station, etc.)

### Installation

1. **Create a directory for your configuration:**

```bash
mkdir -p ~/venstartranslator/data
cd ~/venstartranslator
```

2. **Create a `docker-compose.yml` file:**

```yaml
services:
  venstartranslator:
    container_name: venstartranslator
    image: ghcr.io/mlfreeman2/venstartranslator-preview:main
    restart: unless-stopped
    volumes:
      - "./data:/data"
    environment:
      TZ: "America/New_York"  # Change to your timezone
      SensorFilePath: "/data/sensors.json"
      Kestrel__Endpoints__Http__Url: "http://*:8080"
    network_mode: host  # Required for UDP broadcast
    logging:
      options:
        max-size: "10m"
        max-file: "5"
```

3. **Start the container:**

```bash
docker compose up -d
```

4. **Open the web UI:**

Navigate to `http://your-docker-host-ip:8080` in your browser.

## Configuration

### Setting Up Your First Sensor

1. **Test your JSONPath query** (recommended):
   - Click "Test JSON Path" in the upper right corner
   - Paste a sample JSON response from your temperature API
   - Test different JSONPath queries until you extract the correct temperature value
   - See [JSONPath Examples](#jsonpath-examples) below for common patterns

2. **Add a new sensor**:
   - Click "Add New Sensor"
   - Fill in the form:
     - **Name**: Display name (max 14 characters, shown on thermostat)
     - **Broadcast Sensor**: Enable broadcasting temperature data to thermostats
     - **Purpose**:
       - `Outdoor` - Broadcasts every 5 minutes
       - `Remote`, `Return`, `Supply` - Broadcast every minute
     - **Scale**: `F` (Fahrenheit) or `C` (Celsius)
     - **URL**: Your JSON API endpoint
     - **JSONPath**: The path to extract temperature value (e.g., `$.state`)
     - **Ignore SSL Errors**: Enable for self-signed certificates
     - **Headers**: Add authentication if needed (e.g., `Authorization: Bearer YOUR_TOKEN`)

3. **Test the sensor**:
   - Click "Get Temperature" to verify it's pulling the correct value
   - If it fails, check your URL, JSONPath, and headers

4. **Pair with your thermostat**:
   - Click "Send Pairing Packet" for the sensor
   - Walk to your thermostat within 60 seconds
   - Go to thermostat settings and add the new wireless sensor
   - It should appear with the name you configured

### JSONPath Examples

**New to JSONPath?** The easiest way to get started is to ask an AI:

1. Get a sample JSON response from your API (copy from browser, curl, or Postman)
2. Use one of these prompts with ChatGPT, Claude, or any LLM:

```
I have this JSON response from my temperature sensor API:

[paste your JSON here]

I need a JSONPath query (for Json.NET/Newtonsoft.Json) to extract the temperature value.
The temperature is [describe where it is, e.g., "in the 'state' field" or "in the
array of sensors where name equals 'Living Room'"].

Please provide just the JSONPath query.
```

**Alternative prompt if you're not sure where the temperature is:**
```
I have this JSON response:

[paste your JSON here]

Can you identify which field contains the temperature reading and provide a
JSONPath query (for Json.NET/Newtonsoft.Json) to extract it?
```

3. Test the JSONPath in VenstarTranslator's web UI:
   - Click "Test JSON Path" button (upper right)
   - Paste your full JSON response in the document field
   - Paste the JSONPath query the LLM gave you
   - Click "Test" to verify it extracts the right value

**Common JSONPath patterns you'll encounter:**

| Pattern | What it does | Example |
|---------|--------------|---------|
| `$.state` | Get the "state" field at root level | Simple sensor value |
| `$.data.temperature` | Navigate nested objects | Get temperature from data object |
| `$.sensors[0].temp` | Get first array element | First sensor's temperature |
| `$.sensors[?(@.name=='Living Room')].temp` | Filter array by field value | Find sensor by name |
| `$.common_list[?(@.id=='0x02')].val` | Filter by ID | Ecowitt outdoor sensor |

#### Home Assistant API

Home Assistant REST API returns sensor state in a simple format:

```json
{
  "state": "72.5",
  "attributes": { ... }
}
```

**JSONPath:** `$.state`

**Full Example:**
```json
{
  "Name": "Living Room",
  "Enabled": true,
  "Purpose": "Remote",
  "Scale": "F",
  "URL": "http://homeassistant.local:8123/api/states/sensor.living_room_temperature",
  "JSONPath": "$.state",
  "Headers": [
    {
      "Name": "Authorization",
      "Value": "Bearer YOUR_LONG_LIVED_ACCESS_TOKEN"
    }
  ]
}
```

**Getting a Home Assistant Token:**
1. In Home Assistant, go to your profile (bottom left)
2. Scroll to "Long-Lived Access Tokens"
3. Click "Create Token"
4. Copy the token and use it in the `Authorization` header

#### Ecowitt GW2000/GW1100 Weather Station

Ecowitt stations expose a `/get_livedata_info` endpoint with nested sensor data:

**Outdoor sensor:**
```json
{
  "common_list": [
    { "id": "0x02", "val": "45.7", "unit": "°F" }
  ]
}
```
**JSONPath:** `$.common_list[?(@.id=='0x02')].val`

**Named indoor sensor:**
```json
{
  "ch_aisle": [
    { "channel": "1", "name": "Living Room", "temp": "72.3" }
  ]
}
```
**JSONPath:** `$.ch_aisle[?(@.name=='Living Room')].temp`

**Station onboard sensor:**
```json
{
  "wh25": [
    { "intemp": "68.9", "inhumi": "45" }
  ]
}
```
**JSONPath:** `$.wh25[0].intemp`

See `VenstarTranslator/sensors.ecowitt.json.sample` and `VenstarTranslator/sensors.homeassistant.json.sample` for complete examples.

### Advanced Configuration

#### Multiple Instances (60+ sensors)

If you need more than 20 sensors, run multiple instances with different `FakeMacPrefix` values:

```yaml
# Instance 1 (sensors 0-19)
environment:
  FakeMacPrefix: "428e0486d8"

# Instance 2 (sensors 20-39) - change last character to "7"
environment:
  FakeMacPrefix: "428e0486d7"

# Instance 3 (sensors 40-59) - change last character to "9"
environment:
  FakeMacPrefix: "428e0486d9"
```

**⚠️ Warning:** Changing `FakeMacPrefix` after pairing will break existing sensor connections. You'll need to re-pair all sensors.

#### Manual sensors.json Configuration

While the web UI is recommended, you can also edit `./data/sensors.json` directly. The application validates and normalizes the file on startup. If validation fails, check the container logs:

```bash
docker logs venstartranslator
```

## Troubleshooting

### Thermostat doesn't see sensors

- **Check network mode**: Must use `network_mode: host` in Docker Compose
- **Verify VLAN**: Docker host and thermostat must be on the same broadcast domain
- **Test sensor**: Click "Get Temperature" in web UI to verify data is being fetched
- **Re-pair**: Click "Send Pairing Packet" and add the sensor on the thermostat within 60 seconds

### "Get Temperature" fails

- **Check URL**: Ensure the API endpoint is reachable from the Docker host
- **Verify JSONPath**: Use the "Test JSON Path" tool to validate your query
- **Headers**: Make sure authentication tokens/API keys are correct
- **SSL errors**: Enable "Ignore SSL Errors" for self-signed certificates
- **Timeout**: HTTP requests timeout after 10 seconds. If your API is slow to respond, the request will be cancelled and the sensor will fail to update

### Sensor data not updating

- Check container logs: `docker logs venstartranslator`
- Verify "Broadcast Sensor" is checked for the sensor in the web UI
- Check that the Hangfire dashboard shows scheduled jobs: `http://your-host:8080/hangfire`

## How It Works

1. **Scheduled fetching**: Background jobs query your JSON APIs on a schedule (outdoor sensors every 5 minutes, others every minute)
2. **Temperature extraction**: JSONPath queries extract the temperature value from the JSON response (HTTP requests have a 10-second timeout)
3. **Protocol translation**: Temperature is converted to Venstar's protocol format
4. **UDP broadcast**: Protobuf-encoded packets are broadcast to `255.255.255.255:5001` where Venstar thermostats listen

The thermostat receives these packets exactly as if they came from genuine Venstar wireless sensors.

## Files and Backup

All sensor configurations are stored in `./data/sensors.json`. This is the only file you need to back up. The application also creates SQLite databases in the container for internal state, but these are regenerated from `sensors.json` on startup.



### Other
If you want to manually download Venstar thermostat firmware and poke around like I did, start at 
https://files.skyportlabs.com/ct1_firmware/venstar/firmware.json


