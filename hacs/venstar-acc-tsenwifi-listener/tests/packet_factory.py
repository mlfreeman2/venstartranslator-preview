"""Helpers for building Venstar packets and loading the golden fixtures in tests."""
from __future__ import annotations

import json
from pathlib import Path
from typing import Any

from custom_components.venstar_acc_tsenwifi_listener.protobuf import (
    sensor_message_pb2 as pb,
)

FIXTURES = Path(__file__).resolve().parent / "fixtures" / "csharp_golden_packets.json"


def load_golden() -> list[dict[str, Any]]:
    """Return the checked-in golden packet fixtures (with expected decode values)."""
    return json.loads(FIXTURES.read_text())["packets"]


def build_packet(
    *,
    mac: str = "428e0486d800",
    sensor_id: int = 0,
    sequence: int = 10,
    name: str | None = "Test Sensor",
    temperature: int | None = 120,
    set_type: bool = True,
    sensor_type: int = pb.INFO.REMOTE,
    battery: int | None = None,
    humidity: int | None = None,
    power: int = pb.INFO.BATTERY,
    fw_major: int = 4,
    fw_minor: int = 2,
    command: int = pb.SensorMessage.SENSORDATA,
    with_sensor_data: bool = True,
) -> bytes:
    """Serialize a SensorMessage with controllable fields.

    Optional wire fields default to *absent* (``None``) so tests can exercise the
    HasField-gated decode paths (temperature/battery/humidity/type).
    """
    info = pb.INFO(
        Sequence=sequence,
        SensorId=sensor_id,
        Mac=mac,
        FwMajor=fw_major,
        FwMinor=fw_minor,
        Model=pb.INFO.TEMPSENSOR,
        Power=power,
    )
    if name is not None:
        info.Name = name
    if set_type:
        info.Type = sensor_type
    if temperature is not None:
        info.Temperature = temperature
    if battery is not None:
        info.Battery = battery
    if humidity is not None:
        info.Humidity = humidity

    if with_sensor_data:
        msg = pb.SensorMessage(
            Command=command,
            SensorData=pb.SENSORDATA(Info=info, Signature="test-signature"),
        )
    else:
        msg = pb.SensorMessage(Command=command)
    return msg.SerializeToString()
