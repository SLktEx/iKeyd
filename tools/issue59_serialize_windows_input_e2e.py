from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

collection = ROOT / 'tests/iKeyd.Windows.Tests/WindowsGlobalInputE2ECollection.cs'
collection.write_text('''using Xunit;\n\nnamespace iKeyd.Windows.Tests;\n\n[CollectionDefinition(Name, DisableParallelization = true)]\npublic sealed class WindowsGlobalInputE2ECollection\n{\n    public const string Name = "Windows global input E2E";\n}\n''', encoding='utf-8')

for rel in [
    'tests/iKeyd.Windows.Tests/WindowsKeyboardE2ETests.cs',
    'tests/iKeyd.Windows.Tests/WindowsScenarioRunnerTests.cs',
]:
    path = ROOT / rel
    text = path.read_text(encoding='utf-8')
    if '[Collection(WindowsGlobalInputE2ECollection.Name)]' in text:
        continue
    marker = 'public sealed class '
    if marker not in text:
        raise SystemExit(f'class marker not found in {rel}')
    text = text.replace(marker, '[Collection(WindowsGlobalInputE2ECollection.Name)]\n' + marker, 1)
    path.write_text(text, encoding='utf-8')
