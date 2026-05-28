# UI Automation

DesktopAgent runs in the signed-in user session because services run in Session 0 and cannot safely automate the interactive desktop.

Implemented behavior:

- Structured desktop snapshots with monitors, foreground window, top-level windows, and bounded control trees.
- Control metadata: automation id, name, control type, class, bounds, enabled/offscreen/focus flags, supported patterns, safe value, sensitivity flag, and suggested actions.
- Actions: focus window, click/double/right click, invoke control, set value for non-password fields, paste text into the focused control, send hotkeys, clipboard set/get with redaction, desktop screenshot.
- `ui_execute_plan` executes a deterministic list of basic actions and stops on failure unless the plan says to continue.

Sensitive handling:

- Password fields are marked sensitive using UI Automation password metadata.
- The agent refuses to set password fields.
- Clipboard reads redact sensitive-looking token/password content.
- UAC, MFA, and credential prompts should be handled by pausing and asking the user.

Next work:

- More complete wait conditions.
- Dialog classification.
- Drag/drop and robust scroll/select actions.
- Rich plan logs and stop/resume.
- Stronger screenshot redaction around sensitive fields.
