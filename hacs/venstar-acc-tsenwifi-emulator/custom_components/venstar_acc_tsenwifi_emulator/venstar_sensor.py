"""Venstar sensor logic - packet building and UDP broadcasting."""
from __future__ import annotations

import base64
import hashlib
import hmac
import logging
import socket

from decimal import Decimal, ROUND_HALF_UP

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
# Imported at module level so the (slow) protobuf descriptor build happens
# during integration import, which Home Assistant runs in the executor.
# Importing lazily inside packet builders triggers HA's "blocking call to
# import_module inside the event loop" warning on the first broadcast.
from .protobuf import sensor_message_pb2

_LOGGER = logging.getLogger(__name__)


def get_temperature_index(temperature: float, scale: str) -> int:
    """Calculate temperature index directly from temperature value.

    The Venstar protocol uses byte values (0-255) to represent the current temperature.
    Valid temperatures will have a byte value from 0 through 253.
    254 means shorted sensor and 255 means open sensor.
    The values 0 through 253 map to Celsius temperatures in 0.5°C increments from -40.0°C to 86.5°C
    or Fahrenheit temperatures in 1°F increments from -40°F to 188°F.

    Our Mapping Process:
    1. If Fahrenheit, round to nearest whole degree (half up) and convert to Celsius.
    2. Round to nearest half-degree increment.
    3. Add 40.
    4. Multiply by 2

    Special Note:
    When using this with Fahrenheit temperatures, some values between 0 and 253 will never come up.
    To understand what's going on, imagine the Celsius side as an array with 254 temperatures in it.
    The corresponding Fahrenheit array comes from the following process:
    1. Convert C to F.
    2A. If F is less than 0, round [half-up] the absolute value Fahrenheit rather than the converted value.
    2A1. Multiply the result by -1 to kick it back to negative.
    2B. If F is greater than or equal to zero, round [half-up] the Fahrenheit value

    There are a few cases where that results in a Fahrenheit value appearing twice in a row.
    For example: -38°C converts to -36.4°F which rounds to -36°F
                 -37.5°C converts to -35.5°F which rounds also to -36°F
    For example: 22.5°C converts to 72.5°F which rounds also to 73°F
                 23°C converts to  73.4°F which rounds also to 73°F

    Args:
        temperature: Temperature value
        scale: "F" for Fahrenheit or "C" for Celsius

    Returns:
        Index (0-253) representing the temperature

    Raises:
        ValueError: If temperature is out of range (-40.0°C to 86.5°C)
    """
    if scale == SCALE_FAHRENHEIT:
        # Round Fahrenheit to whole degrees first
        temp_decimal = Decimal(str(temperature))
        if temperature < 0:
            rounded_fahrenheit = -Decimal(str(abs(temperature))).quantize(Decimal('1'), rounding=ROUND_HALF_UP)
        else:
            rounded_fahrenheit = temp_decimal.quantize(Decimal('1'), rounding=ROUND_HALF_UP)
        # Convert rounded Fahrenheit to Celsius: C = (F - 32) × 5/9
        celsius_temp = float(rounded_fahrenheit - 32) * 5.0 / 9.0
    elif scale == SCALE_CELSIUS:
        celsius_temp = temperature
    else:
        raise ValueError(f"Invalid temperature scale: {scale}")

    # Round to nearest 0.5°C (multiply by 2, round, divide by 2)
    rounded_celsius = Decimal(str(celsius_temp * 2)).quantize(Decimal('1'), rounding=ROUND_HALF_UP) / 2

    # Check bounds (-40.0°C to 86.5°C)
    if rounded_celsius < Decimal('-40.0') or rounded_celsius > Decimal('86.5'):
        raise ValueError(
            f"Temperature {temperature}°{scale} (={rounded_celsius}°C) is outside "
            f"the valid range of -40.0°C to 86.5°C"
        )

    # Calculate index: index = (celsius + 40) × 2
    index = int((rounded_celsius + Decimal('40.0')) * 2)

    return index


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

    def _build_info(self, sequence: int, temp_index: int):
        """Build the INFO protobuf message.

        The Temperature field is omitted when the index is 0 (exactly -40.0°C)
        to stay byte-identical with the C# implementation, whose protobuf-net
        serializer skips zero-valued optional fields. Receivers read an absent
        optional uint32 as 0, so the decoded value is the same either way.
        """
        info = sensor_message_pb2.INFO(
            Sequence=sequence,
            SensorId=self.sensor_id,
            Mac=self.mac_address,
            FwMajor=FW_MAJOR,
            FwMinor=FW_MINOR,
            Model=sensor_message_pb2.INFO.TEMPSENSOR,
            Power=sensor_message_pb2.INFO.BATTERY,
            Name=self.name,
            Type=self._get_protobuf_type(),
            Battery=100
        )
        if temp_index != 0:
            info.Temperature = temp_index
        return info

    def build_data_packet(self, temperature: float) -> bytes:
        """Build protobuf data packet with HMAC signature.

        Args:
            temperature: Current temperature reading

        Returns:
            Serialized protobuf SensorMessage ready for UDP broadcast
        """
        temp_index = get_temperature_index(temperature, self.scale)

        _LOGGER.debug(
            "Building data packet for sensor %s (%s): temp=%s°%s, temp_index=%s, seq=%s",
            self.sensor_id, self.name, temperature, self.scale, temp_index, self.sequence,
        )

        # Build INFO message
        info = self._build_info(sequence=self.sequence, temp_index=temp_index)

        # Generate HMAC signature
        info_bytes = info.SerializeToString()
        signature = self.generate_signature(info_bytes)

        _LOGGER.debug(
            "Sensor %s: INFO bytes=%s, signature=%s... (truncated)",
            self.sensor_id, len(info_bytes), signature[:16],
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
            "Sensor %s: Built data packet, size=%s bytes, next_seq=%s",
            self.sensor_id, len(packet), self.sequence,
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
        temp_index = get_temperature_index(temperature, self.scale)

        # Packet-construction detail is DEBUG; the user-facing INFO line for a
        # pairing event is logged by the caller ("Pairing packet sent...").
        _LOGGER.debug(
            "Building PAIRING packet for sensor %s (%s): temp=%s°%s, temp_index=%s, mac=%s, purpose=%s",
            self.sensor_id, self.name, temperature, self.scale, temp_index,
            self.mac_address, self.purpose,
        )

        # Build INFO message
        info = self._build_info(sequence=1, temp_index=temp_index)

        # Build SENSORDATA message (pairing uses key directly as signature)
        sensor_data = sensor_message_pb2.SENSORDATA(
            Info=info,
            Signature=self.signature_key
        )

        _LOGGER.debug(
            "Sensor %s: Pairing signature_key=%s... (truncated)",
            self.sensor_id, self.signature_key[:16],
        )

        # Build SensorMessage
        message = sensor_message_pb2.SensorMessage(
            Command=sensor_message_pb2.SensorMessage.SENSORPAIR,
            SensorData=sensor_data
        )

        # Reset sequence to 1 after pairing
        self.sequence = 1

        packet = message.SerializeToString()
        _LOGGER.debug(
            "Sensor %s: Built pairing packet, size=%s bytes, hex=%s... (truncated)",
            self.sensor_id, len(packet), packet.hex()[:32],
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
            "Broadcasting UDP packet: size=%s bytes, destination=%s:%s, repeats=%s, hex=%s... (truncated)",
            len(packet), BROADCAST_ADDRESS, port, BROADCAST_REPEAT_COUNT, packet.hex()[:64],
        )

        with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as sock:
            sock.setsockopt(socket.SOL_SOCKET, socket.SO_BROADCAST, 1)
            for i in range(BROADCAST_REPEAT_COUNT):
                bytes_sent = sock.sendto(packet, (BROADCAST_ADDRESS, port))
                _LOGGER.debug(
                    "UDP broadcast %s/%s: sent %s bytes to %s:%s",
                    i + 1, BROADCAST_REPEAT_COUNT, bytes_sent, BROADCAST_ADDRESS, port,
                )

        _LOGGER.debug("Successfully broadcast %s UDP packets", BROADCAST_REPEAT_COUNT)

    except Exception:
        _LOGGER.exception(
            "Failed to broadcast UDP packet (size=%s, port=%s)", len(packet), port
        )
        raise
