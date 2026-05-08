# Dignite Paperbase Agent Guide

This repository contains an ABP-based project. Before making code changes, read and follow the relevant rules under `.cursor/rules`.

## Rule Loading

- Start by checking `.cursor/rules` and load only the files relevant to the current task.
- For backend work, prefer the applicable files under `.cursor/rules/framework/common`.
- For ABP background jobs, also read `.cursor/rules/framework/common/background-jobs.mdc`.
- For EF Core work, also read `.cursor/rules/framework/data/ef-core.mdc`.
- For tests, also read `.cursor/rules/framework/testing/patterns.mdc`.
- For Angular UI work, also read `.cursor/rules/framework/ui/angular.mdc`.
- For reusable module work, also read `.cursor/rules/template/module.mdc`.

## Project Layout

- `core/` contains the ABP application core.
- `modules/` contains reusable business modules.
- `host/` contains the single-tenant test host and is the place to configure middleware in `OnApplicationInitialization`.
- `docs/` contains developer-facing documentation organized by feature (e.g. `text-extraction.md`, `embedding.md`, `document-chat.md`) and internal design documents under `docs/design/`.

## Project Rules

- Follow ABP dependency injection conventions. Do not manually call `AddScoped`, `AddTransient`, or `AddSingleton` unless a project rule explicitly allows it.
- When developing reusable modules, all public and protected methods must be `virtual`.
- Do not configure middleware inside reusable modules; configure middleware only in `host/`.
- Keep changes scoped to the requested task and aligned with the existing project structure.

## Issue vs Direct Change

Use a lightweight standard when deciding whether to create an issue before making improvements discovered during collaboration:

- Directly fix small, clear, low-risk issues when the scope is obvious, such as stale comments, typos, small documentation corrections, or narrowly scoped consistency fixes.
- Create an issue first when the change affects business rules, domain semantics, multiple layers/modules, public APIs, migrations, or requires team discussion.
- Create an issue for problems that are discovered but intentionally left out of the current task scope.
- For recurring conventions or workflow standards, discuss first, then document the accepted rule in `AGENTS.md`, `.cursor/rules`, or project docs.
- Keep direct fixes scoped. Do not expand a small correction into unrelated cleanup.
- When unsure whether a discovered improvement should be fixed directly or tracked as an issue, prefer direct fixes only if the change is small, non-behavioral, and clearly correct; otherwise ask or create an issue.
