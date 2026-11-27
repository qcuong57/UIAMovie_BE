---
name: write-tests
description: Create or update automated tests for UIAMovie backend business logic and API behavior using the project's existing test framework, conventions, fixtures, and dependencies.
---

# Write Tests — UIAMovie

## Objective

Add meaningful regression tests for the requested behavior without changing production code unnecessarily.

## Workflow

### Step 1 — Inspect existing tests

1. Read `AGENTS.md`.
2. Inspect `UIAMovie.Tests` and the actual test project structure.
3. Identify the test framework, assertion library and mocking tools already installed.
4. Find similar tests and reuse their setup, naming and fixture patterns.
5. Inspect the production code and its dependencies.

Do not introduce a new testing library without a clear reason.

### Step 2 — Identify test cases

Consider:

- Normal successful behavior.
- Invalid input.
- Missing or nonexistent resources.
- Unauthorized and forbidden access.
- Boundary values.
- Null and empty values.
- Duplicate requests.
- Concurrency and idempotency where relevant.
- Failure of external dependencies.
- Database and cache consistency.

Only add cases relevant to the behavior being tested.

### Step 3 — Write tests

- Keep each test focused on one behavior.
- Use descriptive test names.
- Arrange, Act and Assert clearly.
- Mock external dependencies at the appropriate boundary.
- Avoid testing private implementation details.
- Do not mock the method under test.
- Do not depend on production credentials or live payment gateways.
- Do not make tests depend on execution order.

### Step 4 — Verify

1. Run the relevant test project.
2. Run the full test suite if practical.
3. Report failures honestly.
4. Do not modify assertions merely to make incorrect production behavior pass.

## Completion report

Report:

- Behaviors covered.
- Test files created or modified.
- Test command executed.
- Passed and failed test counts.
- Any behavior that still lacks coverage.