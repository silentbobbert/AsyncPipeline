using System;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace AsyncPipeline
{
    public static class PipelineExtensions
    {
        public static TOutput Step<TInput, TOutput, TInputOuter, TOutputOuter>(this TInput inputType,
            PipelineBuilder<TInputOuter, TOutputOuter> pipelineBuilder,
            Func<TInput, TOutput> step,
            ChannelOptions? channelOptions = null,
            int degreeOfParallelism = 1)
        {
            if (pipelineBuilder is null) throw new ArgumentNullException(nameof(pipelineBuilder));
            if (step is null) throw new ArgumentNullException(nameof(step));

            pipelineBuilder.GenerateStep<TInput, TOutput>(input => new ValueTask<TOutput>(step(input)), channelOptions, degreeOfParallelism);
            return default(TOutput);
        }

        public static TOutput Step<TInput, TOutput, TInputOuter, TOutputOuter>(this TInput inputType,
            PipelineBuilder<TInputOuter, TOutputOuter> pipelineBuilder,
            Func<TInput, ValueTask<TOutput>> step,
            ChannelOptions? channelOptions = null,
            int degreeOfParallelism = 1)
        {
            if (pipelineBuilder is null) throw new ArgumentNullException(nameof(pipelineBuilder));
            if (step is null) throw new ArgumentNullException(nameof(step));

            pipelineBuilder.GenerateStep<TInput, TOutput>(step, channelOptions, degreeOfParallelism);
            return default(TOutput);
        }
    }
}
