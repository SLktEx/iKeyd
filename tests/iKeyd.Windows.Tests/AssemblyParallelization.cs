using Xunit;

// Windows keyboard / legacy-oracle tests operate on process-global and desktop-global
// input state (global hotkeys, low-level hooks, injected key events). Running test
// classes in parallel can make two legacy hotkeySKG instances consume the same
// synthetic gesture and produce nondeterministic output. Keep this assembly serial.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
