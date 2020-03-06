using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

// Inspiration taken from https://michaelscodingspot.com/pipeline-pattern-implementations-csharp/

namespace AsyncPipeline
{
    public class PipelineBuilder<TPipeIn, TPipeOut>
    {
        private readonly List<object> _pipelineSteps = new List<object>();

        public interface IPipelineStep<TStepIn>
        {
            BlockingCollection<PipelineItem<TStepIn>> Buffer { get; set; }
        }
        public class PipelineStep<TStepIn, TStepOut> : IPipelineStep<TStepIn>
        {
            public BlockingCollection<PipelineItem<TStepIn>> Buffer { get; set; } = new BlockingCollection<PipelineItem<TStepIn>>();
            public Func<TStepIn, TStepOut> StepAction { get; set; }
        }
        public class PipelineItem<T>
        {
            public T Input { get; set; }
            public TaskCompletionSource<TPipeOut> TaskCompletionSource { get; set; }
        }

        public PipelineBuilder(Func<TPipeIn, PipelineBuilder<TPipeIn, TPipeOut>, TPipeOut> steps)
        {
            steps.Invoke(default, this); //Invoke just once to build blocking collections
        }

        public Task<TPipeOut> ExecuteAsync(TPipeIn input)
        {
            var first = _pipelineSteps[0] as IPipelineStep<TPipeIn>;
            TaskCompletionSource<TPipeOut> tsk = new TaskCompletionSource<TPipeOut>();
            first.Buffer.Add(new PipelineItem<TPipeIn>()
            {
                Input = input,
                TaskCompletionSource = tsk
            });
            return tsk.Task;
        }

        public PipelineStep<TStepIn, TStepOut> GenerateStep<TStepIn, TStepOut>()
        {
            var pipelineStep = new PipelineStep<TStepIn, TStepOut>();
            var stepIndex = _pipelineSteps.Count;

            Task.Run(() =>
            {
                IPipelineStep<TStepOut> nextPipelineStep = null;

                foreach (var input in pipelineStep.Buffer.GetConsumingEnumerable())
                {
                    bool isLastStep = stepIndex == _pipelineSteps.Count - 1;
                    TStepOut output;
                    try
                    {
                        output = pipelineStep.StepAction(input.Input);
                    }
                    catch (Exception e)
                    {
                        input.TaskCompletionSource.SetException(e);
                        continue;
                    }
                    if (isLastStep)
                    {
                        input.TaskCompletionSource.SetResult(output is TPipeOut res ? res : default);
                    }
                    else
                    {
                        nextPipelineStep = nextPipelineStep ?? (isLastStep ? null : _pipelineSteps[stepIndex + 1] as IPipelineStep<TStepOut>);
                        nextPipelineStep.Buffer.Add(new PipelineItem<TStepOut>() { Input = output, TaskCompletionSource = input.TaskCompletionSource });
                    }
                }
            });

            _pipelineSteps.Add(pipelineStep);
            return pipelineStep;

        }
    }
}