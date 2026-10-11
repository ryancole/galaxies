# Protocol

Galaxies speaks one small binary protocol over **UDP** between the game client and the server. There's no
HTTP, WebSocket or JSON anywhere. It's modelled on the protocol in the `headless` repo.

The C# implementation is `src/server/Galaxies.Protocol` (`Packets.cs`, `PacketCodec.cs`,
`ProtocolConstants.cs`), shared by the server and the Godot client.

## Who decides what

The server owns the game state. For now that's every player and where their avatar is.

Movement works like World of Warcraft: the **client moves its own avatar** (so it feels instant, with the
client's own collision) and **reports where it went**. The server checks every report and refuses any move a
legitimate client couldn't have made. A refused move gets a **correction**: the server tells the client where
the avatar really is, and the client snaps back there. The client doesn't decide where it starts either: the
server places it with a correction when it connects, and the client can't move until then.

The server checks each move against a **movement budget** (`World.TryMove`, settings in `MovementConfig`):

| Check | Refused as |
|---|---|
| NaN or infinite numbers | `invalid` |
| Outside the world: more than 1800 m from the centre on x or z, or below −20 m or above 400 m | `out_of_bounds` |
| More sideways distance than the budget holds. It refills at the run speed × 1.15 (9.8 m/s) and holds up to 1.5 s of that, so reports bunched up after a lag spike still pass | `too_fast` |
| More height gained than the climb budget holds. It refills at 6.5 m/s (running up a 45° slope) and holds up to 1.6 m (a jump plus slack). Falling is always allowed | `too_high` |

The server doesn't know the level's geometry yet, so it can't stop walking through walls or slow flying. That
needs the level layout on the server.

## Basics

- One packet per UDP datagram, at most **1200 bytes** (keeps packets under typical internet MTUs).
- Every packet starts with a 1-byte **type**. Types `0x01`–`0x7F` go client → server; `0x80`–`0xFF` go
  server → client.
- All numbers are **little-endian**. `u8/u16/u32/u64` are unsigned integers, `i32/i64` signed, `f32` an
  IEEE 754 float.
- A **string** is a `u8` byte length followed by that many bytes of UTF-8 (so at most 255 bytes).
- Receivers silently drop anything malformed: unknown type, truncated, trailing bytes, invalid UTF-8 or
  enum values. UDP has no error channel, and replying to junk would invite abuse.
- The server listens on port **5090** by default, IPv4 and IPv6.
- Positions are metres in Godot's axes: **y is up, −z is north**. A **yaw** is radians: 0 looks north (−z),
  positive turns left (towards −x), in [−π, π).

## Connection lifecycle

```
client                                     server
  │── ConnectRequest (nonce, token) ────────▶│   resend every 0.5 s until answered
  │◀──────── ConnectAccepted (session) ──────│   or ConnectDenied (reason)
  │◀──────── Correction (id 1, placed) ──────│   where the avatar starts; repeats each tick until applied
  │                                          │
  │── Move (session, seq, correction id) ───▶│   every 50 ms, once placed
  │◀──────── WorldSnapshot ──────────────────│   every tick (20/s), unreliable
  │◀──────── Correction (id n, reason) ──────│   when a move is refused; repeats until applied
  │── KeepAlive (session) ──────────────────▶│   only if nothing else was sent for 1 s
  │                                          │
  │── Disconnect (session) ─────────────────▶│   client leaves, or
  │◀──────── Disconnected (session, reason) ─│   server ends it
```

1. **Connect.** The client picks a random 64-bit **nonce** and sends `ConnectRequest`, resending it every
   0.5 s until it gets an answer whose nonce matches. A resent request after the server has accepted gets
   the same answer again.
2. **Accepted or denied.** `ConnectAccepted` carries a random 64-bit **session id**. Every later packet in
   either direction carries it, and the server ignores packets whose session id or source address don't
   match. That's what stops other hosts injecting packets into a session.
3. **Placed.** The server sends a `Correction` with id 1 and reason `placed`: where the avatar is. That's the
   spawn point for a new player, or wherever they left off. The client teleports there and only then starts
   sending moves.
4. **Moving.** The client sends a `Move` every 50 ms with where its avatar is, a **sequence** number counting
   up from 1 within the session, and the id of the **last correction it applied**. The server ignores a move
   if:
   - its sequence isn't newer than the newest seen (UDP can reorder), or
   - its correction id isn't the latest the server sent. The move was made before the client snapped back,
     so it started from a refused position.
5. **Corrections.** When the server refuses a move, it sends a `Correction` with the next id, the reason,
   and where the avatar really is. Corrections must arrive, so the server repeats the latest one every tick
   until a move carrying its id shows the client applied it. Clients ignore corrections whose id isn't
   newer than the last one applied.
6. **Snapshots.** Every tick the server sends a `WorldSnapshot` of the **other** online avatars within
   250 m of the client's avatar, nearest first, at most 50. Snapshots are unreliable: one may be lost or
   arrive out of order. Each carries the tick number; ignore any older than the newest you've seen. An avatar
   missing from a snapshot has left or gone out of range.
7. **Staying alive.** Moves count as keep-alives. A client sends `KeepAlive` if it has sent nothing else for
   a second. Either side gives up after **5 seconds** of silence: the server sends
   `Disconnected(timed_out)`, and clients should treat a silent server as gone.
8. **Ending.** A client sends `Disconnect` when it leaves. The server sends `Disconnected` with a reason:
   - `server_shutdown`: sent (twice) when the server stops. Wait a few seconds and reconnect.
   - `superseded`: another connection logged in with the same token. One connection per player; the newest
     wins.
   - `timed_out`: the server heard nothing for 5 seconds.

## Packets

### Client → server

**`0x01` ConnectRequest**

| Field | Type | Notes |
|---|---|---|
| type | u8 | `0x01` |
| magic | u32 | `0x59584C47` (the bytes `G L X Y`) |
| version | u16 | protocol version, currently `1` |
| nonce | u64 | random, chosen by the client |
| token | string | the player's token |

For example, nonce `0x0807060504030201` and token `ab`:

```
01  47 4C 58 59  01 00  01 02 03 04 05 06 07 08  02 61 62
```

**`0x02` KeepAlive**: `type u8`, `session u64`.

**`0x03` Disconnect**: `type u8`, `session u64`.

**`0x04` Move** (33 bytes)

| Field | Type | Notes |
|---|---|---|
| type | u8 | `0x04` |
| session | u64 | |
| sequence | u32 | counts up from 1 within a session |
| correction_id | u32 | the last correction applied |
| x, y, z | f32 ×3 | the avatar's feet |
| yaw | f32 | |

For example, session `0x0807060504030201`, sequence `0x0D0C0B0A`, correction 1, at (1, 2, −1), yaw 0:

```
04  01 02 03 04 05 06 07 08  0A 0B 0C 0D  01 00 00 00  00 00 80 3F  00 00 00 40  00 00 80 BF  00 00 00 00
```

### Server → client

**`0x81` ConnectAccepted**

| Field | Type | Notes |
|---|---|---|
| type | u8 | `0x81` |
| nonce | u64 | echoed from the request |
| session | u64 | random, never 0 |
| player_id | i32 | |
| player_name | string | |
| tick_rate | f32 | ticks (snapshots) per second |

**`0x82` ConnectDenied**: `type u8`, `nonce u64`, `reason u8`: `1` unsupported version, `2` invalid token.

**`0x83` WorldSnapshot**: `type u8`, `session u64`, `tick i64`, `count u8` (at most 50), then `count`
**avatars**, nearest first. An **avatar** is 20 bytes: `player_id i32`, `x, y, z f32`, `yaw f32`.

**`0x84` Disconnected**: `type u8`, `session u64`, `reason u8`: `1` server shutdown, `2` superseded,
`3` timed out.

**`0x85` Correction** (34 bytes)

| Field | Type | Notes |
|---|---|---|
| type | u8 | `0x85` |
| session | u64 | |
| id | u32 | counts up from 1 within a session |
| reason | u8 | `1` placed, `2` too fast, `3` too high, `4` out of bounds, `5` invalid |
| x, y, z | f32 ×3 | where the avatar is |
| yaw | f32 | |

## Versioning

The `version` field in `ConnectRequest` is bumped on any breaking change, and the server denies versions it
doesn't speak (`unsupported_version`). New packet types are not breaking: receivers already drop types they
don't know.

## Not yet

- **Encryption.** Tokens travel in plain text. Fine on a local network for development, but not for a public
  server.
- **Level geometry on the server**, so it can refuse moves through walls.
