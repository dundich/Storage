using Storage.Utils;

namespace Storage;

/// <summary>
/// Transport functions
/// </summary>
public partial class S3BucketClient
{
	[SkipLocalsInit]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private HttpRequestMessage CreateRequest(HttpMethod method, string? fileName = null)
	{
		var url = new ValueStringBuilder(stackalloc char[512], _arrayPool);
		url.Append(_bucket);

		// ReSharper disable once InvertIf
		if (!string.IsNullOrEmpty(fileName))
		{
			url.Append('/');
			_urlBuilder.AppendEncodedName(ref url, fileName);
		}

		return new HttpRequestMessage(method, new Uri(url.Flush(), UriKind.Absolute));
	}

	private Task<HttpResponseMessage> Send(HttpRequestMessage request, string payloadHash, CancellationToken ct)
	{
		if (_disposed)
		{
			Errors.Disposed();
		}

		var now = DateTime.UtcNow; // TODO: !!

		var headers = request.Headers;
		headers.Add("host", _host);
		headers.Add("x-amz-content-sha256", payloadHash);
		headers.Add("x-amz-date", now.ToString(Signature.Iso8601DateTime, CultureInfo.InvariantCulture));

		if (_useHttp2)
		{
			request.Version = HttpVersion.Version20;
		}

		var signature = _signature.Calculate(request, payloadHash, HeadBuilder.S3Headers, now);
		headers.TryAddWithoutValidation("Authorization", _headBuilder.BuildAuthorization(now, signature));

		return _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
	}
}
