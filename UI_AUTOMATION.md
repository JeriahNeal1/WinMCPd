# UI Automation

DesktopAgent runs in the signed-in user session because services run in Session 0 and cannot safely automate the interactive desktop.

Implemented behavior:

- Structured desktop snapshots with monitors, foreground window, top-level windows, and bounded control trees.
- Control metadata: automation id, name, control type, class, bounds, enabled/offscreen/focus flags, supported patterns, safe value, sensitivity flag, and suggested actions.
- Actions: focus/move/resize/minimize/maximize/close windows, inspect windows, find controls, click/double/right click, invoke controls, set values for non-password fields, select items, expand/collapse, scroll, drag/drop, paste text into the focused control, send hotkeys/raw keys, clipboard set/get with redaction, and desktop/window screenshots.
- Waits: `ui_wait_for_window`, `ui_wait_for_control`, `ui_wait_for_text`, `ui_wait_for_dialog`, and `ui_wait_until_idle`.
- Dialog detection: `ui_detect_common_dialogs` returns likely dialogs with safe button names and sensitivity flags.
- `ui_execute_plan` executes deterministic multi-step actions with plan timeout, per-action timeout, retry count, fallback selector, before/after waits, screenshot actions, and failure behavior.
- Plan state: `ui_get_plan_status` and `ui_read_plan_log` return in-session plan results/logs. `ui_stop_current_plan` is currently cooperative through request cancellation.

Sensitive handling:

- Password fields are marked sensitive using UI Automation password metadata.
- The agent refuses to set password fields.
- Clipboard reads redact sensitive-looking token/password content.
- UAC, MFA, password, and credential prompts pause plans when the corresponding stop flags are set. The agent returns the latest UI graph so Codex can ask the user to complete the sensitive step.

Next work:

- Durable plan stop/resume across DesktopAgent restarts.
- Stronger screenshot redaction around sensitive fields.
- More exhaustive real-app integration scripts beyond the current Notepad/Settings-ready primitives.
