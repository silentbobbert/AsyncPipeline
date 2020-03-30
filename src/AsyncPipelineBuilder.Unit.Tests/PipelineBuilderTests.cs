using AsyncPipeline;
using System;
using System.Threading.Tasks;
using Xunit;

namespace AsyncPipelineBuilder.Unit.Tests
{
    public class PipelineBuilderTests
    {
        [Theory]
        [InlineData("The pipeline pattern is the best pattern!", false)]
        [InlineData("The pipeline pattern is the best pattern!!", true)]
        public async Task PipelineBuilder_should_create_an_executable_simple_pipeline(string input, bool expected)
        {
            // Arrange
            var sut = new PipelineBuilder<string, bool>((inputFirst, builder) =>
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

            var sut = new PipelineBuilder<string, bool>((inputFirst, builder) =>
                    inputFirst
                        .Step(builder, first => first.Length) // First step takes the input and returns its length
                        .Step(builder, length => // Second step has a problem
                        {
                            throw expected;
                            return true;
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

            var sut = new PipelineBuilder<string, bool>((inputFirst, builder) =>
                    inputFirst
                        .Step(builder, first => first.Length) // First step takes the input and returns its length
                        .Step(builder, length => // Second step has a problem
                        {
                            throw expected;
                            return true;
                        })
                        .Step(builder, success => { counter++; return true; }) // Wont execute
                    );

            // Act & Assert
            var actual = await Assert.ThrowsAsync<Exception>(() => sut.ExecuteAsync("test"));
            Assert.Equal(expected, actual);
            Assert.Equal(0, counter);
        }
    }
}
