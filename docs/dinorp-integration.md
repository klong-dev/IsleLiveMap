# DINORP workspace (internal integration status)

The DINORP action opens a separate map workspace for `104.234.180.78:7777`.
It currently reads GPS from the local `dinorp-position.json` voice bridge written
by the installed DINORP launcher. It does **not** read DINORP Hub stats or
reuse the launcher's Steam token, device proof, or lease.

The position is accepted only when its `IsleVOIP` timestamp is at most two
seconds old and game-owned Npcap traffic to the pinned endpoint was seen in
the last three seconds. The source republishes only a new timestamp; it does
not keep refreshing a stale coordinate. A new game server observed on the
same gameplay port invalidates the old endpoint evidence. Pro Agent frames
from another endpoint are excluded from this workspace.

This path requires DINORP voice to be connected and publishing the file. If
that bridge is unavailable, the map remains in a waiting state. The bridge
contains XYZ/yaw only; stats, species, party data, and Hub-authoritative HUD
location require an approved read-only integration contract from DINORP Hub.
Do not import the other launcher's encrypted credentials or reproduce its
device/lease/anticheat handshake.

Live validation is still required with game + DINORP connected: check file
freshness, matched game endpoint, GPS movement, disconnect/reconnect, and
wrong-server rejection. Unit tests alone do not establish that a user's
DINORP voice bridge is active.
