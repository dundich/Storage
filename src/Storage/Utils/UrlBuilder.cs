using System.Collections.Frozen;
using System.Text;

namespace Storage.Utils;

internal class UrlBuilder(string accessKey, string region, string service, IArrayPool arrayPool)
{

	private static readonly FrozenSet<char> ValidUrlCharacters =
		"abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_.~/".ToFrozenSet();

	private readonly string _urlMiddle = $"%2F{region}%2F{service}%2Faws4_request";
	private readonly string _urlStart = $"?X-Amz-Algorithm=AWS4-HMAC-SHA256&X-Amz-Credential={accessKey}%2F";


	public string AccessKey => accessKey;
	public string Region => region;
	public string Service => service;


	[SkipLocalsInit]
	private string UnescapeString(ReadOnlySpan<char> query)
	{
		using var data = new ValueStringBuilder(stackalloc char[query.Length], arrayPool);
		foreach (var ch in query)
		{
			data.Append(ch is '+' ? ' ' : ch);
		}

		return Uri.UnescapeDataString(data.Flush());
	}

	[SkipLocalsInit]
	public string BuildUrl(string bucket, string fileName, DateTime now, TimeSpan expires)
	{
		var builder = new ValueStringBuilder(stackalloc char[512], arrayPool);

		builder.Append(bucket);
		builder.Append('/');

		AppendEncodedName(ref builder, fileName);

		builder.Append(_urlStart);
		builder.Append(now, Signature.Iso8601Date);
		builder.Append(_urlMiddle);

		builder.Append("&X-Amz-Date=");
		builder.Append(now, Signature.Iso8601DateTime);
		builder.Append("&X-Amz-Expires=");
		builder.Append(expires.TotalSeconds);

		builder.Append("&X-Amz-SignedHeaders=host");

		return builder.Flush();
	}

	[SkipLocalsInit]
	public string EncodeName(string fileName)
	{
		var builder = new ValueStringBuilder(stackalloc char[fileName.Length], arrayPool);
		var encoded = AppendEncodedName(ref builder, fileName);

		return encoded
			? builder.Flush()
			: fileName;
	}

	[SkipLocalsInit]
	public bool AppendEncodedName(scoped ref ValueStringBuilder builder, ReadOnlySpan<char> name)
	{
		var count = Encoding.UTF8.GetByteCount(name);
		var hasEncoded = false;

		var byteBuffer = arrayPool.Rent<byte>(count);

		Span<char> charBuffer = stackalloc char[2];
		Span<char> upperBuffer = stackalloc char[2];

		var validCharacters = ValidUrlCharacters;

		try
		{
			var encoded = Encoding.UTF8.GetBytes(name, byteBuffer);
			var span = byteBuffer.AsSpan(0, encoded);
			foreach (var element in span)
			{
				var symbol = (char)element;
				if (validCharacters.Contains(symbol))
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

	public void AppendCanonicalQueryParameters(scoped ref ValueStringBuilder builder, string? query)
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
			var name = UnescapeString(query.AsSpan(scanIndex, equalIndex - scanIndex));
			AppendEncodedName(ref builder, name);
			builder.Append('=');

			// Extract value
			if (equalIndex < delimiter)
			{
				var value = UnescapeString(query.AsSpan(equalIndex + 1, delimiter - equalIndex - 1));
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
}
