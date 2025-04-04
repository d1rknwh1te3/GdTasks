﻿using System.Runtime.CompilerServices;
using GdTaskPlayerLoopAutoload = GdTasks.AutoLoader.GdTaskPlayerLoopAutoload;

namespace GdTasks.Structs;

public struct SwitchToMainThreadAwaitable(PlayerLoopTiming playerLoopTiming, CancellationToken cancellationToken)
{
	public Awaiter GetAwaiter() => new(playerLoopTiming, cancellationToken);

	public struct Awaiter(PlayerLoopTiming playerLoopTiming, CancellationToken cancellationToken)
		: ICriticalNotifyCompletion
	{
		public bool IsCompleted
		{
			get
			{
				var currentThreadId = Thread.CurrentThread.ManagedThreadId;
				return GdTaskPlayerLoopAutoload.MainThreadId == currentThreadId;
				// run immediate.
				// register continuation.
			}
		}

		public void GetResult() => cancellationToken.ThrowIfCancellationRequested();

		public void OnCompleted(Action continuation) => GdTaskPlayerLoopAutoload.AddContinuation(playerLoopTiming, continuation);

		public void UnsafeOnCompleted(Action continuation) => GdTaskPlayerLoopAutoload.AddContinuation(playerLoopTiming, continuation);
	}
}