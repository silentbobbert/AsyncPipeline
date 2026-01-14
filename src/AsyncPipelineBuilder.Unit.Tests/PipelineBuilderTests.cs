using AsyncPipeline;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AsyncPipelineBuilder.Unit.Tests
{
    public class PipelineBuilderTests
    {
        private static T Throw<T>(Exception ex) => throw ex;

        [Theory]
        [InlineData("The pipeline pattern is the best pattern!", false)]
        [InlineData("The pipeline pattern is the best pattern!!", true)]
        public async Task PipelineBuilder_should_create_an_executable_simple_pipeline(string input, bool expected)
        {
            // Arrange
            await using var sut = new PipelineBuilder<string, bool>((inputFirst, builder) =>
                    inputFirst
                        .Step(builder, first => first.Length) // First step takes the input and returns its length
                        .Step(builder, length => length % 2 == 0) // Second step in the chain takes the length from first step and sees if its odd or even.
                    );

            // Act
            var actual = await sut.ExecuteAsync(input);

            // Assert
            Assert.Equal(expected, actual);
        }

        [Fact]
        public async Task PipelineBuilder_should_raise_exceptions()
        {
            // Arrange
            var expected = new Exception("Fake Exception");

            await using var sut = new PipelineBuilder<string, bool>((inputFirst, builder) =>
                    inputFirst
                        .Step(builder, first => first.Length) // First step takes the input and returns its length
                        .Step(builder, length => // Second step has a problem
                        {
                            return Throw<bool>(expected);
                        })
                    );

            // Act & Assert
            var actual = await Assert.ThrowsAsync<Exception>(() => sut.ExecuteAsync("test"));
            Assert.Equal(expected, actual);
        }

        [Fact]
        public async Task PipelineBuilder_should_raise_exceptions_and_not_execute_following_steps()
        {
            // Arrange
            var counter = 0;
            var expected = new Exception("Fake Exception");

            await using var sut = new PipelineBuilder<string, bool>((inputFirst, builder) =>
                    inputFirst
                        .Step(builder, first => first.Length) // First step takes the input and returns its length
                        .Step(builder, length => // Second step has a problem
                        {
                            return Throw<bool>(expected);
                        })
                        .Step(builder, success => { counter++; return true; }) // Wont execute
                    );

            // Act & Assert
            var actual = await Assert.ThrowsAsync<Exception>(() => sut.ExecuteAsync("test"));
            Assert.Equal(expected, actual);
            Assert.Equal(0, counter);
        }

        [Fact]
        public async Task PipelineBuilder_should_support_async_steps()
        {
            await using var sut = new PipelineBuilder<string, bool>((inputFirst, builder) =>
                inputFirst
                    .Step(builder, async first =>
                    {
                        await Task.Delay(10);
                        return first.Length;
                    })
                    .Step(builder, async length =>
                    {
                        await Task.Yield();
                        return length % 2 == 0;
                    }));

            var actual = await sut.ExecuteAsync("abcd");
            Assert.True(actual);
        }

        [Fact]
        public async Task PipelineBuilder_should_support_cancellation_per_execution()
        {
            await using var sut = new PipelineBuilder<int, int>((inputFirst, builder) =>
                inputFirst.Step(builder, async value =>
                {
                    await Task.Delay(500);
                    return value + 1;
                }));

            using var cts = new CancellationTokenSource(millisecondsDelay: 20);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.ExecuteAsync(1, cts.Token));
        }

        [Fact]
        public async Task PipelineBuilder_should_handle_many_concurrent_executions()
        {
            await using var sut = new PipelineBuilder<int, int>((inputFirst, builder) =>
                inputFirst
                    .Step(builder, value => value + 1, degreeOfParallelism: 4)
                    .Step(builder, value => value * 2, degreeOfParallelism: 4));

            var inputs = Enumerable.Range(0, 100).ToArray();
            var tasks = new List<Task<int>>(inputs.Length);

            foreach (var i in inputs)
            {
                tasks.Add(sut.ExecuteAsync(i));
            }

            var results = await Task.WhenAll(tasks);

            for (var i = 0; i < inputs.Length; i++)
            {
                Assert.Equal((inputs[i] + 1) * 2, results[i]);
            }
        }

        [Fact]
        public async Task PipelineBuilder_should_raise_exceptions_from_async_steps()
        {
            var expected = new InvalidOperationException("Boom");

            await using var sut = new PipelineBuilder<int, int>((inputFirst, builder) =>
                inputFirst
                    .Step(builder, async value =>
                    {
                        await Task.Delay(10);
                        return value;
                    })
                    .Step(builder, _ => Throw<int>(expected)));

            var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ExecuteAsync(123));
            Assert.Same(expected, actual);
        }

        [Fact]
        public async Task PipelineBuilder_dispose_should_cancel_in_flight_execution()
        {
            var sut = new PipelineBuilder<int, int>((inputFirst, builder) =>
                inputFirst.Step(builder, async value =>
                {
                    await Task.Delay(5_000);
                    return value + 1;
                }));

            var task = sut.ExecuteAsync(1);

            await sut.DisposeAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task);
        }
    }
}
