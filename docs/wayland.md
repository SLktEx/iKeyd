# Wayland backend

`iKeyd.Wayland` implements global remapping below the compositor through the Linux input subsystem. Physical keyboard events are read from evdev and remapped/pass-through events are emitted through a uinput virtual device. This avoids depending on a compositor-specific global-key protocol and keeps chord/keymap/macro logic in `iKeyd.Core`.

## Architecture

```text
physical keyboard
    │
    ▼
Linux evdev ── EVIOCGRAB ──► iKeyd.Core
                                │
                         suppress / pass through
                                │
                                ▼
                           Linux uinput
                                │
                                ▼
                        Wayland compositor
```

When physical keyboards are grabbed, iKeyd re-emits only events that Core marks `PassThrough`; events marked `Suppress` are not replayed. Unknown physical key codes are still replayed so unrelated keys are not silently lost.

## Requirements

The backend currently supports 64-bit Linux. To use global keyboard remapping the process needs:

- read access to the selected `/dev/input/event*` keyboard devices,
- write access to `/dev/uinput` (or `/dev/input/uinput`),
- a loaded `uinput` kernel module when it is not built in.

Configure those permissions with your distribution's normal udev/group mechanism. Avoid making input devices world-readable/writable on a real machine.

Keyboard devices are auto-discovered through both `/dev/input/by-id/*-event-kbd` and `/dev/input/by-path/*-event-kbd`, which covers external and many built-in keyboards. Override discovery with a path-separated list:

```bash
export IKEYD_INPUT_DEVICES=/dev/input/event3:/dev/input/event6
```

Override the uinput device path with:

```bash
export IKEYD_UINPUT=/dev/uinput
```

## Capabilities

The backend exposes capabilities explicitly through `IBackendCapabilityProvider` / `BackendCapabilities`.

Supported when the corresponding device/integration is available:

- physical keyboard input,
- keyboard suppression with `EVIOCGRAB`,
- keyboard output,
- ASCII text output,
- relative pointer movement,
- left/right/middle mouse buttons,
- vertical scrolling,
- media keys.

Clipboard read/write/watch is available when a Wayland session is active and `wl-copy` / `wl-paste` from `wl-clipboard` are installed. The current adapter intentionally uses those commands instead of assuming one compositor-specific data-control protocol.

The following desktop operations are deliberately reported as unsupported by the compositor-independent backend:

- absolute pointer query/positioning,
- active/top-level window queries,
- window activation,
- minimize/maximize/restore,
- window move/resize,
- always-on-top,
- opacity,
- caption/title-bar manipulation.

Calling one of those operations raises `BackendCapabilityException`. A compositor-specific adapter can add them later without putting compositor branches into Core.

## Testing

Run the backend unit/integration tests on Linux with:

```bash
dotnet test tests/iKeyd.Wayland.Tests/iKeyd.Wayland.Tests.csproj
```

The repository `Wayland` GitHub Actions workflow runs on Ubuntu and starts Weston on an Xvfb-backed X11 output so the test compositor exposes a real `wl_seat`. It exercises a real `wl-copy`/`wl-paste` clipboard round-trip. When the hosted runner exposes `/dev/uinput`, it also creates a real virtual input device and writes keyboard/pointer events through it.

The ordinary solution CI still runs the shared Core regressions, so adding the Wayland backend does not fork the chord/keymap behavior from Windows.

## Current boundary

`iKeyd.Wayland` is the platform backend. The reusable automation profile and runtime live in `iKeyd.Core`, while the legacy hotkeySKG-specific S/K/T/R and M/H/S/K/A policy remains in `iKeyd.Profiles.HotkeySkg`. A future Linux host/application can compose those pieces without changing the backend contracts.
