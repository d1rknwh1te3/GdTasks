﻿using System.Runtime.CompilerServices;

namespace GdTasks.Internal;

internal static class Error
{
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static Exception ArgumentOutOfRange(string paramName) => new ArgumentOutOfRangeException(paramName);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static Exception MoreThanOneElement() => new InvalidOperationException("Source sequence contains more than one element.");

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static Exception NoElements() => new InvalidOperationException("Source sequence doesn't contain any elements.");

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void ThrowArgumentException<T>(string message) => throw new ArgumentException(message);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void ThrowArgumentNullException<T>(T value, string paramName) where T : class
	{
		if (value == null)
			ThrowArgumentNullExceptionCore(paramName);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void ThrowNotYetCompleted() => throw new InvalidOperationException("Not yet completed.");

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static T ThrowNotYetCompleted<T>() => throw new InvalidOperationException("Not yet completed.");

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void ThrowOperationCanceledException() => throw new OperationCanceledException();

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void ThrowWhenContinuationIsAlreadyRegistered<T>(T continuationField) where T : class
	{
		if (continuationField != null) 
			ThrowInvalidOperationExceptionCore("continuation is already registered.");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ThrowArgumentNullExceptionCore(string paramName) => throw new ArgumentNullException(paramName);

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ThrowInvalidOperationExceptionCore(string message) => throw new InvalidOperationException(message);
}