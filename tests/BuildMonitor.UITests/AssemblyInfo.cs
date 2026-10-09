// Copyright (c) 2023-2026 ktsu-dev contributors

// ImGui keeps its context in global state, so only one harness can exist per process and two
// scenarios running at once would corrupt each other. BuildMonitor's own state is static too.
// Sequential execution is a correctness requirement here, not a performance preference.
[assembly: DoNotParallelize]
