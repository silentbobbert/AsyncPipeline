# Async Pipeline

Build a flexible, chainable pipeline that executes steps asynchronously.

`1.x` targets `.NET 8` and uses `System.Threading.Channels` internally.

## Installation

PowerShell:

```
Install-Package Ian.Robertson.AsyncPipeline
```

dotnet CLI:

```
dotnet add package Ian.Robertson.AsyncPipeline
```

Or add a reference in your project file:

```
<PackageReference Include="Ian.Robertson.AsyncPipeline" Version="1.0.0" />
```

## What's new in 1.0.0

- Target framework updated to `.NET 8`.
- Pipeline internals rewritten to use `System.Threading.Channels` (no `BlockingCollection` / `Task.Run` worker threads).
- Steps can be synchronous (`Func<TIn, TOut>`) or asynchronous (`Func<TIn, ValueTask<TOut>>`).
- Optional per-step `degreeOfParallelism`.
- `ExecuteAsync` supports per-call cancellation.
- Disposing the pipeline cancels in-flight executions.

## Usage

The unit tests in `AsyncPipelineBuilder.Unit.Tests/PipelineBuilderTests.cs` show typical usage.

### Simple pipeline example

```csharp
public Task<bool> CreateAndRunPipeline(string input)
{
    var pipeline = new PipelineBuilder<string, bool>((inputFirst, builder) =>
        inputFirst
            .Step(builder, first => first.Length)
            .Step(builder, length => length % 2 == 0));

    return pipeline.ExecuteAsync(input);
}
```

### Async steps + cancellation

```csharp
public async Task<bool> CreateAndRunPipelineAsync(string input, CancellationToken ct)
{
    await using var pipeline = new PipelineBuilder<string, bool>((inputFirst, builder) =>
        inputFirst
            .Step(builder, async s =>
            {
                await Task.Delay(10);
                return s.Length;
            })
            .Step(builder, len => len % 2 == 0));

    return await pipeline.ExecuteAsync(input, ct);
}
```

### Per-step parallelism

```csharp
await using var pipeline = new PipelineBuilder<int, int>((inputFirst, builder) =>
    inputFirst
        .Step(builder, x => x + 1, degreeOfParallelism: 4)
        .Step(builder, x => x * 2, degreeOfParallelism: 4));

var result = await pipeline.ExecuteAsync(10);
```
