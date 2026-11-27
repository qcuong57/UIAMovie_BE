---
name: code-review
description: Review UIAMovie backend code for bugs, security vulnerabilities, Clean Architecture violations, performance issues, data consistency problems, and API contract regressions. Use when reviewing a diff, pull request, feature, or existing implementation.
---

# Code Review — UIAMovie

## Objective

Find actionable defects. Prioritize correctness, security and data integrity over stylistic preferences.

## Workflow

1. Read `AGENTS.md`.
2. Inspect the Git diff and identify the intended change.
3. Read the complete changed methods and relevant callers.
4. Trace affected flows across API, Application, Domain and Infrastructure.
5. Inspect related DTOs, validators, DI registrations, EF Core configuration and tests.
6. Check frontend/API contract compatibility when response or request models change.

## Review priorities

### Critical — Security

- Missing authentication or authorization.
- Broken resource ownership checks.
- Client-controlled price, role, subscription or payment state.
- Exposed passwords, tokens, OTPs, secrets or internal identifiers.
- Unsafe URL handling or upload validation.
- Payment callback signature, amount or idempotency weaknesses.
- Race conditions in security-sensitive operations.

### High — Correctness and data integrity

- Incorrect business rules.
- Null handling and invalid state transitions.
- Duplicate processing or missing transaction boundaries.
- Concurrency issues.
- Incorrect time-zone or date-boundary calculations.
- Cache invalidation errors.
- Missing database constraints or incorrect delete behavior.

### Medium — Performance and maintainability

- N+1 database queries.
- Unbounded result sets.
- Missing `AsNoTracking()` on suitable read-only queries.
- Unnecessary database round trips.
- Blocking I/O in asynchronous flows.
- Duplicated business logic or architecture violations.

## Important UIAMovie flows

Pay special attention when a change affects:

- Authentication, OTP, 2FA and refresh-token rotation.
- Token revocation.
- Premium eligibility and subscriptions.
- VNPay and SePay callbacks.
- Playback sessions and advertisement enforcement.
- Heartbeat and view tracking.
- Redis cache and invalidation.
- HomeSection automatic rules.
- External integrations and background workers.

## Output format

For each finding, provide:

- Severity: Critical / High / Medium / Low.
- File and line number.
- The concrete problem.
- A realistic failure scenario.
- Recommended correction.

Separate confirmed bugs from potential risks.

Do not report stylistic preferences as bugs.

If no issue is found, state what was reviewed and which tests were or were not executed.

Do not modify code unless the user explicitly asks for fixes.