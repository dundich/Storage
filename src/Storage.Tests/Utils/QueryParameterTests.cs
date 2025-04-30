//using Storage.Utils;

//namespace Storage.Tests.Utils;

//public class QueryParameterTests
//{
//	public UrlBuilder appender = new QueryParameterAppender(new HttpDescription())



//	[Fact]
//	public void Test_EmptyQuery()
//	{
//		var appender = new QueryParameterAppender();
//		var builder = new ValueStringBuilder();

//		appender.AppendCanonicalQueryParameters1(ref builder, null);
//		Assert.Empty(builder.ToString());

//		appender.AppendCanonicalQueryParameters1(ref builder, "");
//		Assert.Empty(builder.ToString());

//		appender.AppendCanonicalQueryParameters1(ref builder, "?");
//		Assert.Empty(builder.ToString());
//	}

//	[Fact]
//	public void Test_SingleParameter()
//	{
//		var appender = new QueryParameterAppender();
//		var builder = new ValueStringBuilder();

//		appender.AppendCanonicalQueryParameters1(ref builder, "?key=value");
//		Assert.Equal("key=value", builder.ToString());
//	}

//	[Fact]
//	public void Test_MultipleParameters()
//	{
//		var appender = new QueryParameterAppender();
//		var builder = new ValueStringBuilder();

//		appender.AppendCanonicalQueryParameters1(ref builder, "?key1=value1&key2=value2");
//		Assert.Equal("key1=value1&key2=value2", builder.ToString());
//	}

//	[Fact]
//	public void Test_ParameterWithWhitespace()
//	{
//		var appender = new QueryParameterAppender();
//		var builder = new ValueStringBuilder();

//		appender.AppendCanonicalQueryParameters1(ref builder, "? key1 = value1 & key2 = value2 ");
//		Assert.Equal("key1=value1&key2=value2", builder.ToString());
//	}

//	[Fact]
//	public void Test_ParameterWithoutValue()
//	{
//		var appender = new QueryParameterAppender();
//		var builder = new ValueStringBuilder();

//		appender.AppendCanonicalQueryParameters1(ref builder, "?key1&key2=value2");
//		Assert.Equal("key1=&key2=value2", builder.ToString());
//	}

//	[Fact]
//	public void Test_ParameterWithEmptyValue()
//	{
//		var appender = new QueryParameterAppender();
//		var builder = new ValueStringBuilder();

//		appender.AppendCanonicalQueryParameters1(ref builder, "?key1=&key2=");
//		Assert.Equal("key1=&key2=", builder.ToString());
//	}

//	[Fact]
//	public void Test_ParameterWithSpecialCharacters()
//	{
//		var appender = new QueryParameterAppender();
//		var builder = new ValueStringBuilder();

//		appender.AppendCanonicalQueryParameters1(ref builder, "?key1=value%20with%20spaces&key2=value%26with%26ampersands");
//		Assert.Equal("key1=value with spaces&key2=value&with&ampersands", builder.ToString());
//	}
//}
