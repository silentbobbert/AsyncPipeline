using System;

namespace AsyncPipeline
{
    public static class PipelineExtensions
    {
        public static TOutput Step<TInput, TOutput, TInputOuter, TOutputOuter>(this TInput inputType,
            PipelineBuilder<TInputOuter, TOutputOuter> pipelineBuilder,
            Func<TInput, TOutput> step)
        {
            var pipelineStep = pipelineBuilder.GenerateStep<TInput, TOutput>();
            pipelineStep.StepAction = step;
            return default(TOutput);
        }
    }
}
