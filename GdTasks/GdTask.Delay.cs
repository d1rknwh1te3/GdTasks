﻿using Godot;

namespace GdTasks;

public partial struct GdTask
{
	// optimized for single continuation
	public static YieldAwaitable Yield() => new(PlayerLoopTiming.Process);

	// optimized for single continuation
	public static YieldAwaitable Yield(PlayerLoopTiming timing) => new(timing);

	public static GdTask Yield(CancellationToken cancellationToken)
		=> new(YieldPromise.Create(PlayerLoopTiming.Process, cancellationToken, out var token), token);

	public static GdTask Yield(PlayerLoopTiming timing, CancellationToken cancellationToken)
		=> new(YieldPromise.Create(timing, cancellationToken, out var token), token);

	/// <summary>
	/// Similar as GDTask.Yield but guaranteed run on next frame.
	/// </summary>
	public static GdTask NextFrame()
		=> new(NextFramePromise.Create(PlayerLoopTiming.Process, CancellationToken.None, out var token), token);

	/// <summary>
	/// Similar as GDTask.Yield but guaranteed run on next frame.
	/// </summary>
	public static GdTask NextFrame(PlayerLoopTiming timing)
		=> new(NextFramePromise.Create(timing, CancellationToken.None, out var token), token);

	/// <summary>
	/// Similar as GDTask.Yield but guaranteed run on next frame.
	/// </summary>
	public static GdTask NextFrame(CancellationToken cancellationToken)
		=> new(NextFramePromise.Create(PlayerLoopTiming.Process, cancellationToken, out var token), token);

	/// <summary>
	/// Similar as GDTask.Yield but guaranteed run on next frame.
	/// </summary>
	public static GdTask NextFrame(PlayerLoopTiming timing, CancellationToken cancellationToken)
		=> new(NextFramePromise.Create(timing, cancellationToken, out var token), token);

	public static YieldAwaitable WaitForEndOfFrame()
		=> Yield(PlayerLoopTiming.Process);

	public static GdTask WaitForEndOfFrame(CancellationToken cancellationToken)
		=> Yield(PlayerLoopTiming.Process, cancellationToken);

	/// <summary>
	/// Same as GDTask.Yield(PlayerLoopTiming.PhysicsProcess).
	/// </summary>
	public static YieldAwaitable WaitForPhysicsProcess()
		=> Yield(PlayerLoopTiming.PhysicsProcess);

	/// <summary>
	/// Same as GDTask.Yield(PlayerLoopTiming.PhysicsProcess, cancellationToken).
	/// </summary>
	public static GdTask WaitForPhysicsProcess(CancellationToken cancellationToken)
		=> Yield(PlayerLoopTiming.PhysicsProcess, cancellationToken);

	/// <exception cref="ArgumentOutOfRangeException">Delay does not allow minus delayFrameCount.</exception>
	public static GdTask DelayFrame(int delayFrameCount, PlayerLoopTiming delayTiming = PlayerLoopTiming.Process, CancellationToken cancellationToken = default)
	{
		return delayFrameCount < 0
			? throw new ArgumentOutOfRangeException($"Delay does not allow minus delayFrameCount. delayFrameCount:{delayFrameCount}")
			: new GdTask(DelayFramePromise.Create(delayFrameCount, delayTiming, cancellationToken, out var token), token);
	}

	public static GdTask Delay(int millisecondsDelay, PlayerLoopTiming delayTiming = PlayerLoopTiming.Process, CancellationToken cancellationToken = default)
	{
		var delayTimeSpan = TimeSpan.FromMilliseconds(millisecondsDelay);
		return Delay(delayTimeSpan, delayTiming, cancellationToken);
	}

	public static GdTask Delay(TimeSpan delayTimeSpan, PlayerLoopTiming delayTiming = PlayerLoopTiming.Process, CancellationToken cancellationToken = default)
		=> Delay(delayTimeSpan, DelayType.DeltaTime, delayTiming, cancellationToken);

	public static GdTask Delay(int millisecondsDelay, DelayType delayType, PlayerLoopTiming delayTiming = PlayerLoopTiming.Process, CancellationToken cancellationToken = default)
	{
		var delayTimeSpan = TimeSpan.FromMilliseconds(millisecondsDelay);
		return Delay(delayTimeSpan, delayType, delayTiming, cancellationToken);
	}

	public static GdTask Delay(TimeSpan delayTimeSpan, DelayType delayType, PlayerLoopTiming delayTiming = PlayerLoopTiming.Process, CancellationToken cancellationToken = default)
	{
		if (delayTimeSpan < TimeSpan.Zero)
			throw new ArgumentOutOfRangeException($"Delay does not allow minus delayTimeSpan. delayTimeSpan:{delayTimeSpan}");

#if DEBUG
		// force use Realtime.
		if (GdTaskPlayerLoopAutoload.IsMainThread && Engine.IsEditorHint())
			delayType = DelayType.RealTime;
#endif
		switch (delayType)
		{
			case DelayType.RealTime:
				{
					return new GdTask(DelayRealtimePromise.Create(delayTimeSpan, delayTiming, cancellationToken, out var token), token);
				}
			case DelayType.DeltaTime:
			default:
				{
					return new GdTask(DelayPromise.Create(delayTimeSpan, delayTiming, cancellationToken, out var token), token);
				}
		}
	}

	private sealed class YieldPromise : IGdTaskSource, IPlayerLoopItem, ITaskPoolNode<YieldPromise>
	{
		private static TaskPool<YieldPromise> _pool;
		private YieldPromise _nextNode;
		public ref YieldPromise NextNode => ref _nextNode;

		static YieldPromise() => TaskPool.RegisterSizeGetter(typeof(YieldPromise), () => _pool.Size);

		private YieldPromise()
		{ }

		private CancellationToken _cancellationToken;
		private GdTaskCompletionSourceCore<object> _core;

		public static IGdTaskSource Create(PlayerLoopTiming timing, CancellationToken cancellationToken, out short token)
		{
			if (cancellationToken.IsCancellationRequested)
				return AutoResetGdTaskCompletionSource.CreateFromCanceled(cancellationToken, out token);

			if (!_pool.TryPop(out var result))
				result = new YieldPromise();

			result._cancellationToken = cancellationToken;

			TaskTracker.TrackActiveTask(result, 3);
			GdTaskPlayerLoopAutoload.AddAction(timing, result);

			token = result._core.Version;
			return result;
		}

		public void GetResult(short token)
		{
			try
			{
				_core.GetResult(token);
			}
			finally
			{
				TryReturn();
			}
		}

		public GdTaskStatus GetStatus(short token) => _core.GetStatus(token);

		public GdTaskStatus UnsafeGetStatus() => _core.UnsafeGetStatus();

		public void OnCompleted(Action<object> continuation, object state, short token) => _core.OnCompleted(continuation, state, token);

		public bool MoveNext()
		{
			if (_cancellationToken.IsCancellationRequested)
			{
				_core.TrySetCanceled(_cancellationToken);
				return false;
			}

			_core.TrySetResult(null);
			return false;
		}

		private bool TryReturn()
		{
			TaskTracker.RemoveTracking(this);
			_core.Reset();
			_cancellationToken = CancellationToken.None;
			return _pool.TryPush(this);
		}
	}

	private sealed class NextFramePromise : IGdTaskSource, IPlayerLoopItem, ITaskPoolNode<NextFramePromise>
	{
		private static TaskPool<NextFramePromise> _pool;
		private NextFramePromise _nextNode;
		public ref NextFramePromise NextNode => ref _nextNode;

		static NextFramePromise() => TaskPool.RegisterSizeGetter(typeof(NextFramePromise), () => _pool.Size);

		private NextFramePromise()
		{ }

		private bool _isMainThread;
		private ulong _frameCount;
		private CancellationToken _cancellationToken;
		private GdTaskCompletionSourceCore<AsyncUnit> _core;

		public static IGdTaskSource Create(PlayerLoopTiming timing, CancellationToken cancellationToken, out short token)
		{
			if (cancellationToken.IsCancellationRequested)
				return AutoResetGdTaskCompletionSource.CreateFromCanceled(cancellationToken, out token);

			if (!_pool.TryPop(out var result))
				result = new NextFramePromise();

			result._isMainThread = GdTaskPlayerLoopAutoload.IsMainThread;

			if (result._isMainThread)
				result._frameCount = Engine.GetProcessFrames();

			result._cancellationToken = cancellationToken;

			TaskTracker.TrackActiveTask(result, 3);
			GdTaskPlayerLoopAutoload.AddAction(timing, result);

			token = result._core.Version;
			return result;
		}

		public void GetResult(short token)
		{
			try
			{
				_core.GetResult(token);
			}
			finally
			{
				TryReturn();
			}
		}

		public GdTaskStatus GetStatus(short token) => _core.GetStatus(token);

		public GdTaskStatus UnsafeGetStatus() => _core.UnsafeGetStatus();

		public void OnCompleted(Action<object> continuation, object state, short token) => _core.OnCompleted(continuation, state, token);

		public bool MoveNext()
		{
			if (_cancellationToken.IsCancellationRequested)
			{
				_core.TrySetCanceled(_cancellationToken);
				return false;
			}

			if (_isMainThread && _frameCount == Engine.GetProcessFrames())
				return true;

			_core.TrySetResult(AsyncUnit.Default);
			return false;
		}

		private bool TryReturn()
		{
			TaskTracker.RemoveTracking(this);
			_core.Reset();
			_cancellationToken = CancellationToken.None;
			return _pool.TryPush(this);
		}
	}

	private sealed class DelayFramePromise : IGdTaskSource, IPlayerLoopItem, ITaskPoolNode<DelayFramePromise>
	{
		private static TaskPool<DelayFramePromise> _pool;
		private DelayFramePromise _nextNode;
		public ref DelayFramePromise NextNode => ref _nextNode;

		static DelayFramePromise() => TaskPool.RegisterSizeGetter(typeof(DelayFramePromise), () => _pool.Size);

		private DelayFramePromise()
		{ }

		private bool _isMainThread;
		private ulong _initialFrame;
		private int _delayFrameCount;
		private CancellationToken _cancellationToken;

		private int _currentFrameCount;
		private GdTaskCompletionSourceCore<AsyncUnit> _core;

		public static IGdTaskSource Create(int delayFrameCount, PlayerLoopTiming timing, CancellationToken cancellationToken, out short token)
		{
			if (cancellationToken.IsCancellationRequested)
				return AutoResetGdTaskCompletionSource.CreateFromCanceled(cancellationToken, out token);

			if (!_pool.TryPop(out var result))
				result = new DelayFramePromise();

			result._delayFrameCount = delayFrameCount;
			result._cancellationToken = cancellationToken;
			result._isMainThread = GdTaskPlayerLoopAutoload.IsMainThread;

			if (result._isMainThread)
				result._initialFrame = Engine.GetProcessFrames();

			TaskTracker.TrackActiveTask(result, 3);

			GdTaskPlayerLoopAutoload.AddAction(timing, result);

			token = result._core.Version;
			return result;
		}

		public void GetResult(short token)
		{
			try
			{
				_core.GetResult(token);
			}
			finally
			{
				TryReturn();
			}
		}

		public GdTaskStatus GetStatus(short token) => _core.GetStatus(token);

		public GdTaskStatus UnsafeGetStatus() => _core.UnsafeGetStatus();

		public void OnCompleted(Action<object> continuation, object state, short token) => _core.OnCompleted(continuation, state, token);

		public bool MoveNext()
		{
			if (_cancellationToken.IsCancellationRequested)
			{
				_core.TrySetCanceled(_cancellationToken);
				return false;
			}

			if (_currentFrameCount == 0)
			{
				if (_delayFrameCount == 0) // same as Yield
				{
					_core.TrySetResult(AsyncUnit.Default);
					return false;
				}

				// skip in initial frame.
				if (_isMainThread && _initialFrame == Engine.GetProcessFrames())
				{
#if DEBUG
					// force use Realtime.
					if (GdTaskPlayerLoopAutoload.IsMainThread && Engine.IsEditorHint())
					{
						//goto ++currentFrameCount
					}
					else
					{
						return true;
					}
#else
                        return true;
#endif
				}
			}

			if (++_currentFrameCount < _delayFrameCount)
				return true;

			_core.TrySetResult(AsyncUnit.Default);
			return false;
		}

		private bool TryReturn()
		{
			TaskTracker.RemoveTracking(this);
			_core.Reset();
			_currentFrameCount = 0;
			_delayFrameCount = 0;
			_cancellationToken = CancellationToken.None;
			return _pool.TryPush(this);
		}
	}

	private sealed class DelayPromise : IGdTaskSource, IPlayerLoopItem, ITaskPoolNode<DelayPromise>
	{
		private static TaskPool<DelayPromise> _pool;
		private DelayPromise _nextNode;
		public ref DelayPromise NextNode => ref _nextNode;

		static DelayPromise() => TaskPool.RegisterSizeGetter(typeof(DelayPromise), () => _pool.Size);

		private DelayPromise()
		{ }

		private bool _isMainThread;
		private ulong _initialFrame;
		private double _delayTimeSpan;
		private double _elapsed;
		private PlayerLoopTiming _timing;
		private CancellationToken _cancellationToken;

		private GdTaskCompletionSourceCore<object> _core;

		public static IGdTaskSource Create(TimeSpan delayTimeSpan, PlayerLoopTiming timing, CancellationToken cancellationToken, out short token)
		{
			if (cancellationToken.IsCancellationRequested)
				return AutoResetGdTaskCompletionSource.CreateFromCanceled(cancellationToken, out token);

			if (!_pool.TryPop(out var result))
				result = new DelayPromise();

			result._elapsed = 0.0f;
			result._delayTimeSpan = (float)delayTimeSpan.TotalSeconds;
			result._cancellationToken = cancellationToken;
			result._isMainThread = GdTaskPlayerLoopAutoload.IsMainThread;
			result._timing = timing;

			if (result._isMainThread)
				result._initialFrame = Engine.GetProcessFrames();

			TaskTracker.TrackActiveTask(result, 3);
			GdTaskPlayerLoopAutoload.AddAction(timing, result);

			token = result._core.Version;
			return result;
		}

		public void GetResult(short token)
		{
			try
			{
				_core.GetResult(token);
			}
			finally
			{
				TryReturn();
			}
		}

		public GdTaskStatus GetStatus(short token) => _core.GetStatus(token);

		public GdTaskStatus UnsafeGetStatus() => _core.UnsafeGetStatus();

		public void OnCompleted(Action<object> continuation, object state, short token) => _core.OnCompleted(continuation, state, token);

		public bool MoveNext()
		{
			if (_cancellationToken.IsCancellationRequested)
			{
				_core.TrySetCanceled(_cancellationToken);
				return false;
			}

			if (_elapsed == 0.0f)
			{
				if (_isMainThread && _initialFrame == Engine.GetProcessFrames())
				{
					return true;
				}
			}

			_elapsed += _timing is PlayerLoopTiming.Process or PlayerLoopTiming.PauseProcess
				? GdTaskPlayerLoopAutoload.Global.DeltaTime
				: GdTaskPlayerLoopAutoload.Global.PhysicsDeltaTime;

			if (!(_elapsed >= _delayTimeSpan))
				return true;

			_core.TrySetResult(null);
			return false;
		}

		private bool TryReturn()
		{
			TaskTracker.RemoveTracking(this);
			_core.Reset();
			_delayTimeSpan = 0;
			_elapsed = 0;
			_cancellationToken = CancellationToken.None;
			return _pool.TryPush(this);
		}
	}

	private sealed class DelayRealtimePromise : IGdTaskSource, IPlayerLoopItem, ITaskPoolNode<DelayRealtimePromise>
	{
		private static TaskPool<DelayRealtimePromise> _pool;
		private DelayRealtimePromise _nextNode;
		public ref DelayRealtimePromise NextNode => ref _nextNode;

		static DelayRealtimePromise() => TaskPool.RegisterSizeGetter(typeof(DelayRealtimePromise), () => _pool.Size);

		private DelayRealtimePromise()
		{ }

		private long _delayTimeSpanTicks;
		private ValueStopwatch _stopwatch;
		private CancellationToken _cancellationToken;
		private GdTaskCompletionSourceCore<AsyncUnit> _core;

		public static IGdTaskSource Create(TimeSpan delayTimeSpan, PlayerLoopTiming timing, CancellationToken cancellationToken, out short token)
		{
			if (cancellationToken.IsCancellationRequested)
				return AutoResetGdTaskCompletionSource.CreateFromCanceled(cancellationToken, out token);

			if (!_pool.TryPop(out var result))
				result = new DelayRealtimePromise();

			result._stopwatch = ValueStopwatch.StartNew();
			result._delayTimeSpanTicks = delayTimeSpan.Ticks;
			result._cancellationToken = cancellationToken;

			TaskTracker.TrackActiveTask(result, 3);
			GdTaskPlayerLoopAutoload.AddAction(timing, result);

			token = result._core.Version;
			return result;
		}

		public void GetResult(short token)
		{
			try
			{
				_core.GetResult(token);
			}
			finally
			{
				TryReturn();
			}
		}

		public GdTaskStatus GetStatus(short token) => _core.GetStatus(token);

		public GdTaskStatus UnsafeGetStatus() => _core.UnsafeGetStatus();

		public void OnCompleted(Action<object> continuation, object state, short token) => _core.OnCompleted(continuation, state, token);

		public bool MoveNext()
		{
			if (_cancellationToken.IsCancellationRequested)
			{
				_core.TrySetCanceled(_cancellationToken);
				return false;
			}

			if (_stopwatch.IsInvalid)
			{
				_core.TrySetResult(AsyncUnit.Default);
				return false;
			}

			if (_stopwatch.ElapsedTicks < _delayTimeSpanTicks)
				return true;

			_core.TrySetResult(AsyncUnit.Default);
			return false;
		}

		private bool TryReturn()
		{
			TaskTracker.RemoveTracking(this);
			_core.Reset();
			_stopwatch = default;
			_cancellationToken = CancellationToken.None;
			return _pool.TryPush(this);
		}
	}
}