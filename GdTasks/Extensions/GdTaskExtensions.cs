﻿namespace GdTasks.Extensions;

public static partial class GdTaskExtensions
{
	/// <summary>
	/// Convert Task[T] -> GDTask[T].
	/// </summary>
	public static GdTask<T> AsGdTask<T>(this Task<T> task, bool useCurrentSynchronizationContext = true)
	{
		var promise = new GdTaskCompletionSource<T>();

		task.ContinueWith((x, state) =>
		{
			if (state == null) // TODO: Check
				return;

			var p = (GdTaskCompletionSource<T>)state;

			switch (x.Status)
			{
				case TaskStatus.Canceled:
					p.TrySetCanceled();
					break;

				case TaskStatus.Faulted:
					p.TrySetException(x.Exception);
					break;

				case TaskStatus.RanToCompletion:
					p.TrySetResult(x.Result);
					break;

				default:
					throw new NotSupportedException();
			}
		}, promise, useCurrentSynchronizationContext ? TaskScheduler.FromCurrentSynchronizationContext() : TaskScheduler.Current);

		return promise.Task;
	}

	/// <summary>
	/// Convert Task -> GdTask.
	/// </summary>
	public static GdTask AsGdTask(this Task task, bool useCurrentSynchronizationContext = true)
	{
		var promise = new GdTaskCompletionSource();

		task.ContinueWith((x, state) =>
		{
			if (state == null) // TODO: Check
				return;

			var p = (GdTaskCompletionSource)state;

			switch (x.Status)
			{
				case TaskStatus.Canceled:
					p.TrySetCanceled();
					break;

				case TaskStatus.Faulted:
					p.TrySetException(x.Exception);
					break;

				case TaskStatus.RanToCompletion:
					p.TrySetResult();
					break;

				default:
					throw new NotSupportedException();
			}
		}, promise, useCurrentSynchronizationContext ? TaskScheduler.FromCurrentSynchronizationContext() : TaskScheduler.Current);

		return promise.Task;
	}

	public static Task<T> AsTask<T>(this GdTask<T> task)
	{
		try
		{
			GdTask<T>.Awaiter awaiter;

			try
			{
				awaiter = task.GetAwaiter();
			}
			catch (Exception ex)
			{
				return Task.FromException<T>(ex);
			}

			if (awaiter.IsCompleted)
			{
				try
				{
					var result = awaiter.GetResult();
					return Task.FromResult(result);
				}
				catch (Exception ex)
				{
					return Task.FromException<T>(ex);
				}
			}

			var tcs = new TaskCompletionSource<T>();

			awaiter.SourceOnCompleted(state =>
			{
				using var tuple = (StateTuple<TaskCompletionSource<T>, GdTask<T>.Awaiter>)state;

				var (inTcs, inAwaiter) = tuple;

				try
				{
					var result = inAwaiter.GetResult();
					inTcs.SetResult(result);
				}
				catch (Exception ex)
				{
					inTcs.SetException(ex);
				}
			}, StateTuple.Create(tcs, awaiter));

			return tcs.Task;
		}
		catch (Exception ex)
		{
			return Task.FromException<T>(ex);
		}
	}

	public static Task AsTask(this GdTask task)
	{
		try
		{
			GdTask.Awaiter awaiter;

			try
			{
				awaiter = task.GetAwaiter();
			}
			catch (Exception ex)
			{
				return Task.FromException(ex);
			}

			if (awaiter.IsCompleted)
			{
				try
				{
					awaiter.GetResult(); // check token valid on Succeeded
					return Task.CompletedTask;
				}
				catch (Exception ex)
				{
					return Task.FromException(ex);
				}
			}

			var tcs = new TaskCompletionSource<object>();

			awaiter.SourceOnCompleted(state =>
			{
				using var tuple = (StateTuple<TaskCompletionSource<object>, GdTask.Awaiter>)state;
				var (inTcs, inAwaiter) = tuple;

				try
				{
					inAwaiter.GetResult();
					inTcs.SetResult(null);
				}
				catch (Exception ex)
				{
					inTcs.SetException(ex);
				}
			}, StateTuple.Create(tcs, awaiter));

			return tcs.Task;
		}
		catch (Exception ex)
		{
			return Task.FromException(ex);
		}
	}

	/// <summary>
	/// Ignore task result when cancel raised first.
	/// </summary>
	public static GdTask AttachExternalCancellation(this GdTask task, CancellationToken cancellationToken)
	{
		if (!cancellationToken.CanBeCanceled)
			return task;

		if (cancellationToken.IsCancellationRequested)
			return GdTask.FromCanceled(cancellationToken);

		return task.Status.IsCompleted()
			? task
			: new GdTask(new AttachExternalCancellationSource(task, cancellationToken), 0);
	}

	/// <summary>
	/// Ignore task result when cancel raised first.
	/// </summary>
	public static GdTask<T> AttachExternalCancellation<T>(this GdTask<T> task, CancellationToken cancellationToken)
	{
		if (!cancellationToken.CanBeCanceled)
			return task;

		if (cancellationToken.IsCancellationRequested)
			return GdTask.FromCanceled<T>(cancellationToken);

		return task.Status.IsCompleted()
			? task
			: new GdTask<T>(new AttachExternalCancellationSource<T>(task, cancellationToken), 0);
	}

	public static async GdTask ContinueWith(this GdTask task, Action continuationFunction)
	{
		await task;
		continuationFunction();
	}

	public static async GdTask ContinueWith(this GdTask task, Func<GdTask> continuationFunction)
	{
		await task;
		await continuationFunction();
	}

	public static async GdTask<T> ContinueWith<T>(this GdTask task, Func<T> continuationFunction)
	{
		await task;
		return continuationFunction();
	}

	public static async GdTask<T> ContinueWith<T>(this GdTask task, Func<GdTask<T>> continuationFunction)
	{
		await task;
		return await continuationFunction();
	}

	public static async GdTask ContinueWith<T>(this GdTask<T> task, Action<T> continuationFunction)
		=> continuationFunction(await task);

	public static async GdTask ContinueWith<T>(this GdTask<T> task, Func<T, GdTask> continuationFunction)
		=> await continuationFunction(await task);

	public static async GdTask<TR> ContinueWith<T, TR>(this GdTask<T> task, Func<T, TR> continuationFunction)
		=> continuationFunction(await task);

	public static async GdTask<TR> ContinueWith<T, TR>(this GdTask<T> task, Func<T, GdTask<TR>> continuationFunction)
		=> await continuationFunction(await task);

	public static void Forget(this GdTask task)
	{
		var awaiter = task.GetAwaiter();
		if (awaiter.IsCompleted)
		{
			try
			{
				awaiter.GetResult();
			}
			catch (Exception ex)
			{
				GdTaskScheduler.PublishUnobservedTaskException(ex);
			}
		}
		else
		{
			awaiter.SourceOnCompleted(state =>
			{
				using var t = (StateTuple<GdTask.Awaiter>)state;
				try
				{
					t.Item1.GetResult();
				}
				catch (Exception ex)
				{
					GdTaskScheduler.PublishUnobservedTaskException(ex);
				}
			}, StateTuple.Create(awaiter));
		}
	}

	public static void Forget(this GdTask task, Action<Exception>? exceptionHandler, bool handleExceptionOnMainThread = true)
	{
		if (exceptionHandler == null)
		{
			Forget(task);
		}
		else
		{
			ForgetCoreWithCatch(task, exceptionHandler, handleExceptionOnMainThread).Forget();
		}
	}

	public static void Forget<T>(this GdTask<T> task)
	{
		var awaiter = task.GetAwaiter();
		if (awaiter.IsCompleted)
		{
			try
			{
				awaiter.GetResult();
			}
			catch (Exception ex)
			{
				GdTaskScheduler.PublishUnobservedTaskException(ex);
			}
		}
		else
		{
			awaiter.SourceOnCompleted(state =>
			{
				using var t = (StateTuple<GdTask<T>.Awaiter>)state;
				try
				{
					t.Item1.GetResult();
				}
				catch (Exception ex)
				{
					GdTaskScheduler.PublishUnobservedTaskException(ex);
				}
			}, StateTuple.Create(awaiter));
		}
	}

	public static void Forget<T>(this GdTask<T> task, Action<Exception>? exceptionHandler, bool handleExceptionOnMainThread = true)
	{
		if (exceptionHandler == null)
		{
			task.Forget();
		}
		else
		{
			ForgetCoreWithCatch(task, exceptionHandler, handleExceptionOnMainThread).Forget();
		}
	}

	public static GdTask.Awaiter GetAwaiter(this GdTask[] tasks)
		=> GdTask.WhenAll(tasks).GetAwaiter();

	public static GdTask.Awaiter GetAwaiter(this IEnumerable<GdTask> tasks)
		=> GdTask.WhenAll(tasks).GetAwaiter();

	public static GdTask<T[]>.Awaiter GetAwaiter<T>(this GdTask<T>[] tasks)
		=> GdTask.WhenAll(tasks).GetAwaiter();

	public static GdTask<T[]>.Awaiter GetAwaiter<T>(this IEnumerable<GdTask<T>> tasks)
		=> GdTask.WhenAll(tasks).GetAwaiter();

	public static GdTask<(T1, T2)>.Awaiter GetAwaiter<T1, T2>(this (GdTask<T1> task1, GdTask<T2> task2) tasks)
		=> GdTask.WhenAll(tasks.Item1, tasks.Item2).GetAwaiter();

	public static GdTask<(T1, T2, T3)>.Awaiter GetAwaiter<T1, T2, T3>(this (GdTask<T1> task1, GdTask<T2> task2, GdTask<T3> task3) tasks)
		=> GdTask.WhenAll(tasks.Item1, tasks.Item2, tasks.Item3).GetAwaiter();

	public static GdTask<(T1, T2, T3, T4)>.Awaiter GetAwaiter<T1, T2, T3, T4>(this (GdTask<T1> task1, GdTask<T2> task2, GdTask<T3> task3, GdTask<T4> task4) tasks)
		=> GdTask.WhenAll(tasks.Item1, tasks.Item2, tasks.Item3, tasks.Item4).GetAwaiter();

	public static GdTask<(T1, T2, T3, T4, T5)>.Awaiter GetAwaiter<T1, T2, T3, T4, T5>(this (GdTask<T1> task1, GdTask<T2> task2, GdTask<T3> task3, GdTask<T4> task4, GdTask<T5> task5) tasks)
		=> GdTask.WhenAll(tasks.Item1, tasks.Item2, tasks.Item3, tasks.Item4, tasks.Item5).GetAwaiter();

	public static GdTask<(T1, T2, T3, T4, T5, T6)>.Awaiter GetAwaiter<T1, T2, T3, T4, T5, T6>(this (GdTask<T1> task1, GdTask<T2> task2, GdTask<T3> task3, GdTask<T4> task4, GdTask<T5> task5, GdTask<T6> task6) tasks)
		=> GdTask.WhenAll(tasks.Item1, tasks.Item2, tasks.Item3, tasks.Item4, tasks.Item5, tasks.Item6).GetAwaiter();

	public static GdTask<(T1, T2, T3, T4, T5, T6, T7)>.Awaiter GetAwaiter<T1, T2, T3, T4, T5, T6, T7>(this (GdTask<T1> task1, GdTask<T2> task2, GdTask<T3> task3, GdTask<T4> task4, GdTask<T5> task5, GdTask<T6> task6, GdTask<T7> task7) tasks)
		=> GdTask.WhenAll(tasks.Item1, tasks.Item2, tasks.Item3, tasks.Item4, tasks.Item5, tasks.Item6, tasks.Item7).GetAwaiter();

	public static GdTask<(T1, T2, T3, T4, T5, T6, T7, T8)>.Awaiter GetAwaiter<T1, T2, T3, T4, T5, T6, T7, T8>(this (GdTask<T1> task1, GdTask<T2> task2, GdTask<T3> task3, GdTask<T4> task4, GdTask<T5> task5, GdTask<T6> task6, GdTask<T7> task7, GdTask<T8> task8) tasks)
		=> GdTask.WhenAll(tasks.Item1, tasks.Item2, tasks.Item3, tasks.Item4, tasks.Item5, tasks.Item6, tasks.Item7, tasks.Item8).GetAwaiter();

	public static GdTask<(T1, T2, T3, T4, T5, T6, T7, T8, T9)>.Awaiter GetAwaiter<T1, T2, T3, T4, T5, T6, T7, T8, T9>(this (GdTask<T1> task1, GdTask<T2> task2, GdTask<T3> task3, GdTask<T4> task4, GdTask<T5> task5, GdTask<T6> task6, GdTask<T7> task7, GdTask<T8> task8, GdTask<T9> task9) tasks)
		=> GdTask.WhenAll(tasks.Item1, tasks.Item2, tasks.Item3, tasks.Item4, tasks.Item5, tasks.Item6, tasks.Item7, tasks.Item8, tasks.Item9).GetAwaiter();

	public static GdTask<(T1, T2, T3, T4, T5, T6, T7, T8, T9, T10)>.Awaiter GetAwaiter<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(this (GdTask<T1> task1, GdTask<T2> task2, GdTask<T3> task3, GdTask<T4> task4, GdTask<T5> task5, GdTask<T6> task6, GdTask<T7> task7, GdTask<T8> task8, GdTask<T9> task9, GdTask<T10> task10) tasks)
		=> GdTask.WhenAll(tasks.Item1, tasks.Item2, tasks.Item3, tasks.Item4, tasks.Item5, tasks.Item6, tasks.Item7, tasks.Item8, tasks.Item9, tasks.Item10).GetAwaiter();

	public static GdTask<(T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11)>.Awaiter GetAwaiter<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(this (GdTask<T1> task1, GdTask<T2> task2, GdTask<T3> task3, GdTask<T4> task4, GdTask<T5> task5, GdTask<T6> task6, GdTask<T7> task7, GdTask<T8> task8, GdTask<T9> task9, GdTask<T10> task10, GdTask<T11> task11) tasks)
		=> GdTask.WhenAll(tasks.Item1, tasks.Item2, tasks.Item3, tasks.Item4, tasks.Item5, tasks.Item6, tasks.Item7, tasks.Item8, tasks.Item9, tasks.Item10, tasks.Item11).GetAwaiter();

	public static GdTask<(T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12)>.Awaiter GetAwaiter<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(this (GdTask<T1> task1, GdTask<T2> task2, GdTask<T3> task3, GdTask<T4> task4, GdTask<T5> task5, GdTask<T6> task6, GdTask<T7> task7, GdTask<T8> task8, GdTask<T9> task9, GdTask<T10> task10, GdTask<T11> task11, GdTask<T12> task12) tasks)
		=> GdTask.WhenAll(tasks.Item1, tasks.Item2, tasks.Item3, tasks.Item4, tasks.Item5, tasks.Item6, tasks.Item7, tasks.Item8, tasks.Item9, tasks.Item10, tasks.Item11, tasks.Item12).GetAwaiter();

	public static GdTask<(T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13)>.Awaiter GetAwaiter<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(this (GdTask<T1> task1, GdTask<T2> task2, GdTask<T3> task3, GdTask<T4> task4, GdTask<T5> task5, GdTask<T6> task6, GdTask<T7> task7, GdTask<T8> task8, GdTask<T9> task9, GdTask<T10> task10, GdTask<T11> task11, GdTask<T12> task12, GdTask<T13> task13) tasks)
		=> GdTask.WhenAll(tasks.Item1, tasks.Item2, tasks.Item3, tasks.Item4, tasks.Item5, tasks.Item6, tasks.Item7, tasks.Item8, tasks.Item9, tasks.Item10, tasks.Item11, tasks.Item12, tasks.Item13).GetAwaiter();

	public static GdTask<(T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14)>.Awaiter GetAwaiter<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(this (GdTask<T1> task1, GdTask<T2> task2, GdTask<T3> task3, GdTask<T4> task4, GdTask<T5> task5, GdTask<T6> task6, GdTask<T7> task7, GdTask<T8> task8, GdTask<T9> task9, GdTask<T10> task10, GdTask<T11> task11, GdTask<T12> task12, GdTask<T13> task13, GdTask<T14> task14) tasks)
		=> GdTask.WhenAll(tasks.Item1, tasks.Item2, tasks.Item3, tasks.Item4, tasks.Item5, tasks.Item6, tasks.Item7, tasks.Item8, tasks.Item9, tasks.Item10, tasks.Item11, tasks.Item12, tasks.Item13, tasks.Item14).GetAwaiter();

	public static GdTask<(T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15)>.Awaiter GetAwaiter<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>(this (GdTask<T1> task1, GdTask<T2> task2, GdTask<T3> task3, GdTask<T4> task4, GdTask<T5> task5, GdTask<T6> task6, GdTask<T7> task7, GdTask<T8> task8, GdTask<T9> task9, GdTask<T10> task10, GdTask<T11> task11, GdTask<T12> task12, GdTask<T13> task13, GdTask<T14> task14, GdTask<T15> task15) tasks)
		=> GdTask.WhenAll(tasks.Item1, tasks.Item2, tasks.Item3, tasks.Item4, tasks.Item5, tasks.Item6, tasks.Item7, tasks.Item8, tasks.Item9, tasks.Item10, tasks.Item11, tasks.Item12, tasks.Item13, tasks.Item14, tasks.Item15).GetAwaiter();

	public static GdTask.Awaiter GetAwaiter(this (GdTask task1, GdTask task2) tasks)
		=> GdTask.WhenAll(tasks.Item1, tasks.Item2).GetAwaiter();

	public static GdTask.Awaiter GetAwaiter(this (GdTask task1, GdTask task2, GdTask task3) tasks)
		=> GdTask.WhenAll(tasks.Item1, tasks.Item2, tasks.Item3).GetAwaiter();

	public static GdTask.Awaiter GetAwaiter(this (GdTask task1, GdTask task2, GdTask task3, GdTask task4) tasks)
		=> GdTask.WhenAll(tasks.Item1, tasks.Item2, tasks.Item3, tasks.Item4).GetAwaiter();

	public static GdTask.Awaiter GetAwaiter(this (GdTask task1, GdTask task2, GdTask task3, GdTask task4, GdTask task5) tasks)
		=> GdTask.WhenAll(tasks.Item1, tasks.Item2, tasks.Item3, tasks.Item4, tasks.Item5).GetAwaiter();

	public static GdTask.Awaiter GetAwaiter(this (GdTask task1, GdTask task2, GdTask task3, GdTask task4, GdTask task5, GdTask task6) tasks)
		=> GdTask.WhenAll(tasks.Item1, tasks.Item2, tasks.Item3, tasks.Item4, tasks.Item5, tasks.Item6).GetAwaiter();

	public static GdTask.Awaiter GetAwaiter(this (GdTask task1, GdTask task2, GdTask task3, GdTask task4, GdTask task5, GdTask task6, GdTask task7) tasks)
		=> GdTask.WhenAll(tasks.Item1, tasks.Item2, tasks.Item3, tasks.Item4, tasks.Item5, tasks.Item6, tasks.Item7).GetAwaiter();

	public static GdTask.Awaiter GetAwaiter(this (GdTask task1, GdTask task2, GdTask task3, GdTask task4, GdTask task5, GdTask task6, GdTask task7, GdTask task8) tasks)
		=> GdTask.WhenAll(tasks.Item1, tasks.Item2, tasks.Item3, tasks.Item4, tasks.Item5, tasks.Item6, tasks.Item7, tasks.Item8).GetAwaiter();

	public static GdTask.Awaiter GetAwaiter(this (GdTask task1, GdTask task2, GdTask task3, GdTask task4, GdTask task5, GdTask task6, GdTask task7, GdTask task8, GdTask task9) tasks)
		=> GdTask.WhenAll(tasks.Item1, tasks.Item2, tasks.Item3, tasks.Item4, tasks.Item5, tasks.Item6, tasks.Item7, tasks.Item8, tasks.Item9).GetAwaiter();

	public static GdTask.Awaiter GetAwaiter(this (GdTask task1, GdTask task2, GdTask task3, GdTask task4, GdTask task5, GdTask task6, GdTask task7, GdTask task8, GdTask task9, GdTask task10) tasks)
		=> GdTask.WhenAll(tasks.Item1, tasks.Item2, tasks.Item3, tasks.Item4, tasks.Item5, tasks.Item6, tasks.Item7, tasks.Item8, tasks.Item9, tasks.Item10).GetAwaiter();

	public static GdTask.Awaiter GetAwaiter(this (GdTask task1, GdTask task2, GdTask task3, GdTask task4, GdTask task5, GdTask task6, GdTask task7, GdTask task8, GdTask task9, GdTask task10, GdTask task11) tasks)
		=> GdTask.WhenAll(tasks.Item1, tasks.Item2, tasks.Item3, tasks.Item4, tasks.Item5, tasks.Item6, tasks.Item7, tasks.Item8, tasks.Item9, tasks.Item10, tasks.Item11).GetAwaiter();

	public static GdTask.Awaiter GetAwaiter(this (GdTask task1, GdTask task2, GdTask task3, GdTask task4, GdTask task5, GdTask task6, GdTask task7, GdTask task8, GdTask task9, GdTask task10, GdTask task11, GdTask task12) tasks)
		=> GdTask.WhenAll(tasks.Item1, tasks.Item2, tasks.Item3, tasks.Item4, tasks.Item5, tasks.Item6, tasks.Item7, tasks.Item8, tasks.Item9, tasks.Item10, tasks.Item11, tasks.Item12).GetAwaiter();

	public static GdTask.Awaiter GetAwaiter(this (GdTask task1, GdTask task2, GdTask task3, GdTask task4, GdTask task5, GdTask task6, GdTask task7, GdTask task8, GdTask task9, GdTask task10, GdTask task11, GdTask task12, GdTask task13) tasks)
		=> GdTask.WhenAll(tasks.Item1, tasks.Item2, tasks.Item3, tasks.Item4, tasks.Item5, tasks.Item6, tasks.Item7, tasks.Item8, tasks.Item9, tasks.Item10, tasks.Item11, tasks.Item12, tasks.Item13).GetAwaiter();

	public static GdTask.Awaiter GetAwaiter(this (GdTask task1, GdTask task2, GdTask task3, GdTask task4, GdTask task5, GdTask task6, GdTask task7, GdTask task8, GdTask task9, GdTask task10, GdTask task11, GdTask task12, GdTask task13, GdTask task14) tasks)
		=> GdTask.WhenAll(tasks.Item1, tasks.Item2, tasks.Item3, tasks.Item4, tasks.Item5, tasks.Item6, tasks.Item7, tasks.Item8, tasks.Item9, tasks.Item10, tasks.Item11, tasks.Item12, tasks.Item13, tasks.Item14).GetAwaiter();

	public static GdTask.Awaiter GetAwaiter(this (GdTask task1, GdTask task2, GdTask task3, GdTask task4, GdTask task5, GdTask task6, GdTask task7, GdTask task8, GdTask task9, GdTask task10, GdTask task11, GdTask task12, GdTask task13, GdTask task14, GdTask task15) tasks)
		=> GdTask.WhenAll(tasks.Item1, tasks.Item2, tasks.Item3, tasks.Item4, tasks.Item5, tasks.Item6, tasks.Item7, tasks.Item8, tasks.Item9, tasks.Item10, tasks.Item11, tasks.Item12, tasks.Item13, tasks.Item14, tasks.Item15).GetAwaiter();

	public static async GdTask Timeout(this GdTask task, TimeSpan timeout, DelayType delayType = DelayType.DeltaTime, PlayerLoopTiming timeoutCheckTiming = PlayerLoopTiming.Process, CancellationTokenSource taskCancellationTokenSource = null)
	{
		var delayCancellationTokenSource = new CancellationTokenSource();
		var timeoutTask = GdTask.Delay(timeout, delayType, timeoutCheckTiming, delayCancellationTokenSource.Token).SuppressCancellationThrow();

		int winArgIndex;
		bool taskResultIsCanceled;
		try
		{
			(winArgIndex, taskResultIsCanceled, _) = await GdTask.WhenAny(task.SuppressCancellationThrow(), timeoutTask);
		}
		catch
		{
			await delayCancellationTokenSource.CancelAsync();
			delayCancellationTokenSource.Dispose();
			throw;
		}

		// timeout
		if (winArgIndex == 1)
		{
			if (taskCancellationTokenSource == null)
				throw new TimeoutException($"Exceed Timeout:{timeout}");

			await taskCancellationTokenSource.CancelAsync();
			taskCancellationTokenSource.Dispose();

			throw new TimeoutException($"Exceed Timeout:{timeout}");
		}

		await delayCancellationTokenSource.CancelAsync();
		delayCancellationTokenSource.Dispose();

		if (taskResultIsCanceled)
		{
			Error.ThrowOperationCanceledException();
		}
	}

	public static async GdTask<T> Timeout<T>(this GdTask<T> task, TimeSpan timeout, DelayType delayType = DelayType.DeltaTime, PlayerLoopTiming timeoutCheckTiming = PlayerLoopTiming.Process, CancellationTokenSource taskCancellationTokenSource = null)
	{
		var delayCancellationTokenSource = new CancellationTokenSource();
		var timeoutTask = GdTask.Delay(timeout, delayType, timeoutCheckTiming, delayCancellationTokenSource.Token).SuppressCancellationThrow();
		int winArgIndex;

		(bool IsCanceled, T Result) taskResult;

		try
		{
			(winArgIndex, taskResult, _) = await GdTask.WhenAny(task.SuppressCancellationThrow(), timeoutTask);
		}
		catch
		{
			await delayCancellationTokenSource.CancelAsync();
			delayCancellationTokenSource.Dispose();
			throw;
		}

		// timeout
		if (winArgIndex == 1)
		{
			if (taskCancellationTokenSource == null)
				throw new TimeoutException($"Exceed Timeout:{timeout}");

			await taskCancellationTokenSource.CancelAsync();
			taskCancellationTokenSource.Dispose();

			throw new TimeoutException($"Exceed Timeout:{timeout}");
		}

		await delayCancellationTokenSource.CancelAsync();
		delayCancellationTokenSource.Dispose();

		if (taskResult.IsCanceled)
			Error.ThrowOperationCanceledException();

		return taskResult.Result;
	}

	/// <summary>
	/// Timeout with suppress OperationCanceledException. Returns (bool, IsCacneled).
	/// </summary>
	public static async GdTask<bool> TimeoutWithoutException(this GdTask task, TimeSpan timeout, DelayType delayType = DelayType.DeltaTime, PlayerLoopTiming timeoutCheckTiming = PlayerLoopTiming.Process, CancellationTokenSource taskCancellationTokenSource = null)
	{
		var delayCancellationTokenSource = new CancellationTokenSource();
		var timeoutTask = GdTask.Delay(timeout, delayType, timeoutCheckTiming, delayCancellationTokenSource.Token).SuppressCancellationThrow();

		int winArgIndex;
		bool taskResultIsCanceled;

		try
		{
			(winArgIndex, taskResultIsCanceled, _) = await GdTask.WhenAny(task.SuppressCancellationThrow(), timeoutTask);
		}
		catch
		{
			await delayCancellationTokenSource.CancelAsync();
			delayCancellationTokenSource.Dispose();
			return true;
		}

		// timeout
		if (winArgIndex == 1)
		{
			if (taskCancellationTokenSource == null)
				return true;

			await taskCancellationTokenSource.CancelAsync();
			taskCancellationTokenSource.Dispose();

			return true;
		}

		await delayCancellationTokenSource.CancelAsync();
		delayCancellationTokenSource.Dispose();

		return taskResultIsCanceled;
	}

	/// <summary>
	/// Timeout with suppress OperationCanceledException. Returns (bool IsTimeout, T Result).
	/// </summary>
	public static async Task<(bool, T? Result)> TimeoutWithoutException<T>(this GdTask<T> task, TimeSpan timeout, DelayType delayType = DelayType.DeltaTime, PlayerLoopTiming timeoutCheckTiming = PlayerLoopTiming.Process, CancellationTokenSource taskCancellationTokenSource = null)
	{
		var delayCancellationTokenSource = new CancellationTokenSource();
		var timeoutTask = GdTask.Delay(timeout, delayType, timeoutCheckTiming, delayCancellationTokenSource.Token).SuppressCancellationThrow();
		int winArgIndex;

		(bool IsCanceled, T Result) taskResult;

		try
		{
			(winArgIndex, taskResult, _) = await GdTask.WhenAny(task.SuppressCancellationThrow(), timeoutTask);
		}
		catch
		{
			await delayCancellationTokenSource.CancelAsync();
			delayCancellationTokenSource.Dispose();
			return (true, default);
		}

		// timeout
		if (winArgIndex == 1)
		{
			if (taskCancellationTokenSource == null)
				return (true, default);

			await taskCancellationTokenSource.CancelAsync();
			taskCancellationTokenSource.Dispose();

			return (true, default);
		}

		await delayCancellationTokenSource.CancelAsync();
		delayCancellationTokenSource.Dispose();

		return taskResult.IsCanceled
			? (true, default)
			: (false, taskResult.Result);
	}

	public static AsyncLazy ToAsyncLazy(this GdTask task) => new(task);

	public static AsyncLazy<T> ToAsyncLazy<T>(this GdTask<T> task) => new(task);
	public static async GdTask<T> Unwrap<T>(this GdTask<GdTask<T>> task)
		=> await await task;

	public static async GdTask Unwrap(this GdTask<GdTask> task)
		=> await await task;

	public static async GdTask<T> Unwrap<T>(this Task<GdTask<T>> task)
		=> await await task;

	public static async GdTask<T> Unwrap<T>(this Task<GdTask<T>> task, bool continueOnCapturedContext)
		=> await await task.ConfigureAwait(continueOnCapturedContext);

	public static async GdTask Unwrap(this Task<GdTask> task)
		=> await await task;

	public static async GdTask Unwrap(this Task<GdTask> task, bool continueOnCapturedContext)
		=> await await task.ConfigureAwait(continueOnCapturedContext);

	public static async GdTask<T> Unwrap<T>(this GdTask<Task<T>> task)
		=> await await task;

	public static async GdTask<T> Unwrap<T>(this GdTask<Task<T>> task, bool continueOnCapturedContext)
		=> await (await task).ConfigureAwait(continueOnCapturedContext);

	public static async GdTask Unwrap(this GdTask<Task> task)
		=> await await task;

	public static async GdTask Unwrap(this GdTask<Task> task, bool continueOnCapturedContext)
		=> await (await task).ConfigureAwait(continueOnCapturedContext);

	private static async GdTaskVoid ForgetCoreWithCatch(GdTask task, Action<Exception> exceptionHandler, bool handleExceptionOnMainThread)
	{
		try
		{
			await task;
		}
		catch (Exception ex)
		{
			try
			{
				if (handleExceptionOnMainThread)
				{
					await GdTask.SwitchToMainThread();
				}
				exceptionHandler(ex);
			}
			catch (Exception ex2)
			{
				GdTaskScheduler.PublishUnobservedTaskException(ex2);
			}
		}
	}

	private static async GdTaskVoid ForgetCoreWithCatch<T>(GdTask<T> task, Action<Exception> exceptionHandler, bool handleExceptionOnMainThread)
	{
		try
		{
			await task;
		}
		catch (Exception ex)
		{
			try
			{
				if (handleExceptionOnMainThread)
					await GdTask.SwitchToMainThread();

				exceptionHandler(ex);
			}
			catch (Exception ex2)
			{
				GdTaskScheduler.PublishUnobservedTaskException(ex2);
			}
		}
	}

	private sealed class AttachExternalCancellationSource : IGdTaskSource
	{
		private static readonly Action<object> CancellationCallbackDelegate = CancellationCallback;

		private CancellationToken _cancellationToken;
		private GdTaskCompletionSourceCore<AsyncUnit> _core;
		private CancellationTokenRegistration _tokenRegistration;
		public AttachExternalCancellationSource(GdTask task, CancellationToken cancellationToken)
		{
			_cancellationToken = cancellationToken;
			_tokenRegistration = cancellationToken.RegisterWithoutCaptureExecutionContext(CancellationCallbackDelegate, this);
			RunTask(task).Forget();
		}

		public void GetResult(short token) => _core.GetResult(token);

		public GdTaskStatus GetStatus(short token) => _core.GetStatus(token);

		public void OnCompleted(Action<object> continuation, object state, short token) => _core.OnCompleted(continuation, state, token);

		public GdTaskStatus UnsafeGetStatus() => _core.UnsafeGetStatus();

		private static void CancellationCallback(object state)
		{
			var self = (AttachExternalCancellationSource)state;
			self._core.TrySetCanceled(self._cancellationToken);
		}

		private async GdTaskVoid RunTask(GdTask task)
		{
			try
			{
				await task;
				_core.TrySetResult(AsyncUnit.Default);
			}
			catch (Exception ex)
			{
				_core.TrySetException(ex);
			}
			finally
			{
				await _tokenRegistration.DisposeAsync();
			}
		}
	}

	private sealed class AttachExternalCancellationSource<T> : IGdTaskSource<T>
	{
		private static readonly Action<object> CancellationCallbackDelegate = CancellationCallback;

		private CancellationToken _cancellationToken;
		private GdTaskCompletionSourceCore<T> _core;
		private CancellationTokenRegistration _tokenRegistration;
		public AttachExternalCancellationSource(GdTask<T> task, CancellationToken cancellationToken)
		{
			_cancellationToken = cancellationToken;
			_tokenRegistration = cancellationToken.RegisterWithoutCaptureExecutionContext(CancellationCallbackDelegate, this);
			RunTask(task).Forget();
		}

		void IGdTaskSource.GetResult(short token) => _core.GetResult(token);

		public T GetResult(short token) => _core.GetResult(token);

		public GdTaskStatus GetStatus(short token) => _core.GetStatus(token);

		public void OnCompleted(Action<object> continuation, object state, short token) => _core.OnCompleted(continuation, state, token);

		public GdTaskStatus UnsafeGetStatus() => _core.UnsafeGetStatus();

		private static void CancellationCallback(object state)
		{
			var self = (AttachExternalCancellationSource<T>)state;
			self._core.TrySetCanceled(self._cancellationToken);
		}

		private async GdTaskVoid RunTask(GdTask<T> task)
		{
			try
			{
				_core.TrySetResult(await task);
			}
			catch (Exception ex)
			{
				_core.TrySetException(ex);
			}
			finally
			{
				await _tokenRegistration.DisposeAsync();
			}
		}
	}
}