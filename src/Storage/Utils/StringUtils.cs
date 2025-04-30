using System.Collections.Frozen;
using System.Text;

namespace Storage.Utils;

internal static class StringUtils
{
	private static readonly FrozenSet<char> ValidUrlCharacters =
		"abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_.~/".ToFrozenSet();

	[ThreadStatic]
	private static StringBuilder? _sharedBuilder;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static StringBuilder Append(this StringBuilder builder, string start, int middle, string end)
	{
		return builder
			.Append(start)
			.Append(middle)
			.Append(end);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static StringBuilder Append(this StringBuilder builder, string start, string middle, string end)
	{
		return builder
			.Append(start)
			.Append(middle)
			.Append(end);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static StringBuilder GetBuilder()
	{
		return Interlocked.Exchange(ref _sharedBuilder, null)
		       ?? new StringBuilder(4096);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static int Format(ref Span<char> buffer, DateTime dateTime, string format)
	{
		return dateTime.TryFormat(buffer, out var written, format, CultureInfo.InvariantCulture)
			? written
			: -1;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static int FormatX2(ref Span<char> buffer, byte value)
	{
		return value.TryFormat(buffer, out var written, "x2", CultureInfo.InvariantCulture)
			? written
			: -1;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static int FormatX2(ref Span<char> buffer, char value)
	{
		return ((int)value).TryFormat(buffer, out var written, "x2", CultureInfo.InvariantCulture)
			? written
			: -1;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static string Flush(this StringBuilder builder)
	{
		var result = builder.ToString();
		builder.Clear();

		Interlocked.Exchange(ref _sharedBuilder, builder);
		return result;
	}



	[SkipLocalsInit]
	public static string UnescapeString(ReadOnlySpan<char> query)
	{
		using var data = new ValueStringBuilder(stackalloc char[query.Length]);
		foreach (var ch in query)
		{
			data.Append(ch is '+' ? ' ' : ch);
		}

		return Uri.UnescapeDataString(data.Flush());
	}

	[SkipLocalsInit]
	public static bool AppendEncodedName(scoped ref ValueStringBuilder builder, ReadOnlySpan<char> name)
	{
		var arrayPool = DefaultArrayPool.Instance;
		var count = Encoding.UTF8.GetByteCount(name);
		var hasEncoded = false;

		var byteBuffer = arrayPool.Rent<byte>(count);

		Span<char> charBuffer = stackalloc char[2];
		Span<char> upperBuffer = stackalloc char[2];

		try
		{
			var encoded = Encoding.UTF8.GetBytes(name, byteBuffer);
			var span = byteBuffer.AsSpan(0, encoded);
			foreach (var element in span)
			{
				var symbol = (char)element;
				if (ValidUrlCharacters.Contains(symbol))
				{
					builder.Append(symbol);
				}
				else
				{
					builder.Append('%');

					StringUtils.FormatX2(ref charBuffer, symbol);
					MemoryExtensions.ToUpperInvariant(charBuffer, upperBuffer);
					builder.Append(upperBuffer);

					hasEncoded = true;
				}
			}

			return hasEncoded;
		}
		finally
		{
			arrayPool.Return(byteBuffer);
		}
	}


	public static void AppendCanonicalQueryParameters(scoped ref ValueStringBuilder builder, string? query)
	{
		if (string.IsNullOrEmpty(query) || query == "?")
		{
			return;
		}

		int scanIndex = query[0] == '?' ? 1 : 0;
		int textLength = query.Length;

		while (scanIndex < textLength)
		{
			int delimiter = query.IndexOf('&', scanIndex);
			if (delimiter == -1)
			{
				delimiter = textLength;
			}

			int equalIndex = query.IndexOf('=', scanIndex);
			if (equalIndex == -1 || equalIndex > delimiter)
			{
				equalIndex = delimiter; // No value, treat as empty
			}

			// Trim whitespace for the name
			while (scanIndex < equalIndex && char.IsWhiteSpace(query[scanIndex]))
			{
				scanIndex++;
			}

			// Extract name
			var name = StringUtils.UnescapeString(query.AsSpan(scanIndex, equalIndex - scanIndex));
			AppendEncodedName(ref builder, name);
			builder.Append('=');

			// Extract value
			if (equalIndex < delimiter)
			{
				var value = StringUtils.UnescapeString(query.AsSpan(equalIndex + 1, delimiter - equalIndex - 1));
				AppendEncodedName(ref builder, value);
			}
			else
			{
				AppendEncodedName(ref builder, string.Empty);
			}

			builder.Append('&');
			scanIndex = delimiter + 1;
		}

		// Remove the last '&' if present
		builder.RemoveLast();
	}

	public static string BuildFileUrl(string bucket, string? fileName = null)
	{
		var url = new ValueStringBuilder(stackalloc char[512]);
		url.Append(bucket);

		// ReSharper disable once InvertIf
		if (!string.IsNullOrEmpty(fileName))
		{
			url.Append('/');
			AppendEncodedName(ref url, fileName);
		}

		return url.Flush();
	}


	[SkipLocalsInit]
	public static string UrlEncodeName(string fileName)
	{
		var builder = new ValueStringBuilder(stackalloc char[fileName.Length]);
		var encoded = AppendEncodedName(ref builder, fileName);

		return encoded
			? builder.Flush()
			: fileName;
	}
}
