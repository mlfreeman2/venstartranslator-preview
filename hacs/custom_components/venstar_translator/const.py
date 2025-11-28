"""Constants for the Venstar Translator integration."""

DOMAIN = "venstar_translator"

# Storage
STORAGE_VERSION = 1
STORAGE_KEY = "venstar_translator"

# Sensor limits
MAX_SENSORS = 20
MAX_NAME_LENGTH = 14

# Broadcast settings
UDP_PORT = 5001
BROADCAST_ADDRESS = "255.255.255.255"
BROADCAST_REPEAT_COUNT = 5

# Broadcast intervals (seconds)
OUTDOOR_INTERVAL = 300  # 5 minutes
DEFAULT_INTERVAL = 60   # 1 minute

# Sensor purposes
PURPOSE_OUTDOOR = "Outdoor"
PURPOSE_REMOTE = "Remote"
PURPOSE_RETURN = "Return"
PURPOSE_SUPPLY = "Supply"

VALID_PURPOSES = [
    PURPOSE_OUTDOOR,
    PURPOSE_REMOTE,
    PURPOSE_RETURN,
    PURPOSE_SUPPLY,
]

# Temperature scales
SCALE_FAHRENHEIT = "F"
SCALE_CELSIUS = "C"

VALID_SCALES = [
    SCALE_FAHRENHEIT,
    SCALE_CELSIUS,
]

# Protobuf firmware version
FW_MAJOR = 4
FW_MINOR = 2
