using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.Configuration;
using Minio;

namespace Storage.Benchmark.Utils;

internal static class BenchmarkHelper
{
	public static readonly byte[] StreamBuffer = new byte[2048];

	// ReSharper disable once InconsistentNaming
	public static AmazonS3Client CreateAWSClient(S3BucketSettings settings)
	{
		return new AmazonS3Client(
			new BasicAWSCredentials(settings.AccessKey, settings.SecretKey),
			new AmazonS3Config
			{
				RegionEndpoint = RegionEndpoint.USEast1,
				ServiceURL = settings.Endpoint,
				ForcePathStyle = true, // MUST be true to work correctly with MinIO server
			});
	}

	public static IMinioClient CreateMinioClient(S3BucketSettings settings)
	{
		Uri endpoint = new (settings.Endpoint);

		var builder = new MinioClient();
		
		return builder
			.WithEndpoint(endpoint)
			.WithCredentials(settings.AccessKey, settings.SecretKey)
			
			.Build();
	}

	public static S3BucketClient CreateStoragesClient(S3BucketSettings settings)
	{
		return new S3BucketClient(new HttpClient(), settings);
	}

	public static void EnsureBucketExists(S3BucketClient client, CancellationToken cancellation)
	{
		if (client.IsBucketExists(cancellation).GetAwaiter().GetResult())
		{
			return;
		}

		client.CreateBucket(cancellation).GetAwaiter().GetResult();
	}

	public static void EnsureFileExists(
		IConfiguration config,
		S3BucketClient client,
		string fileName,
		CancellationToken cancellation)
	{
		var fileData = ReadBigFile(config);
		fileData.Seek(0, SeekOrigin.Begin);

		var result = client
			.UploadFile(fileName, "application/pdf", fileData, cancellation)
			.GetAwaiter()
			.GetResult();

		if (!result)
		{
			throw new FileLoadException("File isn't uploaded");
		}
	}

	public static byte[] ReadBigArray(IConfiguration config)
	{
		var filePath = config.GetValue<string>("BigFilePath");

		return !string.IsNullOrEmpty(filePath) && File.Exists(filePath)
			? File.ReadAllBytes(filePath)
			: GetByteArray(123 * 1024 * 1024); // 123 Mb
	}

	public static InputStream ReadBigFile(IConfiguration config)
	{
		return new InputStream(ReadBigArray(config));
	}

	public static IConfiguration ReadConfiguration()
	{
		return new ConfigurationBuilder()
			.AddJsonFile("appsettings.json", false)
			.Build();
	}

	public static async Task<int> ReadStreamMock(Stream input, byte[] buffer, CancellationToken cancellation)
	{
		var result = 0;
		while (await input.ReadAsync(buffer, cancellation) != 0)
		{
			result++;
		}

		await input.DisposeAsync();

		return result;
	}

	public static S3BucketSettings ReadSettings(IConfiguration config)
	{
		var settings = config.GetRequiredSection("S3Storage").Get<S3BucketSettings>();
		if (settings == null || string.IsNullOrEmpty(settings.Endpoint))
		{
			throw new UriFormatException("S3Storage configuration is not found");
		}

		var isContainer = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER");
		if (isContainer != null && bool.TryParse(isContainer, out var value) && value)
		{
			Uri endpointUri = new (settings.Endpoint);

			settings = new S3BucketSettings
			{
				AccessKey = settings.AccessKey,
				Bucket = settings.Bucket,
				Endpoint = $"{endpointUri.Scheme}://host.docker.internal:{endpointUri.Port}",
				Region = settings.Region,
				SecretKey = settings.SecretKey,
				Service = settings.Service,
				UseHttp2 = settings.UseHttp2,
			};
		}

		return settings;
	}

	private static byte[] GetByteArray(int size)
	{
		var random = Random.Shared;
		var bytes = new byte[size];
		for (var i = 0; i < bytes.Length; i++)
		{
			bytes[i] = (byte)random.Next();
		}

		return bytes;
	}
}
