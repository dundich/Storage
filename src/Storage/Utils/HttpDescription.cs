using System.Collections.Frozen;
using System.Text;

namespace Storage.Utils;

internal sealed class HttpDescription(
	IArrayPool arrayPool,
	string accessKey,
	string region,
	string service,
	string[] signedHeaders)
{
	private static readonly FrozenSet<char> ValidUrlCharacters =
		"abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_.~/".ToFrozenSet();


	private readonly string _headerEnd = $"/{region}/{service}/aws4_request, SignedHeaders={string.Join(';', signedHeaders)}, Signature=";
	private readonly string _headerStart = $"AWS4-HMAC-SHA256 Credential={accessKey}/";

	private readonly string _urlMiddle = $"%2F{region}%2F{service}%2Faws4_request";
	private readonly string _urlStart = $"?X-Amz-Algorithm=AWS4-HMAC-SHA256&X-Amz-Credential={accessKey}%2F";

	[SkipLocalsInit]
	public bool AppendEncodedName(scoped ref ValueStringBuilder builder, ReadOnlySpan<char> name)
	{
		var count = Encoding.UTF8.GetByteCount(name);
		var hasEncoded = false;

		var byteBuffer = arrayPool.Rent<byte>(count);

		Span<char> charBuffer = stackalloc char[2];
		Span<char> upperBuffer = stackalloc char[2];

		var validCharacters = ValidUrlCharacters;
		var encoded = Encoding.UTF8.GetBytes(name, byteBuffer);

		try
		{
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
	public string BuildHeader(DateTime now, string signature)
	{
		using var builder = new ValueStringBuilder(stackalloc char[512], arrayPool);

		builder.Append(_headerStart);
		builder.Append(now, Signature.Iso8601Date);
		builder.Append(_headerEnd);
		builder.Append(signature);

		return builder.Flush();
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
}
