using System.Security.Cryptography;
using System.Text;

namespace Storage.Utils;

internal sealed class Signature(UrlBuilder urlBuider, string secretKey, IArrayPool arrayPool)
{

	public const string Iso8601DateTime = "yyyyMMddTHHmmssZ";
	public const string Iso8601Date = "yyyyMMdd";

	private static SortedDictionary<string, string>? _headerSort = [];

	private readonly byte[] _secretKey = Encoding.UTF8.GetBytes($"AWS4{secretKey}");
	private readonly string _scope = $"/{urlBuider.Region}/{urlBuider.Service}/aws4_request\n";


	[SkipLocalsInit]
	public string Calculate(
		HttpRequestMessage request,
		string payloadHash,
		string[] signedHeaders,
		DateTime requestDate)
	{
		var builder = new ValueStringBuilder(stackalloc char[512], arrayPool);

		AppendStringToSign(ref builder, requestDate);
		AppendCanonicalRequestHash(ref builder, request, signedHeaders, payloadHash);

		Span<byte> signature = stackalloc byte[32];
		CreateSigningKey(ref signature, requestDate);

		signature = signature[..Sign(ref signature, signature, builder.AsReadonlySpan())];
		builder.Dispose();

		return HashHelper.ToHex(signature, arrayPool);
	}

	[SkipLocalsInit]
	public string Calculate(string url, DateTime requestDate)
	{
		var builder = new ValueStringBuilder(stackalloc char[512], arrayPool);

		AppendStringToSign(ref builder, requestDate);
		AppendCanonicalRequestHash(ref builder, url);

		Span<byte> signature = stackalloc byte[32];
		CreateSigningKey(ref signature, requestDate);

		signature = signature[..Sign(ref signature, signature, builder.AsReadonlySpan())];
		builder.Dispose();

		return HashHelper.ToHex(signature, arrayPool);
	}

	private void AppendCanonicalHeaders(
		scoped ref ValueStringBuilder builder,
		HttpRequestMessage request,
		string[] signedHeaders)
	{
		var sortedHeaders = Interlocked.Exchange(ref _headerSort, null) ?? [];
		foreach (var requestHeader in request.Headers)
		{
			var header = NormalizeHeader(requestHeader.Key);
			if (signedHeaders.Contains(header))
			{
				sortedHeaders.Add(header, string.Join(' ', requestHeader.Value).Trim());
			}
		}

		var content = request.Content;
		if (content != null)
		{
			foreach (var contentHeader in content.Headers)
			{
				var header = NormalizeHeader(contentHeader.Key);
				if (signedHeaders.Contains(header))
				{
					sortedHeaders.Add(header, string.Join(' ', contentHeader.Value).Trim());
				}
			}
		}

		foreach (var (header, value) in sortedHeaders)
		{
			builder.Append(header);
			builder.Append(':');
			builder.Append(value);
			builder.Append('\n');
		}

		sortedHeaders.Clear();
		Interlocked.Exchange(ref _headerSort, sortedHeaders);
	}




	[SkipLocalsInit]
	private void AppendCanonicalRequestHash(
		scoped ref ValueStringBuilder builder,
		HttpRequestMessage request,
		string[] signedHeaders,
		string payload)
	{
		var canonical = new ValueStringBuilder(stackalloc char[512], arrayPool);
		var uri = request.RequestUri!;

		const char newLine = '\n';

		canonical.Append(request.Method.Method);
		canonical.Append(newLine);
		canonical.Append(uri.AbsolutePath);
		canonical.Append(newLine);

		urlBuider.AppendCanonicalQueryParameters(ref canonical, uri.Query);
		canonical.Append(newLine);

		AppendCanonicalHeaders(ref canonical, request, signedHeaders);
		canonical.Append(newLine);

		var first = true;
		var span = signedHeaders.AsSpan();
		for (var index = 0; index < span.Length; index++)
		{
			var header = span[index];
			if (first)
			{
				first = false;
			}
			else
			{
				canonical.Append(';');
			}

			canonical.Append(header);
		}

		canonical.Append(newLine);
		canonical.Append(payload);

		AppendSha256ToHex(ref builder, canonical.AsReadonlySpan());

		canonical.Dispose();
	}

	[SkipLocalsInit]
	private void AppendCanonicalRequestHash(scoped ref ValueStringBuilder builder, string url)
	{
		var uri = new Uri(url);

		var canonical = new ValueStringBuilder(stackalloc char[256], arrayPool);
		canonical.Append("GET\n"); // canonical request
		canonical.Append(uri.AbsolutePath);
		canonical.Append('\n');
		canonical.Append(uri.Query.AsSpan(1));
		canonical.Append('\n');
		canonical.Append("host:");
		canonical.Append(uri.Host);

		if (!uri.IsDefaultPort)
		{
			canonical.Append(':');
			canonical.Append(uri.Port);
		}

		canonical.Append("\n\n");
		canonical.Append("host\n");
		canonical.Append("UNSIGNED-PAYLOAD");

		AppendSha256ToHex(ref builder, canonical.AsReadonlySpan());

		canonical.Dispose();
	}

	[SkipLocalsInit]
	private void AppendSha256ToHex(ref ValueStringBuilder builder, scoped ReadOnlySpan<char> value)
	{
		var count = Encoding.UTF8.GetByteCount(value);

		var byteBuffer = arrayPool.Rent<byte>(count);

		var encoded = Encoding.UTF8.GetBytes(value, byteBuffer);

		Span<byte> hashBuffer = stackalloc byte[32];
		if (SHA256.TryHashData(byteBuffer.AsSpan(0, encoded), hashBuffer, out var written))
		{
			Span<char> buffer = stackalloc char[2];
			for (var index = 0; index < hashBuffer[..written].Length; index++)
			{
				var element = hashBuffer[..written][index];
				builder.Append(buffer[..StringUtils.FormatX2(ref buffer, element)]);
			}
		}

		arrayPool.Return(byteBuffer);
	}

	[SkipLocalsInit]
	private string NormalizeHeader(string header)
	{
		using var builder = new ValueStringBuilder(stackalloc char[header.Length], arrayPool);
		var culture = CultureInfo.InvariantCulture;

		// ReSharper disable once ForeachCanBePartlyConvertedToQueryUsingAnotherGetEnumerator
		var span = header.AsSpan();
		foreach (var ch in span)
		{
			if (ch is ' ') continue;
			builder.Append(char.ToLower(ch, culture));
		}

		return string.Intern(builder.Flush());
	}

	private int Sign(ref Span<byte> buffer, ReadOnlySpan<byte> key, scoped ReadOnlySpan<char> content)
	{
		var count = Encoding.UTF8.GetByteCount(content);

		var byteBuffer = arrayPool.Rent<byte>(count);

		var encoded = Encoding.UTF8.GetBytes(content, byteBuffer);
		var result = HMACSHA256.TryHashData(key, byteBuffer.AsSpan(0, encoded), buffer, out var written)
			? written
			: -1;

		arrayPool.Return(byteBuffer);

		return result;
	}



	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void AppendStringToSign(ref ValueStringBuilder builder, DateTime requestDate)
	{
		builder.Append("AWS4-HMAC-SHA256\n");
		builder.Append(requestDate, Iso8601DateTime);
		builder.Append("\n");
		builder.Append(requestDate, Iso8601Date);
		builder.Append(_scope);
	}

	[SkipLocalsInit]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void CreateSigningKey(ref Span<byte> buffer, DateTime requestDate)
	{
		Span<char> dateBuffer = stackalloc char[16];

		Sign(ref buffer, _secretKey, dateBuffer[..StringUtils.Format(ref dateBuffer, requestDate, Iso8601Date)]);
		Sign(ref buffer, buffer, urlBuider.Region);
		Sign(ref buffer, buffer, urlBuider.Service);
		Sign(ref buffer, buffer, "aws4_request");
	}
}
