# Venstar Translator - Home Assistant Integration

> **Status**: Work in Progress - Not yet functional

This directory contains the Home Assistant Custom Component (HACS) version of VenstarTranslator.

## Overview

This integration allows Home Assistant to emulate Venstar wireless temperature sensors by broadcasting UDP packets to Venstar ColorTouch thermostats. It monitors HA temperature entities and translates them into the Venstar sensor protocol format.

## Differences from Docker Version

- **No HTTP/JSONPath**: Uses HA temperature entities directly
- **No JSON file**: Configuration via HA UI (Config Flow)
- **No Hangfire**: Uses Python asyncio scheduler
- **Random MAC**: Generated once and persisted (no FakeMacPrefix)
- **Native HA UI**: Add/edit/delete sensors through Settings

## Directory Structure

```
custom_components/venstar_translator/
├── __init__.py                 # Integration setup & coordinator
├── manifest.json               # Integration metadata
├── config_flow.py             # UI configuration flow
├── const.py                   # Constants
├── strings.json               # UI text strings
├── translations/
│   └── en.json               # English translations
├── services.yaml             # Service definitions
├── protobuf/
│   ├── sensor_message.proto  # Protobuf definition
│   └── venstar_pb2.py        # Compiled protobuf (generated)
├── venstar_sensor.py         # Core sensor logic
└── coordinator.py            # Broadcast scheduler
```

## Development Status

See [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md) for detailed implementation plan.

### Current Phase: Foundation (Phase 1)

- [x] Directory structure created
- [x] `manifest.json` defined
- [x] Constants defined in `const.py`
- [x] Protobuf definition written
- [ ] Protobuf compiled to Python
- [ ] Temperature lookup tables implemented
- [ ] MAC generation logic implemented
- [ ] Config flow skeleton
- [ ] Storage layer implemented

## Building Protobuf

To compile the protobuf definition:

```bash
cd custom_components/venstar_translator/protobuf
protoc --python_out=. sensor_message.proto
```

This will generate `venstar_pb2.py`.

## Installation (Not Yet Available)

> This integration is not yet functional. Do not install.

Once complete, installation will be via HACS:

1. Add this repository to HACS as a custom repository
2. Install "Venstar Translator" from HACS
3. Restart Home Assistant
4. Go to Settings → Devices & Services → Add Integration
5. Search for "Venstar Translator"

## License

See parent directory LICENSE file.
