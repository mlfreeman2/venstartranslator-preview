#!/usr/bin/env python3
"""Broadcast Venstar sensor packets to UDP 5001 for listener development.

By default replays the golden fixture packets checked in at
tests/fixtures/csharp_golden_packets.json. Also accepts any
``venstar-protobuf-capture/1`` file saved by the C# app's Protobuf Listener
page (both formats store packets as {"packets": [{"hex": ...}, ...]}), which
turns a user's capture attached to a bug report into locally reproducible
traffic.

Note: the fixtures have fixed sequence numbers, so after the first round the
listener's per-mac dedup will (correctly) suppress state updates and only
refresh last_seen. That is the expected behavior to observe, not a bug in
this script.
"""
import argparse
import json
import socket
import time
from pathlib import Path

DEFAULT_FIXTURES = (
    Path(__file__).resolve().parent.parent
    / "tests"
    / "fixtures"
    / "csharp_golden_packets.json"
)


def load_packets(path: str) -> list[bytes]:
    data = json.loads(Path(path).read_text())
    return [bytes.fromhex(entry["hex"]) for entry in data["packets"]]


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "file",
        nargs="?",
        default=str(DEFAULT_FIXTURES),
        help="fixture or venstar-protobuf-capture/1 file (default: golden fixtures)",
    )
    parser.add_argument("--port", type=int, default=5001)
    parser.add_argument(
        "--interval",
        type=float,
        default=10.0,
        help="seconds between replay rounds; 0 sends one round and exits",
    )
    parser.add_argument(
        "--repeat",
        type=int,
        default=5,
        help="copies of each packet per round (real senders send 5)",
    )
    args = parser.parse_args()

    packets = load_packets(args.file)
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.setsockopt(socket.SOL_SOCKET, socket.SO_BROADCAST, 1)

    print(
        f"Replaying {len(packets)} packets x{args.repeat} to "
        f"255.255.255.255:{args.port}"
        + (f" every {args.interval}s (Ctrl+C to stop)" if args.interval > 0 else " once")
    )
    while True:
        for packet in packets:
            for _ in range(args.repeat):
                sock.sendto(packet, ("255.255.255.255", args.port))
        if args.interval <= 0:
            break
        time.sleep(args.interval)


if __name__ == "__main__":
    main()
