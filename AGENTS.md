# Repository Guidelines

## Project Structure & Module Organization

- `Jobs/` contains the core orchestration library: `Connectors/` reads records, `Pipelines/` processes them, `Sinks/` writes results, `States/` manages state, and `JobsEntities/` provides job building, runtime, and checkpointing. Contracts live in adjacent `Interfaces/` directories.
- `JobsOperator/` is the console runner; `Test.json` supplies the default sample input.
- `JobsOperator.Web/` is the Blazor Server dashboard. UI components live in `Components/`, execution management in `Services/`, and static assets in `wwwroot/`.
- `TestBuilds/` contains example `IJob` implementations, including the Kafka relay; it is not an automated test project.
- `compose.yaml` defines local Kafka infrastructure and the containerized runner.

## Build, Test, and Development Commands

Use the .NET 10 SDK and run commands from the repository root:

- `dotnet restore JobsOperator.sln` restores NuGet dependencies.
- `dotnet build JobsOperator.sln` builds all four projects.
- `dotnet run --project JobsOperator` runs the JSON sample job in an interactive console.
- `dotnet watch --project JobsOperator.Web` starts the dashboard with development reloads.
- `docker compose up -d --build` builds the runner and starts the Kafka relay stack. Docker is required.
- `docker compose down` stops the stack while retaining persisted volumes.

## Coding Style & Naming Conventions

Follow existing C# style: four-space indentation, file-scoped namespaces, and braces on separate lines. Use PascalCase for types and public members, camelCase for parameters and locals, `_camelCase` for private fields, and `I` prefixes for interfaces. Match filenames to their main types and suffix asynchronous methods with `Async`. Keep nullable reference types enabled and propagate cancellation tokens through asynchronous work. Preserve surrounding XML documentation conventions. No repository-wide formatter or linter configuration is checked in.

## Method Documentation

Document every method with XML comments. Do not end documentation text with periods, including `<summary>`, `<param>`, `<typeparam>`, and `<returns>` descriptions. Describe every input parameter, generic type parameter, and `out` or `ref` parameter, including its input/output semantics. Use `<returns>` to describe every non-void return value, including the result of asynchronous operations; for a non-generic `Task` or `ValueTask`, describe what completion represents. For `void` methods, omit `<returns>`.

## Engineering Principles & Agent Instructions

Act as an expert in developing high-load, fault-tolerant systems. Follow Domain-Driven Design (DDD), Clean Code principles, and established C#/.NET best practices. Keep domain responsibilities explicit, methods focused, and dependencies aligned with existing module boundaries. Consider concurrency, bounded resource usage, cancellation, failure recovery, and checkpoint consistency when changing execution paths. Preserve the project structure and module responsibilities described above; place new code within the existing organization unless restructuring is explicitly requested.

## Testing Guidelines

No automated test framework or coverage threshold is configured. Validate changes with a solution build and relevant sample runs. For pipeline or checkpoint changes, verify output and restart/recovery behavior; for dashboard changes, exercise job upload and execution. Document manual steps and results in the PR. When introducing automated tests, use descriptive names such as `RestoreAsync_WithCheckpoint_ResumesPosition` and document their runner command.

## Commit & Pull Request Guidelines

Recent commits use short Russian descriptions such as `Доработки`; no prefix convention is evident. Use focused summaries. PRs should explain changes, link relevant issues, report validation, and include UI screenshots.

## Configuration & Runtime Data

Select Kafka execution with `JOB_MODE=kafka-relay`; configure `KAFKA_BOOTSTRAP_SERVERS`, `KAFKA_INPUT_TOPIC`, `KAFKA_OUTPUT_TOPIC`, and `KAFKA_CHECKPOINT_PATH` as needed. Keep credentials and generated uploads/checkpoints out of commits. Load only trusted job assemblies.
