"""Venstar sensor logic - packet building and UDP broadcasting."""
from __future__ import annotations

import base64
import hashlib
import hmac
import logging
import socket
from typing import TYPE_CHECKING

if TYPE_CHECKING:
    pass

from .const import (
    BROADCAST_ADDRESS,
    BROADCAST_REPEAT_COUNT,
    FW_MAJOR,
    FW_MINOR,
    PURPOSE_OUTDOOR,
    PURPOSE_REMOTE,
    PURPOSE_RETURN,
    PURPOSE_SUPPLY,
    SCALE_CELSIUS,
    SCALE_FAHRENHEIT,
    UDP_PORT,
)

_LOGGER = logging.getLogger(__name__)

# Temperature lookup tables from VenstarTranslator C# codebase
# Round the temperature and look it up here, then send that array index in the protobuf packet
TEMPERATURES_FAHRENHEIT = [
    "-40", "-39", "-38", "-37", "-36", "-36", "-35", "-34", "-33", "-32",
    "-31", "-30", "-29", "-28", "-27", "-27", "-26", "-25", "-24", "-23",
    "-22", "-21", "-20", "-19", "-18", "-18", "-17", "-16", "-15", "-14",
    "-13", "-12", "-11", "-10", "-9", "-9", "-8", "-7", "-6", "-5",
    "-4", "-3", "-2", "-1", "0", "1", "1", "2", "3", "4",
    "5", "6", "7", "8", "9", "10", "10", "11", "12", "13",
    "14", "15", "16", "17", "18", "19", "19", "20", "21", "22",
    "23", "24", "25", "26", "27", "28", "28", "29", "30", "31",
    "32", "33", "34", "35", "36", "37", "37", "38", "39", "40",
    "41", "42", "43", "44", "45", "46", "46", "47", "48", "49",
    "50", "51", "52", "53", "54", "55", "55", "56", "57", "58",
    "59", "60", "61", "62", "63", "64", "64", "65", "66", "67",
    "68", "69", "70", "71", "72", "73", "73", "74", "75", "76",
    "77", "78", "79", "80", "81", "82", "82", "83", "84", "85",
    "86", "87", "88", "89", "90", "91", "91", "92", "93", "94",
    "95", "96", "97", "98", "99", "100", "100", "101", "102", "103",
    "104", "105", "106", "107", "108", "109", "109", "110", "111", "112",
    "113", "114", "115", "116", "117", "118", "118", "119", "120", "121",
    "122", "123", "124", "125", "126", "127", "127", "128", "129", "130",
    "131", "132", "133", "134", "135", "136", "136", "137", "138", "139",
    "140", "141", "142", "143", "144", "145", "145", "146", "147", "148",
    "149", "150", "151", "152", "153", "154", "154", "155", "156", "157",
    "158", "159", "160", "161", "162", "163", "163", "164", "165", "166",
    "167", "168", "169", "170", "171", "172", "172", "173", "174", "175",
    "176", "177", "178", "179", "180", "181", "181", "182", "183", "184",
    "185", "186", "187", "188"
]

TEMPERATURES_CELSIUS = [
    "-40.0", "-39.5", "-39.0", "-38.5", "-38.0", "-37.5", "-37.0", "-36.5", "-36.0", "-35.5",
    "-35.0", "-34.5", "-34.0", "-33.5", "-33.0", "-32.5", "-32.0", "-31.5", "-31.0", "-30.5",
    "-30.0", "-29.5", "-29.0", "-28.5", "-28.0", "-27.5", "-27.0", "-26.5", "-26.0", "-25.5",
    "-25.0", "-24.5", "-24.0", "-23.5", "-23.0", "-22.5", "-22.0", "-21.5", "-21.0", "-20.5",
    "-20.0", "-19.5", "-19.0", "-18.5", "-18.0", "-17.5", "-17.0", "-16.5", "-16.0", "-15.5",
    "-15.0", "-14.5", "-14.0", "-13.5", "-13.0", "-12.5", "-12.0", "-11.5", "-11.0", "-10.5",
    "-10.0", "-9.5", "-9.0", "-8.5", "-8.0", "-7.5", "-7.0", "-6.5", "-6.0", "-5.5",
    "-5.0", "-4.5", "-4.0", "-3.5", "-3.0", "-2.5", "-2.0", "-1.5", "-1.0", "-0.5",
    "0.0", "0.5", "1.0", "1.5", "2.0", "2.5", "3.0", "3.5", "4.0", "4.5",
    "5.0", "5.5", "6.0", "6.5", "7.0", "7.5", "8.0", "8.5", "9.0", "9.5",
    "10.0", "10.5", "11.0", "11.5", "12.0", "12.5", "13.0", "13.5", "14.0", "14.5",
    "15.0", "15.5", "16.0", "16.5", "17.0", "17.5", "18.0", "18.5", "19.0", "19.5",
    "20.0", "20.5", "21.0", "21.5", "22.0", "22.5", "23.0", "23.5", "24.0", "24.5",
    "25.0", "25.5", "26.0", "26.5", "27.0", "27.5", "28.0", "28.5", "29.0", "29.5",
    "30.0", "30.5", "31.0", "31.5", "32.0", "32.5", "33.0", "33.5", "34.0", "34.5",
    "35.0", "35.5", "36.0", "36.5", "37.0", "37.5", "38.0", "38.5", "39.0", "39.5",
    "40.0", "40.5", "41.0", "41.5", "42.0", "42.5", "43.0", "43.5", "44.0", "44.5",
    "45.0", "45.5", "46.0", "46.5", "47.0", "47.5", "48.0", "48.5", "49.0", "49.5",
    "50.0", "50.5", "51.0", "51.5", "52.0", "52.5", "53.0", "53.5", "54.0", "54.5",
    "55.0", "55.5", "56.0", "56.5", "57.0", "57.5", "58.0", "58.5", "59.0", "59.5",
    "60.0", "60.5", "61.0", "61.5", "62.0", "62.5", "63.0", "63.5", "64.0", "64.5",
    "65.0", "65.5", "66.0", "66.5", "67.0", "67.5", "68.0", "68.5", "69.0", "69.5",
    "70.0", "70.5", "71.0", "71.5", "72.0", "72.5", "73.0", "73.5", "74.0", "74.5",
    "75.0", "75.5", "76.0", "76.5", "77.0", "77.5", "78.0", "78.5", "79.0", "79.5",
    "80.0", "80.5", "81.0", "81.5", "82.0", "82.5", "83.0", "83.5", "84.0", "84.5",
    "85.0", "85.5", "86.0", "86.5"
]


def get_temperature_index(temperature: float, scale: str) -> int:
    """Map temperature to Venstar's lookup table index.

    Args:
        temperature: Temperature value
        scale: "F" for Fahrenheit or "C" for Celsius

    Returns:
        Index in the temperature lookup table

    Raises:
        ValueError: If temperature is out of range for the lookup table
    """
    if scale == SCALE_FAHRENHEIT:
        rounded = str(round(temperature))
        lookup = TEMPERATURES_FAHRENHEIT
    elif scale == SCALE_CELSIUS:
        # Round to nearest 0.5°C (multiply by 2, round, divide by 2)
        rounded = f"{round(temperature * 2) / 2:.1f}"
        lookup = TEMPERATURES_CELSIUS
    else:
        raise ValueError(f"Invalid temperature scale: {scale}")

    try:
        return lookup.index(rounded)
    except ValueError as e:
        raise ValueError(
            f"Temperature {temperature}°{scale} out of range "
            f"(rounded to {rounded}, valid range: {lookup[0]} to {lookup[-1]})"
        ) from e


class VenstarSensor:
    """Represents a Venstar wireless temperature sensor.

    Handles packet building, signature generation, and sequence management.
    """

    def __init__(
        self,
        sensor_id: int,
        mac_prefix: str,
        name: str,
        purpose: str,
        scale: str,
        sequence: int = 1,
    ):
        """Initialize a Venstar sensor.

        Args:
            sensor_id: Sensor ID (0-19)
            mac_prefix: 10-character hex MAC prefix
            name: Sensor name (max 14 characters)
            purpose: Sensor purpose (Outdoor, Remote, Return, Supply)
            scale: Temperature scale ("F" or "C")
            sequence: Current sequence number (default: 1)
        """
        self.sensor_id = sensor_id
        self.mac_prefix = mac_prefix
        self.name = name
        self.purpose = purpose
        self.scale = scale
        self.sequence = sequence

    @property
    def mac_address(self) -> str:
        """Generate MAC address from prefix and sensor ID."""
        return f"{self.mac_prefix}{self.sensor_id:02x}".lower()

    @property
    def signature_key(self) -> str:
        """Generate HMAC key from MAC address (SHA256 hash, base64 encoded)."""
        mac_bytes = self.mac_address.encode('utf-8')
        sha256_hash = hashlib.sha256(mac_bytes).digest()
        return base64.b64encode(sha256_hash).decode('utf-8')

    def generate_signature(self, info_bytes: bytes) -> str:
        """Generate HMAC-SHA256 signature for INFO protobuf.

        Args:
            info_bytes: Serialized INFO protobuf message

        Returns:
            Base64-encoded HMAC-SHA256 signature
        """
        key = base64.b64decode(self.signature_key)
        signature = hmac.new(key, info_bytes, hashlib.sha256).digest()
        return base64.b64encode(signature).decode('utf-8')

    def _get_protobuf_type(self) -> int:
        """Map purpose string to protobuf SensorType enum value."""
        # Import here to avoid circular dependency
        from .protobuf import sensor_message_pb2

        if self.purpose == PURPOSE_OUTDOOR:
            return sensor_message_pb2.INFO.OUTDOOR
        elif self.purpose == PURPOSE_REMOTE:
            return sensor_message_pb2.INFO.REMOTE
        elif self.purpose == PURPOSE_RETURN:
            return sensor_message_pb2.INFO.RETURN
        elif self.purpose == PURPOSE_SUPPLY:
            return sensor_message_pb2.INFO.SUPPLY
        else:
            raise ValueError(f"Invalid sensor purpose: {self.purpose}")

    def build_data_packet(self, temperature: float) -> bytes:
        """Build protobuf data packet with HMAC signature.

        Args:
            temperature: Current temperature reading

        Returns:
            Serialized protobuf SensorMessage ready for UDP broadcast
        """
        # Import here to avoid circular dependency
        from .protobuf import sensor_message_pb2

        # Get temperature index for lookup table
        temp_index = get_temperature_index(temperature, self.scale)

        _LOGGER.debug(
            f"Building data packet for sensor {self.sensor_id} ({self.name}): "
            f"temp={temperature}°{self.scale}, temp_index={temp_index}, seq={self.sequence}"
        )

        # Build INFO message
        info = sensor_message_pb2.INFO(
            Sequence=self.sequence,
            SensorId=self.sensor_id,
            Mac=self.mac_address,
            FwMajor=FW_MAJOR,
            FwMinor=FW_MINOR,
            Model=sensor_message_pb2.INFO.TEMPSENSOR,
            Power=sensor_message_pb2.INFO.BATTERY,
            Name=self.name,
            Type=self._get_protobuf_type(),
            Temperature=temp_index,
            Battery=100
        )

        # Generate HMAC signature
        info_bytes = info.SerializeToString()
        signature = self.generate_signature(info_bytes)

        _LOGGER.debug(
            f"Sensor {self.sensor_id}: INFO bytes={len(info_bytes)}, "
            f"signature={signature[:16]}... (truncated)"
        )

        # Build SENSORDATA message
        sensor_data = sensor_message_pb2.SENSORDATA(
            Info=info,
            Signature=signature
        )

        # Build SensorMessage
        message = sensor_message_pb2.SensorMessage(
            Command=sensor_message_pb2.SensorMessage.SENSORDATA,
            SensorData=sensor_data
        )

        # Increment sequence number
        self.sequence += 1
        if self.sequence >= 65000:
            self.sequence = 1

        packet = message.SerializeToString()
        _LOGGER.debug(
            f"Sensor {self.sensor_id}: Built data packet, size={len(packet)} bytes, "
            f"next_seq={self.sequence}"
        )

        return packet

    def build_pairing_packet(self, temperature: float) -> bytes:
        """Build protobuf pairing packet.

        Pairing packets use sequence=1 and the signature key directly
        (not HMAC signature).

        Args:
            temperature: Current temperature reading

        Returns:
            Serialized protobuf SensorMessage ready for UDP broadcast
        """
        # Import here to avoid circular dependency
        from .protobuf import sensor_message_pb2

        temp_index = get_temperature_index(temperature, self.scale)

        _LOGGER.info(
            f"Building PAIRING packet for sensor {self.sensor_id} ({self.name}): "
            f"temp={temperature}°{self.scale}, temp_index={temp_index}, "
            f"mac={self.mac_address}, purpose={self.purpose}"
        )

        # Build INFO message
        info = sensor_message_pb2.INFO(
            Sequence=1,
            SensorId=self.sensor_id,
            Mac=self.mac_address,
            FwMajor=FW_MAJOR,
            FwMinor=FW_MINOR,
            Model=sensor_message_pb2.INFO.TEMPSENSOR,
            Power=sensor_message_pb2.INFO.BATTERY,
            Name=self.name,
            Type=self._get_protobuf_type(),
            Temperature=temp_index,
            Battery=100
        )

        # Build SENSORDATA message (pairing uses key directly as signature)
        sensor_data = sensor_message_pb2.SENSORDATA(
            Info=info,
            Signature=self.signature_key
        )

        _LOGGER.debug(
            f"Sensor {self.sensor_id}: Pairing signature_key={self.signature_key[:16]}... (truncated)"
        )

        # Build SensorMessage
        message = sensor_message_pb2.SensorMessage(
            Command=sensor_message_pb2.SensorMessage.SENSORPAIR,
            SensorData=sensor_data
        )

        # Reset sequence to 1 after pairing
        self.sequence = 1

        packet = message.SerializeToString()
        _LOGGER.info(
            f"Sensor {self.sensor_id}: Built pairing packet, size={len(packet)} bytes, "
            f"hex={packet.hex()[:32]}... (truncated)"
        )

        return packet


def broadcast_udp_packet(packet: bytes, port: int = UDP_PORT) -> None:
    """Broadcast UDP packet to the network.

    Sends the packet 5 times to 255.255.255.255:5001 (Venstar protocol requirement).

    Args:
        packet: Serialized protobuf packet to broadcast
        port: UDP port (default: 5001)
    """
    try:
        _LOGGER.debug(
            f"Broadcasting UDP packet: size={len(packet)} bytes, "
            f"destination={BROADCAST_ADDRESS}:{port}, repeats={BROADCAST_REPEAT_COUNT}, "
            f"hex={packet.hex()[:64]}... (truncated)"
        )

        with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as sock:
            sock.setsockopt(socket.SOL_SOCKET, socket.SO_BROADCAST, 1)
            for i in range(BROADCAST_REPEAT_COUNT):
                bytes_sent = sock.sendto(packet, (BROADCAST_ADDRESS, port))
                _LOGGER.debug(
                    f"UDP broadcast {i+1}/{BROADCAST_REPEAT_COUNT}: "
                    f"sent {bytes_sent} bytes to {BROADCAST_ADDRESS}:{port}"
                )

        _LOGGER.debug(f"Successfully broadcast {BROADCAST_REPEAT_COUNT} UDP packets")

    except Exception as e:
        _LOGGER.error(
            f"Failed to broadcast UDP packet: {e}, "
            f"packet_size={len(packet)}, port={port}",
            exc_info=True
        )
        raise
