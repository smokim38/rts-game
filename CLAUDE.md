@AGENTS.md

# Claude Code notes

The studio handbook above (AGENTS.md) applies to you in full. These notes only add Claude-specific details.

- Your role, model, effort and tools come from your agent file in `.claude/agents/`. Without a role, you are a general assistant to the owner and are still bound by the handbook.
- Use plan mode for reviews, and for any Critical-tier work until the owner approves the plan.
- To save context and usage, hand broad codebase searches to a read-only subagent and keep only its conclusion.
- Don't run `/init`. Don't rewrite this file or AGENTS.md unless your Issue is about them, which makes it Critical tier.
- Don't set the `CLAUDE_CODE_EFFORT_LEVEL` or `CLAUDE_CODE_AUTO_COMPACT_WINDOW` environment variables: they override every agent's own effort and auto-compact settings.
