﻿using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace GdTasks.Internal;

internal static class StatePool<T1>
{
	private static readonly ConcurrentQueue<StateTuple<T1>> Queue = new();

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static StateTuple<T1> Create(T1 item1)
	{
		if (!Queue.TryDequeue(out var value))
			return new StateTuple<T1> { Item1 = item1 };

		value.Item1 = item1;
		return value;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void Return(StateTuple<T1> tuple)
	{
		tuple.Item1 = default;
		Queue.Enqueue(tuple);
	}
}

internal static class StatePool<T1, T2>
{
	private static readonly ConcurrentQueue<StateTuple<T1, T2>> Queue = new();

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static StateTuple<T1, T2> Create(T1 item1, T2 item2)
	{
		if (!Queue.TryDequeue(out var value))
			return new StateTuple<T1, T2> { Item1 = item1, Item2 = item2 };

		value.Item1 = item1;
		value.Item2 = item2;

		return value;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void Return(StateTuple<T1, T2> tuple)
	{
		tuple.Item1 = default;
		tuple.Item2 = default;
		Queue.Enqueue(tuple);
	}
}

internal class StateTuple<T1> : IDisposable
{
	public T1 Item1;

	public void Deconstruct(out T1 item1) => item1 = Item1;

	public void Dispose() => StatePool<T1>.Return(this);
}
internal class StateTuple<T1, T2> : IDisposable
{
	public T1 Item1;
	public T2 Item2;

	public void Deconstruct(out T1 item1, out T2 item2)
	{
		item1 = Item1;
		item2 = Item2;
	}

	public void Dispose() => StatePool<T1, T2>.Return(this);
}
internal class StateTuple<T1, T2, T3> : IDisposable
{
	public T1 Item1;
	public T2 Item2;
	public T3 Item3;

	public void Deconstruct(out T1 item1, out T2 item2, out T3 item3)
	{
		item1 = Item1;
		item2 = Item2;
		item3 = Item3;
	}

	public void Dispose() => StatePool<T1, T2, T3>.Return(this);
}

internal static class StatePool<T1, T2, T3>
{
	private static readonly ConcurrentQueue<StateTuple<T1?, T2?, T3?>> Queue = new();

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static StateTuple<T1, T2, T3> Create(T1 item1, T2 item2, T3 item3)
	{
		if (!Queue.TryDequeue(out var value))
			return new StateTuple<T1, T2, T3> { Item1 = item1, Item2 = item2, Item3 = item3 };

		value.Item1 = item1;
		value.Item2 = item2;
		value.Item3 = item3;

		return value;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void Return(StateTuple<T1?, T2?, T3?> tuple)
	{
		tuple.Item1 = default;
		tuple.Item2 = default;
		tuple.Item3 = default;
		Queue.Enqueue(tuple);
	}
}