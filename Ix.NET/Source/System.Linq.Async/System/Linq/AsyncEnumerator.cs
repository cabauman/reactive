﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information. 

using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace System.Collections.Generic
{
    /// <summary>
    /// Provides a set of extension methods for <see cref="IAsyncEnumerator{T}"/>.
    /// </summary>
    public static partial class AsyncEnumerator
    {
        /// <summary>
        /// Creates a new enumerator using the specified delegates implementing the members of <see cref="IAsyncEnumerator{T}"/>.
        /// </summary>
        /// <typeparam name="T">The type of the elements returned by the enumerator.</typeparam>
        /// <param name="moveNext">The delegate implementing the <see cref="IAsyncEnumerator{T}.MoveNextAsync"/> method.</param>
        /// <param name="current">The delegate implementing the <see cref="IAsyncEnumerator{T}.Current"/> property getter.</param>
        /// <param name="dispose">The delegate implementing the <see cref="IAsyncDisposable.DisposeAsync"/> method.</param>
        /// <returns>A new enumerator instance.</returns>
        public static IAsyncEnumerator<T> Create<T>(Func<ValueTask<bool>> moveNext, Func<T> current, Func<ValueTask> dispose)
        {
            if (moveNext == null)
                throw Error.ArgumentNull(nameof(moveNext));

            // Note: Many methods pass null in for the second two params. We're assuming
            // That the caller is responsible and knows what they're doing
            return new AnonymousAsyncIterator<T>(moveNext, current, dispose);
        }

        /// <summary>
        /// Advances the enumerator to the next element in the sequence, returning the result asynchronously.
        /// </summary>
        /// <typeparam name="T">The type of the elements returned by the enumerator.</typeparam>
        /// <param name="source">The enumerator to advance.</param>
        /// <param name="cancellationToken">Cancellation token that can be used to cancel the operation.</param>
        /// <returns>
        /// Task containing the result of the operation: true if the enumerator was successfully advanced
        /// to the next element; false if the enumerator has passed the end of the sequence.
        /// </returns>
        public static ValueTask<bool> MoveNextAsync<T>(this IAsyncEnumerator<T> source, CancellationToken cancellationToken)
        {
            if (source == null)
                throw Error.ArgumentNull(nameof(source));

            cancellationToken.ThrowIfCancellationRequested();

            return source.MoveNextAsync();
        }

        internal static IAsyncEnumerator<T> Create<T>(Func<TaskCompletionSource<bool>, ValueTask<bool>> moveNext, Func<T> current, Func<ValueTask> dispose)
        {
            return new AnonymousAsyncIterator<T>(
                async () =>
                {
                    var tcs = new TaskCompletionSource<bool>();

                    return await moveNext(tcs).ConfigureAwait(false);
                },
                current,
                dispose
            );
        }

        private sealed class AnonymousAsyncIterator<T> : AsyncIterator<T>
        {
            private readonly Func<T> _currentFunc;
            private readonly Func<ValueTask<bool>> _moveNext;
            private Func<ValueTask> _dispose;

            public AnonymousAsyncIterator(Func<ValueTask<bool>> moveNext, Func<T> currentFunc, Func<ValueTask> dispose)
            {
                Debug.Assert(moveNext != null);

                _moveNext = moveNext;
                _currentFunc = currentFunc;
                _dispose = dispose;

                // Explicit call to initialize enumerator mode
                GetAsyncEnumerator(default);
            }

            public override AsyncIteratorBase<T> Clone()
            {
                throw new NotSupportedException("AnonymousAsyncIterator cannot be cloned. It is only intended for use as an iterator.");
            }

            public override async ValueTask DisposeAsync()
            {
                var dispose = Interlocked.Exchange(ref _dispose, null);

                if (dispose != null)
                {
                    await dispose().ConfigureAwait(false);
                }

                await base.DisposeAsync().ConfigureAwait(false);
            }

            protected override async ValueTask<bool> MoveNextCore()
            {
                switch (_state)
                {
                    case AsyncIteratorState.Allocated:
                        _state = AsyncIteratorState.Iterating;
                        goto case AsyncIteratorState.Iterating;

                    case AsyncIteratorState.Iterating:
                        if (await _moveNext().ConfigureAwait(false))
                        {
                            _current = _currentFunc();
                            return true;
                        }

                        await DisposeAsync().ConfigureAwait(false);
                        break;
                }

                return false;
            }
        }
    }
}
