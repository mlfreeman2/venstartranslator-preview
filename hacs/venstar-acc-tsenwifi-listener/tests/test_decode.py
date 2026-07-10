"""Decode + validation-gate tests. Pure (no Home Assistant runtime needed)."""
from __future__ import annotations

import pytest

from custom_components.venstar_acc_tsenwifi_listener.listener import (
    DropReason,
    decode_packet,
    normalize_mac,
)
from custom_components.venstar_acc_tsenwifi_listener.protobuf import (
    sensor_message_pb2 as pb,
)

from .packet_factory import build_packet, load_golden


@pytest.mark.parametrize("packet", load_golden(), ids=lambda p: p["description"][:40])
def test_golden_fixtures_decode(packet):
    """The cross-implementation parity contract: recover every expected field,
    including temperature_field_present via HasField, from the C# app's bytes."""
    expected = packet["expected"]
    reading, drop = decode_packet(bytes.fromhex(packet["hex"]), "192.0.2.1")

    assert drop is None
    assert reading is not None
    assert reading.mac == expected["mac"]
    assert reading.sensor_id == expected["sensor_id"]
    assert reading.name == expected["name"]
    assert reading.purpose == expected["purpose"]
    assert reading.sequence == expected["sequence"]
    assert reading.raw_index == expected["temp_index"]
    assert reading.temp_c == expected["temp_c"]
    assert reading.battery == expected["battery"]
    assert reading.humidity == expected["humidity"]
    assert reading.fw_major == expected["fw_major"]
    assert reading.fw_minor == expected["fw_minor"]
    assert reading.power == expected["power"].lower()

    # temperature_field_present is observable: the omitted-field fixture still
    # decodes to index 0 / -40.0 °C.
    if not expected["temperature_field_present"]:
        assert reading.raw_index == 0
        assert reading.temp_c == -40.0


def test_index_to_celsius_endpoints():
    for index, celsius in [(0, -40.0), (80, 0.0), (124, 22.0), (253, 86.5)]:
        reading, drop = decode_packet(build_packet(temperature=index), "1.2.3.4")
        assert drop is None
        assert reading.temp_c == celsius


def test_absent_temperature_is_minus_40():
    reading, drop = decode_packet(build_packet(temperature=None), "1.2.3.4")
    assert drop is None
    assert reading.raw_index == 0
    assert reading.temp_c == -40.0


def test_fault_sentinels_keep_packet_with_no_value():
    for index, fault in [(254, "shorted"), (255, "open")]:
        reading, drop = decode_packet(build_packet(temperature=index), "1.2.3.4")
        assert drop is None
        assert reading.temp_c is None
        assert reading.fault == fault


@pytest.mark.parametrize("index", [256, 300, 65535])
def test_out_of_range_index_dropped(index):
    reading, drop = decode_packet(build_packet(temperature=index), "1.2.3.4")
    assert reading is None
    assert drop is DropReason.INVALID


def test_absent_battery_and_humidity_are_none_not_zero():
    reading, _ = decode_packet(build_packet(temperature=120), "1.2.3.4")
    assert reading.battery is None
    assert reading.humidity is None


def test_present_zero_battery_and_humidity_kept_as_zero():
    reading, _ = decode_packet(
        build_packet(temperature=120, battery=0, humidity=0), "1.2.3.4"
    )
    assert reading.battery == 0
    assert reading.humidity == 0


def test_absent_type_defaults_to_remote():
    reading, drop = decode_packet(build_packet(temperature=120, set_type=False), "1.2.3.4")
    assert drop is None
    assert reading.purpose == "Remote"


def test_empty_name_falls_back_to_mac_suffix():
    reading, _ = decode_packet(build_packet(temperature=120, name=None), "1.2.3.4")
    assert reading.name == "Venstar d800"


def test_command_only_packet_is_dropped_no_ghost():
    reading, drop = decode_packet(build_packet(with_sensor_data=False), "1.2.3.4")
    assert reading is None
    assert drop is DropReason.INVALID


def test_wrong_command_dropped():
    reading, drop = decode_packet(
        build_packet(temperature=120, command=pb.SensorMessage.SUCCESS), "1.2.3.4"
    )
    assert reading is None
    assert drop is DropReason.INVALID


def test_pairing_command_accepted():
    reading, drop = decode_packet(
        build_packet(temperature=120, command=pb.SensorMessage.SENSORPAIR), "1.2.3.4"
    )
    assert drop is None
    assert reading.command == pb.SensorMessage.SENSORPAIR


@pytest.mark.parametrize("junk", [b"", b"\xde\xad\xbe\xef", b"not protobuf at all"])
def test_unparseable_or_incomplete_dropped(junk):
    reading, drop = decode_packet(junk, "1.2.3.4")
    assert reading is None
    # Random bytes either fail to parse or parse without SensorData; both drop.
    assert drop in (DropReason.UNPARSEABLE, DropReason.INVALID)


@pytest.mark.parametrize(
    "raw,expected",
    [
        ("42:8E:04:86:D8:00", "428e0486d800"),
        ("42-8e-04-86-d8-00", "428e0486d800"),
        ("428E0486D800", "428e0486d800"),
    ],
)
def test_mac_normalization(raw, expected):
    reading, drop = decode_packet(build_packet(temperature=120, mac=raw), "1.2.3.4")
    assert drop is None
    assert reading.mac == expected


@pytest.mark.parametrize("bad", ["not-a-mac", "428e0486d8", "428e0486d8001122"])
def test_bad_mac_dropped(bad):
    reading, drop = decode_packet(build_packet(temperature=120, mac=bad), "1.2.3.4")
    assert reading is None
    assert drop is DropReason.INVALID


def test_ignore_prefix_filters_packet():
    reading, drop = decode_packet(
        build_packet(temperature=120, mac="428e0486d800"),
        "1.2.3.4",
        ignore_prefixes=frozenset({"428e0486d8"}),
    )
    assert reading is None
    assert drop is DropReason.FILTERED


def test_ignore_prefix_leaves_other_macs():
    reading, drop = decode_packet(
        build_packet(temperature=120, mac="aabbccddee00"),
        "1.2.3.4",
        ignore_prefixes=frozenset({"428e0486d8"}),
    )
    assert drop is None
    assert reading.mac == "aabbccddee00"


def test_source_ip_captured():
    reading, _ = decode_packet(build_packet(temperature=120), "10.0.0.42")
    assert reading.source_ip == "10.0.0.42"


def test_normalize_mac_helper():
    assert normalize_mac("AABBCCDDEEFF") == "aabbccddeeff"
    assert normalize_mac("aa:bb:cc:dd:ee:ff") == "aabbccddeeff"
    assert normalize_mac("xyz") is None
    assert normalize_mac("") is None
