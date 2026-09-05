# Windows keyboard E2E

The Windows keyboard backend has an end-to-end test that exercises the live Win32 path on the CI runner. It installs `WH_KEYBOARD_LL`, injects F24 through `user32`, verifies the hook receives and suppresses the injected down/up events, then sends F24 through iKeyd's real `SendInput` implementation and verifies the iKeyd `dwExtraInfo` marker prevents a feedback loop.

This validates the actual Windows hook/message-loop/injection path, not only P/Invoke signatures or mocks.

For release confidence, a self-hosted or local interactive Windows machine should additionally cover physical key presses and environment-specific cases such as IME, elevated applications, Remote Desktop, and software with specialized input handling.
