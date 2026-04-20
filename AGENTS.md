# Dignite Paperbase Agent Guide

This repository contains an ABP-based project. Before making code changes, read and follow the relevant rules under `.cursor/rules`.

## Rule Loading

- Start by checking `.cursor/rules` and load only the files relevant to the current task.
- For backend work, prefer the applicable files under `.cursor/rules/framework/common`.
- For EF Core work, also read `.cursor/rules/framework/data/ef-core.mdc`.
- For tests, also read `.cursor/rules/framework/testing/patterns.mdc`.
- For Angular UI work, also read `.cursor/rules/framework/ui/angular.mdc`.
- For reusable module work, also read `.cursor/rules/template/module.mdc`.

## Project Layout

- `core/` contains the ABP application core.
- `modules/` contains reusable business modules.
- `host/` contains the single-tenant test host and is the place to configure middleware in `OnApplicationInitialization`.
- `docs/` contains project documentation.

## Project Rules

- Follow ABP dependency injection conventions. Do not manually call `AddScoped`, `AddTransient`, or `AddSingleton` unless a project rule explicitly allows it.
- When developing reusable modules, all public and protected methods must be `virtual`.
- Do not configure middleware inside reusable modules; configure middleware only in `host/`.
- Keep changes scoped to the requested task and aligned with the existing project structure.
