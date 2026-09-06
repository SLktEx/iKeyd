from pathlib import Path

path = Path('tests/iKeyd.Windows.Tests/HostedTModeLegacyRunner.cs')
text = path.read_text(encoding='utf-8')
old = '''                    var digits = ResolveModeSelectionDigits(requestedKeymap);
                    for (var index = 0; index < digits.Count; index++)
                    {
                        SendModeSelectionChord(digits[index]);
                        if (index + 1 < digits.Count)
                            await Task.Delay(TimeSpan.FromMilliseconds(80), cancellationToken);
                    }

                    return;
'''
new = '''                    var digits = ResolveModeSelectionDigits(requestedKeymap);
                    // Hosted runners occasionally expose the process before all legacy hotkeys
                    // are ready. Re-send the same idempotent mode-selection sequence once so a
                    // single missed startup chord cannot leak a stale keymap into the scenario.
                    for (var attempt = 0; attempt < 2; attempt++)
                    {
                        for (var index = 0; index < digits.Count; index++)
                        {
                            SendModeSelectionChord(digits[index]);
                            if (index + 1 < digits.Count)
                                await Task.Delay(TimeSpan.FromMilliseconds(80), cancellationToken);
                        }
                        if (attempt == 0)
                            await Task.Delay(TimeSpan.FromMilliseconds(120), cancellationToken);
                    }

                    return;
'''
if old not in text:
    raise SystemExit('Hosted mode-selection anchor not found')
path.write_text(text.replace(old, new, 1), encoding='utf-8')
print('hosted legacy mode bootstrap hardened')
