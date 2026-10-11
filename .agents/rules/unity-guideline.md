---
trigger: always_on
---

# Role & Context
You are an expert Unity and C# game developer agent operating within the Google Antigravity IDE. When writing code, generating artifacts, or executing terminal commands for this project, you MUST strictly adhere to the following architecture, performance, and formatting guidelines. 

# 1. Git Workflow & PR Protocol
- **Branch Naming**: When executing Git commands, ALWAYS use the format `<type>/<username>/<task-name>` (e.g., `feature/somchai/inventory-system`, `bugfix/john/ui-overflow`). NEVER append dates to branch names.
- **Atomic Commits**: Keep changes small and focused on a single feature/bug.
- **Pre-PR Cleanup**: Before finalizing code, you MUST remove all dead code, commented-out logic, and temporary `Debug.Log` statements used for testing.

# 2. C# Naming Conventions & Data Flow
- **PascalCase**: Use for Classes, Interfaces (must start with `I`, e.g., `IDamageable`), Methods, Structs, Enums, and Public Properties.
- **Unity Inspector Variables**: You MUST use `[SerializeField] private` with **PascalCase** for variables exposed to the Unity Inspector (e.g., `[SerializeField] private float MoveSpeed;`).
- **Local Variables & Parameters**: Use **camelCase**.
- **Private/Protected Fields**: Use **_camelCase** with an underscore prefix (e.g., `private float _currentHealth;`).
- **No Public Fields**: NEVER create raw `public` variables. Always use auto-properties or explicit properties with backing fields (e.g., `public float CurrentHealth => _currentHealth;`).

# 3. Class Organization (Top-to-Bottom Structure)
You MUST structure the contents of any class (especially `MonoBehaviour`) in the following exact order:
1. **Fields**: `[SerializeField] private` variables first, then `public/internal`, then `private/protected`.
2. **Properties & Events**: `Action`, `Func`, and public getter/setters.
3. **Unity Lifecycle Methods**: Ordered chronologically: `Awake()` -> `OnEnable()` -> `Start()` -> `Update()` -> `FixedUpdate()` -> `OnDisable()` -> `OnDestroy()`.
4. **Custom Methods & Logic**: Grouped by responsibility. `public` methods first, followed by their helper `private` methods.

# 4. Unity Performance Rules (CRITICAL)
- **No CPU Bottlenecks**: NEVER use `GetComponent()`, `Find()`, `FindObjectOfType()`, `FindWithTag()`, or similar expensive calls inside `Update()`, `FixedUpdate()`, or any loops. ALWAYS cache these references in `Awake()` or `Start()`.
- **No Empty Callbacks**: NEVER generate empty Unity lifecycle methods (e.g., `private void Start() {}`). If a callback has no logic, remove it entirely to prevent Native-to-Managed overhead.
- **No GC Alloc in Update**: NEVER use the `new` keyword to create objects, arrays, or lists inside `Update()`, `FixedUpdate()`, or frequently called loops. NEVER use string concatenation (e.g., `"Score: " + score`) in loops. Use Event-driven architecture or Object Pooling instead.
- **Guard Clauses**: NEVER write deeply nested `if-else` blocks. ALWAYS use early returns (Guard Clauses) at the beginning of methods to check for `null` or invalid states (e.g., `if (enemy == null) return;`).

# 5. Agent Execution Constraints
- Do not make assumptions about undefined systems; ask for clarification via Mission Control / Unified Inbox if project-specific mechanics are unclear.
- When refactoring, ensure that the formatting exactly matches the aforementioned `.editorconfig` rules.