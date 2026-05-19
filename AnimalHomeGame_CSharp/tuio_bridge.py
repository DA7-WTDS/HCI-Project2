"""
tuio_bridge.py — Receives raw TUIO/OSC UDP packets from reacTIVision (port 3333)
                 and forwards plain-text commands to the C# game (port 3334).

Why this exists:
    TCD.System.TUIO 1.0.6 is a .NET Framework 4.x library that silently
    fails on .NET 10. This bridge does the same job with zero C# dependencies.

Run:
    python tuio_bridge.py

Make sure reacTIVision is running BEFORE starting this script.
"""

import socket
import struct

# ── Ports ─────────────────────────────────────────────────────────────────────
TUIO_IN_PORT = 3333        # reacTIVision sends TUIO/OSC here
GAME_PORT    = 3334        # C# game's TuioHandler listens here
GAME_IP      = "127.0.0.1"

# ── Minimal OSC parser ─────────────────────────────────────────────────────────

def _read_osc_string(data: bytes, pos: int):
    """Read a null-terminated, 4-byte-padded OSC string."""
    end = data.index(b'\x00', pos)
    s   = data[pos:end].decode('ascii', errors='ignore')
    # advance to next 4-byte boundary
    pos = end + 1
    r   = pos % 4
    if r:
        pos += 4 - r
    return s, pos


def _parse_osc_message(data: bytes):
    """Return (address, args_list) or None if parsing fails."""
    try:
        pos       = 0
        address, pos = _read_osc_string(data, pos)
        typetags, pos = _read_osc_string(data, pos)
        if not typetags.startswith(','):
            return None
        args = []
        for t in typetags[1:]:
            if t == 'i':
                args.append(struct.unpack_from('>i', data, pos)[0]); pos += 4
            elif t == 'f':
                args.append(struct.unpack_from('>f', data, pos)[0]); pos += 4
            elif t == 's':
                s, pos = _read_osc_string(data, pos)
                args.append(s)
            else:
                break   # blob / other — stop here
        return address, args
    except Exception:
        return None


def _parse_osc_bundle(data: bytes):
    """Return list of (address, args) parsed from an OSC bundle or single message."""
    messages = []
    if data[:8] == b'#bundle\x00':
        pos = 16   # skip '#bundle\0' + 8-byte timetag
        while pos + 4 <= len(data):
            size = struct.unpack_from('>i', data, pos)[0]
            pos += 4
            if size > 0:
                msg = _parse_osc_message(data[pos:pos + size])
                if msg:
                    messages.append(msg)
            pos += size
    else:
        msg = _parse_osc_message(data)
        if msg:
            messages.append(msg)
    return messages


# ── Main bridge loop ───────────────────────────────────────────────────────────

def main():
    sock_in = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock_in.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    sock_in.bind(('', TUIO_IN_PORT))
    sock_in.settimeout(1.0)

    sock_out = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)

    print(f"[TUIO Bridge] Listening on :{TUIO_IN_PORT}  →  forwarding to {GAME_IP}:{GAME_PORT}")
    print("[TUIO Bridge] Make sure reacTIVision is running. Press Ctrl+C to quit.\n")

    # session_id → (symbol_id, x, y)
    active: dict[int, tuple[int, float, float]] = {}

    while True:
        try:
            data, _ = sock_in.recvfrom(65535)
        except socket.timeout:
            continue
        except KeyboardInterrupt:
            break

        messages = _parse_osc_bundle(data)

        pending_sets: list[tuple[int, int, float, float]] = []
        alive_sessions: set[int] | None = None

        for address, args in messages:
            if address != '/tuio/2Dobj' or not args:
                continue
            cmd = args[0]

            if cmd == 'set' and len(args) >= 5:
                # set sessionId symbolId x y angle [xspeed yspeed rspeed maccel raccel]
                pending_sets.append((int(args[1]), int(args[2]),
                                     float(args[3]), float(args[4])))

            elif cmd == 'alive':
                alive_sessions = {int(a) for a in args[1:]}

            # 'fseq' — ignore

        # Process 'set' messages
        for session_id, symbol_id, x, y in pending_sets:
            if session_id not in active:
                active[session_id] = (symbol_id, x, y)
                msg = f"TUIO_ADD:{symbol_id},{x:.4f},{y:.4f}"
                sock_out.sendto(msg.encode(), (GAME_IP, GAME_PORT))
                print(f"[BRIDGE] ADD  symbolId={symbol_id}  x={x:.3f}  y={y:.3f}")
            else:
                old = active[session_id]
                if abs(old[1] - x) > 0.001 or abs(old[2] - y) > 0.001:
                    active[session_id] = (symbol_id, x, y)
                    msg = f"TUIO_UPD:{symbol_id},{x:.4f},{y:.4f}"
                    sock_out.sendto(msg.encode(), (GAME_IP, GAME_PORT))
                    print(f"[BRIDGE] UPD  symbolId={symbol_id}  x={x:.3f}  y={y:.3f}")

        # Remove objects that disappeared from the 'alive' list
        if alive_sessions is not None:
            for session_id in list(active.keys()):
                if session_id not in alive_sessions:
                    symbol_id, x, y = active.pop(session_id)
                    msg = f"TUIO_REM:{symbol_id},{x:.4f},{y:.4f}"
                    sock_out.sendto(msg.encode(), (GAME_IP, GAME_PORT))
                    print(f"[BRIDGE] REM  symbolId={symbol_id}")

    sock_in.close()
    sock_out.close()
    print("[TUIO Bridge] Stopped.")


if __name__ == '__main__':
    main()
