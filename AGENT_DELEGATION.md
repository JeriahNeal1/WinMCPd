# Agent Delegation

WindowsPowerUserMcp can detect and run local agent CLI tools, but treats their output as untrusted until reviewed, built, and tested.

Implemented:

- `detect_agent_tools`
- `create_agent_prompt`
- `run_agent_cli`
- `read_agent_output` via tracked process output
- handoff summary aliases through the task ledger

Scaffolded:

- `install_agent_tool`
- `import_agent_patch`
- `validate_agent_output`

Rules:

- Prefer official OAuth/browser/device-code login flows.
- If login requires the user, return a waiting state and queue a continuation.
- Do not type, store, or log passwords.
- Prefer patch/diff-based integration and run validation before trusting another agent's work.
