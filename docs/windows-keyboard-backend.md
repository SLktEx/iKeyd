# Windows keyboard backend

The Windows backend uses `WH_KEYBOARD_LL` on a dedicated message-loop thread and `SendInput` for output.

## Event model

- virtual key, scan code, and extended-key state are preserved
- key down/up is normalized before entering Core
- the backend tracks pressed state independently of Core
- handlers return either pass-through or suppress
- toggle state is read with `GetKeyState`

## Injection loop prevention

`WindowsKeyboardOutput` stamps every `SendInput` event with an iKeyd-specific `dwExtraInfo` marker. `WindowsKeyboardHook` recognizes that marker and skips those events before they reach the application handler. Other injected input remains observable and is tagged as `KeyEventOrigin.Injected`.

## Windows E2E validation

`WindowsKeyboardE2ETests` exercises the real Win32 path on a Windows runner:

1. install the real `WH_KEYBOARD_LL` hook
2. inject F24 through `user32`
3. verify the hook receives both down/up events and suppresses them
4. send F24 through iKeyd's actual `SendInput` implementation
5. verify the iKeyd injection marker prevents the events from looping back to the application handler

This is stronger than a pure P/Invoke/unit test because the test requires a live Windows message loop and actual low-level hook delivery.

A local or self-hosted interactive Windows machine is still recommended for final human-input verification, especially for physical keyboards, IME interaction, elevated applications, Remote Desktop, games, and other software with unusual input stacks.
