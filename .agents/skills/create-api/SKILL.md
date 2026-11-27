---
name: create-api
description: Create or modify REST API endpoints in the UIAMovie ASP.NET Core backend following its existing Clean Architecture, DTO, validation, repository, dependency injection, authorization, and response conventions. Use when implementing a new API endpoint or extending an existing endpoint.
---

# Create API — UIAMovie

## Objective

Implement API features using the existing UIAMovie architecture and conventions. Do not introduce a new architecture or duplicate existing abstractions.

## Workflow

### Step 1 — Understand the existing code

Before editing:

1. Read the root `AGENTS.md`.
2. Inspect the solution and relevant project files.
3. Find similar existing endpoints and trace their complete implementation.
4. Inspect the related Controller, Application Service, DTO, Validator, repository interface, repository implementation, entity and DI registration.
5. Identify existing response, authorization and error-handling conventions.

Do not assume a class or method exists without checking the source.

### Step 2 — Plan the implementation

Describe:

- Files to create or modify.
- Responsibilities of each layer.
- Request and response DTOs.
- Validation and authorization requirements.
- Database and cache impact.
- Tests needed.

For a large or security-sensitive change, wait for user approval before editing.

### Step 3 — Implement by layer

Follow the actual project conventions:

- Domain: add or modify domain entities/rules only when necessary.
- Application: implement business logic, DTOs, interfaces and validators.
- Infrastructure: implement persistence or external-service abstractions.
- API: expose the endpoint through a focused Controller action.
- Tests: add tests for new business rules and important edge cases.

Do not put business logic in Controllers.

Do not create a new repository if an existing repository already provides the required operation.

Do not expose EF Core entities directly as API responses.

### Step 4 — Security and correctness

Verify:

- Authentication and authorization requirements.
- Ownership checks for user-specific resources.
- Server-side validation.
- Pagination and query limits where applicable.
- Cancellation and asynchronous I/O conventions.
- Cache invalidation if data changes.
- No sensitive information in logs or responses.
- Frontend API contract compatibility.

Never trust client-supplied prices, roles, subscription status or payment status.

### Step 5 — Verify

Run the relevant build and tests using the actual solution/project structure.

Inspect the final diff.

Do not claim success if a command was not executed or failed.

## Completion report

Report:

1. Endpoint and behavior implemented.
2. Files changed.
3. Architecture decisions.
4. Validation and authorization.
5. Build/test commands and actual results.
6. API compatibility, migration or cache implications.
7. Remaining risks or work.