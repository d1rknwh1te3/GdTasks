﻿using System.Runtime.CompilerServices;

namespace GdTasks.Internal;

internal static class ArrayPoolUtil
{
	public static RentArray<T> Materialize<T>(IEnumerable<T> source)
	{
		if (source is T[] array)
			return new RentArray<T>(array, array.Length, null);

		var defaultCount = 32;

		if (source is ICollection<T> coll)
		{
			if (coll.Count == 0)
				return new RentArray<T>([], 0, null);

			defaultCount = coll.Count;

			var pool = ArrayPool<T>.Shared;
			var buffer = pool.Rent(defaultCount);

			coll.CopyTo(buffer, 0);

			return new RentArray<T>(buffer, coll.Count, pool);
		}

		if (source is IReadOnlyCollection<T> rcoll) defaultCount = rcoll.Count;

		if (defaultCount == 0)
			return new RentArray<T>([], 0, null);

		{
			var pool = ArrayPool<T>.Shared;

			var index = 0;
			var buffer = pool.Rent(defaultCount);
			foreach (var item in source)
			{
				EnsureCapacity(ref buffer, index, pool);
				buffer[index++] = item;
			}

			return new RentArray<T>(buffer, index, pool);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static void EnsureCapacity<T>(ref T[] array, int index, ArrayPool<T> pool)
	{
		if (array.Length <= index)
			EnsureCapacityCore(ref array, index, pool);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void EnsureCapacityCore<T>(ref T[] array, int index, ArrayPool<T> pool)
	{
		if (array.Length > index)
			return;

		var newSize = array.Length * 2;
		var newArray = pool.Rent((index < newSize) ? newSize : (index * 2));

		Array.Copy(array, 0, newArray, 0, array.Length);

		pool.Return(array, clearArray: !RuntimeHelpersAbstraction.IsWellKnownNoReferenceContainsType<T>());

		array = newArray;
	}
	public struct RentArray<T>(T[] array, int length, ArrayPool<T> pool) : IDisposable
	{
		public readonly T[] Array = array;
		public readonly int Length = length;
		private ArrayPool<T>? _pool = pool;

		public void Dispose() => DisposeManually(!RuntimeHelpersAbstraction.IsWellKnownNoReferenceContainsType<T>());

		public void DisposeManually(bool clearArray)
		{
			if (_pool == null)
				return;

			if (clearArray)
				System.Array.Clear(Array, 0, Length);

			_pool.Return(Array, clearArray: false);
			_pool = null;
		}
	}
}