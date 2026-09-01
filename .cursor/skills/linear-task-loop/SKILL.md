---
name: linear-task-loop
description: Manage Undefault work through Linear, delegate scoped issues to agents, verify results, and update statuses. Use when the user asks to automate tasks, run the project workflow, manage Linear, or launch agents from issues.
---

# Linear Task Loop

## Purpose

Use this workflow to manage Undefault as a Linear-first project while implementation is delegated to other agents.

## Loop

1. Select the next Linear issue by milestone, priority, blockers, and dependencies.
2. Confirm the issue has clear scope, non-goals, and acceptance criteria.
3. Move the issue to `In Progress`.
4. Delegate:
   - design/proposal work -> `architecture-designer`
   - implementation work -> `issue-implementer`
   - verification/review -> `verification-reviewer`
5. Review agent output against the issue acceptance criteria.
6. Run or request targeted tests when code changed.
7. Update Linear with status, summary, blockers, and next steps.
8. Mark `Done` only after acceptance criteria are verified.

## Project Boundaries

- Linear is the source of truth for scope and status.
- Do not create implementation issues from design tasks until the product owner approves the proposal.
- Keep `Hotkeys + Timeline` as tester/product-owner tooling, not client UI.
- Spotify is dropped. Do not file or implement Spotify adapters, OAuth, or apps. Leftover deletion is UND-84.
- Preserve the current CS2 + Tauon baseline (`round_start → resume`, `death → pause`) unless an issue explicitly changes it.
- Do not commit, push, or create PRs unless the user explicitly asks.

## Status Template

```markdown
Linear update:
- Issue:
- Status:
- Agent/delegation:
- Verification:
- Blockers:
- Next:
```
