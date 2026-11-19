# Peekaboom

A standalone replacement for the PhysX Visual Debugger (PVD), written in C# and Avalonia. It accepts the legacy PVD stream from an unmodified PhysX game and lets you view, scrub and inspect it.

## Standalone

- Never reference BasisCoreTools, BasisCoreToolsUI, BasisCoreToolsLib, BasisCoreToolsNet or anything else from Basis. Peekaboom may be open-sourced.
- Only permissively licensed dependencies and assets. No FontAwesome icons.
- Keep "PhysX" and "PVD" out of product names. They are NVIDIA trademarks.

## Coding Conventions

- **NO COMMENTS.** None in C#, XAML, project files or scripts. If a comment seems necessary, ask first.
- **Classes/Methods:** PascalCase (e.g., `StreamReader`, `ReadEvent()`)
- **Private fields:** camelCase with `m` prefix (e.g., `mListener`)
- **Constants:** UPPER_SNAKE_CASE (e.g., `DEFAULT_PORT`)
- **C# style** - Prefer "basic" code. LINQ is seldom used. Async/await is almost never used.

## Prose

- Em-dashes are never used. Not in source, prints, docs or anywhere else. Plain ASCII.
- No semicolons in prose at all, and almost never a colon. Start a new sentence instead.

## Building

Never run builds. Write the code, stop at a sensible point, and the user builds and reports back.

## Version control

The project uses git. **Never** run any git commands unless explicitly asked to.

## Plans and notes

- Peekaboom does not use Argo or any other ticket tracker.
- Plans, research and handoff notes are Markdown files in `docs`. Read `docs/Plan.md` at the start of a task.
- Make plans extremely concise. Sacrifice grammar for the sake of concision.
- At the end of each plan, list any unresolved questions.
- Never reference docs from the source.
- `docs` is untracked. Never add it to a commit.

## Line endings

All files use CRLF.
