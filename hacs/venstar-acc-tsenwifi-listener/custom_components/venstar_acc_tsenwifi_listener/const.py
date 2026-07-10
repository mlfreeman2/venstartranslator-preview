"""Constants for the Venstar ACC-TSENWIFI Listener integration."""

DOMAIN = "venstar_acc_tsenwifi_listener"

UDP_PORT = 5001
DEFAULT_BIND_ADDRESS = "0.0.0.0"   # must stay wildcard: binding a specific unicast
                                   # address does NOT receive 255.255.255.255 broadcasts

# dispatcher signals
SIGNAL_NEW_DEVICE = f"{DOMAIN}_new_device"
SIGNAL_UPDATE = DOMAIN + "_update_{}"   # .format(mac)
SIGNAL_AVAILABILITY = f"{DOMAIN}_availability_tick"   # payload-less; staleness sweep (§6e)

# options
CONF_PORT = "port"
CONF_IGNORE_LOCAL_EMULATED = "ignore_local_emulated"   # §2.10, default False
EMITTER_DOMAIN = "venstar_acc_tsenwifi_emulator"   # co-installed emulator detection (§6c step 4)

# staleness thresholds (seconds) — mirror the thermostat's own error timing
STALE_OUTDOOR = 20 * 60   # outdoor sensors broadcast every 5 min
STALE_DEFAULT = 5 * 60    # everything else broadcasts every 1 min

# how often the staleness sweep re-evaluates availability (§6e)
AVAILABILITY_SCAN_INTERVAL = 60

STORAGE_VERSION = 1
STORAGE_KEY = DOMAIN

# purpose labels (match the emulator)
PURPOSE_OUTDOOR = "Outdoor"
PURPOSE_REMOTE = "Remote"
PURPOSE_RETURN = "Return"
PURPOSE_SUPPLY = "Supply"
DEFAULT_PURPOSE = PURPOSE_REMOTE   # for absent/unknown INFO.Type — see §2.6

# temperature index bounds on the wire (§6c step 5)
TEMP_INDEX_MAX = 253       # highest valid temperature index (86.5 °C)
FAULT_SHORTED = 254        # shorted-sensor sentinel
FAULT_OPEN = 255           # open-sensor sentinel

# device metadata
MANUFACTURER = "Venstar"
# The wire's model field says only TEMPSENSOR, so the two parts can't be told
# apart from packets — surface both.
MODEL = "ACC-TSENWIFI / ACC-TSENWIFIPRO"
