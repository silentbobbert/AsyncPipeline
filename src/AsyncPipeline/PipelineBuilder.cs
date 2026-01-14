using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace AsyncPipeline
{
    public sealed class PipelineBuilder<TPipeIn, TPipeOut> : IAsyncDisposable
    {
        private readonly List<IStep> _steps = new();
        private ChannelWriter<PipelineItem<TPipeIn>>? _firstWriter;
        private bool _started;
        private bool _disposed;

        internal interface IStep
        {
            Type InType { get; }
            Type OutType { get; }
            void BindNext(IStep? next);
            void Start(CancellationToken pipelineCancellation);
        }

        internal interface IStepWriter<T>
        {
            ChannelWriter<PipelineItem<T>> Writer { get; }
        }

        internal sealed class PipelineItem<T>
        {
            public required T Input { get; init; }
            public required TaskCompletionSource<TPipeOut> Completion { get; init; }
            public CancellationToken Cancellation { get; init; }
        }

        internal sealed class Step<TStepIn, TStepOut> : IStep, IStepWriter<TStepIn>, IAsyncDisposable
        {
            private readonly Channel<PipelineItem<TStepIn>> _input;
            private readonly int _degreeOfParallelism;
            private readonly CancellationTokenSource _stopCts = new();

            private ChannelWriter<PipelineItem<TStepOut>>? _nextWriter;
            private bool _isLast;

            public Step(Func<TStepIn, ValueTask<TStepOut>> action, ChannelOptions? options, int degreeOfParallelism)
            {
                Action = action ?? throw new ArgumentNullException(nameof(action));
                _degreeOfParallelism = Math.Max(1, degreeOfParallelism);

                _input = options switch
                {
                    BoundedChannelOptions bounded => Channel.CreateBounded<PipelineItem<TStepIn>>(bounded),
                    UnboundedChannelOptions unbounded => Channel.CreateUnbounded<PipelineItem<TStepIn>>(unbounded),
                    null => Channel.CreateUnbounded<PipelineItem<TStepIn>>(new UnboundedChannelOptions { SingleReader = false, SingleWriter = false }),
                    _ => Channel.CreateUnbounded<PipelineItem<TStepIn>>(new UnboundedChannelOptions { SingleReader = false, SingleWriter = false })
                };
            }

            public Func<TStepIn, ValueTask<TStepOut>> Action { get; }

            public ChannelWriter<PipelineItem<TStepIn>> Writer => _input.Writer;

            public Type InType => typeof(TStepIn);
            public Type OutType => typeof(TStepOut);

            public void BindNext(IStep? next)
            {
                if (next is null)
                {
                    _isLast = true;
                    _nextWriter = null;
                    return;
                }

                if (next is IStepWriter<TStepOut> typed)
                {
                    _nextWriter = typed.Writer;
                    return;
                }

                throw new InvalidOperationException($"Invalid pipeline shape. Step output type '{typeof(TStepOut)}' does not match next input type '{next.InType}'.");
            }

            public void Start(CancellationToken pipelineCancellation)
            {
                var linked = CancellationTokenSource.CreateLinkedTokenSource(_stopCts.Token, pipelineCancellation);
                for (var i = 0; i < _degreeOfParallelism; i++)
                {
                    _ = RunAsync(linked.Token);
                }
            }

            private async Task RunAsync(CancellationToken ct)
            {
                Exception? completionError = null;
                try
                {
                    await foreach (var item in _input.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                    {
                        if (item.Cancellation.IsCancellationRequested)
                        {
                            item.Completion.TrySetCanceled(item.Cancellation);
                            continue;
                        }

                        TStepOut output;
                        try
                        {
                            output = await Action(item.Input).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException oce) when (item.Cancellation.CanBeCanceled)
                        {
                            item.Completion.TrySetCanceled(oce.CancellationToken);
                            continue;
                        }
                        catch (Exception ex)
                        {
                            item.Completion.TrySetException(ex);
                            continue;
                        }

                        if (_isLast)
                        {
                            if (output is TPipeOut res)
                            {
                                item.Completion.TrySetResult(res);
                            }
                            else
                            {
                                item.Completion.TrySetResult(default!);
                            }
                        }
                        else
                        {
                            if (_nextWriter is null)
                            {
                                item.Completion.TrySetException(new InvalidOperationException("Pipeline misconfigured: missing next step."));
                                continue;
                            }

                            await _nextWriter.WriteAsync(new PipelineItem<TStepOut>
                            {
                                Input = output,
                                Completion = item.Completion,
                                Cancellation = item.Cancellation
                            }, ct).ConfigureAwait(false);
                        }
                    }
                }
                catch (Exception ex)
                {
                    completionError = ex;
                }
                finally
                {
                    // If the reader loop exits due to pipeline cancellation/fault, we don't have a good way
                    // to enumerate in-flight items. Callers can still cancel their returned tasks.
                    if (completionError is not null)
                    {
                        // no-op: background worker faulting should not crash the process; surfaced via debugging/logging.
                    }
                }
            }

            public ValueTask DisposeAsync()
            {
                _stopCts.Cancel();
                _input.Writer.TryComplete();
                _stopCts.Dispose();
                return ValueTask.CompletedTask;
            }
        }

        private readonly CancellationTokenSource _pipelineCts = new();

        public PipelineBuilder(Func<TPipeIn, PipelineBuilder<TPipeIn, TPipeOut>, TPipeOut> steps)
        {
            if (steps is null) throw new ArgumentNullException(nameof(steps));
            steps.Invoke(default!, this);
        }

        public PipelineBuilder(Func<TPipeIn, PipelineBuilder<TPipeIn, TPipeOut>, ValueTask<TPipeOut>> steps)
        {
            if (steps is null) throw new ArgumentNullException(nameof(steps));
            steps.Invoke(default!, this);
        }

        public Task<TPipeOut> ExecuteAsync(TPipeIn input, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            EnsureStarted();

            if (_steps.Count == 0)
            {
                return Task.FromException<TPipeOut>(new InvalidOperationException("Pipeline has no steps."));
            }

            if (_firstWriter is null)
            {
                return Task.FromException<TPipeOut>(new InvalidOperationException("Pipeline misconfigured: missing first step writer."));
            }
            var tcs = new TaskCompletionSource<TPipeOut>(TaskCreationOptions.RunContinuationsAsynchronously);

            // If the pipeline is disposed/cancelled, cancel the returned task (unless it already completed).
            CancellationTokenRegistration pipelineReg = default;
            if (_pipelineCts.Token.CanBeCanceled)
            {
                pipelineReg = _pipelineCts.Token.Register(() => tcs.TrySetCanceled(_pipelineCts.Token));
            }
            _ = tcs.Task.ContinueWith(static (_, s) => ((CancellationTokenRegistration)s!).Dispose(), pipelineReg,
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

            // If the caller cancels their token, cancel the returned task even if the pipeline step ignores it.
            CancellationTokenRegistration execReg = default;
            if (cancellationToken.CanBeCanceled)
            {
                execReg = cancellationToken.Register(static s => ((TaskCompletionSource<TPipeOut>)s!).TrySetCanceled(), tcs);
                _ = tcs.Task.ContinueWith(static (_, s) => ((CancellationTokenRegistration)s!).Dispose(), execReg,
                    CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }

            if (!_firstWriter.TryWrite(new PipelineItem<TPipeIn>
            {
                Input = input,
                Completion = tcs,
                Cancellation = cancellationToken
            }))
            {
                tcs.TrySetException(new InvalidOperationException("Pipeline is not accepting new items."));
            }

            return tcs.Task;
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;

            _pipelineCts.Cancel();
            foreach (var step in _steps)
            {
                if (step is IAsyncDisposable ad)
                {
                    await ad.DisposeAsync().ConfigureAwait(false);
                }
            }
            _pipelineCts.Dispose();
        }

        internal Step<TStepIn, TStepOut> GenerateStep<TStepIn, TStepOut>(Func<TStepIn, ValueTask<TStepOut>> action, ChannelOptions? options = null, int degreeOfParallelism = 1)
        {
            ThrowIfDisposed();
            if (_started) throw new InvalidOperationException("Cannot add steps after pipeline has started.");

            var step = new Step<TStepIn, TStepOut>(action, options, degreeOfParallelism);
            if (_steps.Count == 0)
            {
                if (typeof(TStepIn) != typeof(TPipeIn))
                {
                    throw new InvalidOperationException($"First step input type '{typeof(TStepIn)}' must match pipeline input type '{typeof(TPipeIn)}'.");
                }
                _firstWriter = (ChannelWriter<PipelineItem<TPipeIn>>)(object)step.Writer;
            }
            _steps.Add(step);
            return step;
        }

        private void EnsureStarted()
        {
            if (_started) return;
            _started = true;

            for (var i = 0; i < _steps.Count; i++)
            {
                var next = i == _steps.Count - 1 ? null : _steps[i + 1];
                _steps[i].BindNext(next);
            }

            foreach (var step in _steps)
            {
                step.Start(_pipelineCts.Token);
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(PipelineBuilder<TPipeIn, TPipeOut>));
        }
    }
}
