# Async Pipeline
Build a flexible chained pipeline that can execute the steps asynchronously

## Installation Instructions
Install the package using your method of choice

Powershell (install latest version)
```
Install-Package Ian.Robertson.AsyncPipeline
```

dotnet CLI
```
dotnet add package Ian.Robertson.AsyncPipeline (install latest version)
```

Or manually add a reference in your project file (making a note of the version you want)
```
<PackageReference Include="Ian.Robertson.AsyncPipeline" Version="0.1.0.1" />
```
## Usage Instructions
The [unit tests](https://github.com/silentbobbert/AsyncPipeline/blob/master/AsyncPipelineBuilder.Unit.Tests/PipelineBuilderTests.cs) are a good place to see how to use this package.

### An example of a pipeline...
This example works on strings to transform them perhaps, or does some other processing on strings. This overly simplistic pipeline example takes a string, calculates it's length in step 1, and passes that result to step 2. Step 2 takes the length provided by step 1 and does further processing on the length, in this case, working out with the number is odd or even.

```
public Task<bool> CreateAndRunPipeline(string input)
{
    var pipeline = new PipelineBuilder<string, bool>((inputFirst, builder) =>
            inputFirst
            // First step takes the input and returns its length
                .Step(builder, first => first.Length) 
            // Second step in the chain takes the length from first step and sees 
            // if its odd or even.
                .Step(builder, length => length % 2 == 1) 
            );
    
    // return the awaitable task to the caller to await
    return pipeline.ExecuteAsync(input);
}
```

The pipeline feature is flexible enough to work on any object type, and you can chain as many steps together as you need to get the end result you want.