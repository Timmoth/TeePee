using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using TeePee.Tests.TestData;
using Xunit;
using Xunit.Abstractions;

namespace TeePee.Tests
{
    /// <summary>
    /// Tests to ensure the Handler is behaving correctly.
    /// </summary>
    public class TeePeeTests
    {
        // URL and Method used for each test
        private string m_Url = "https://www.test.co.uk/api/items";
        private HttpMethod m_HttpMethod = HttpMethod.Get;

        // Instance of Tracking Builder for each test
        private TeePeeBuilder m_TrackingBuilder = new TeePeeBuilder();

        // Logger
        private readonly Mock<ILogger<TeePee>> m_MockLogger;
        
        // Shortcut methods
        private RequestMatchBuilder RequestMatchBuilder() => m_TrackingBuilder.ForRequest(m_Url, m_HttpMethod);
        private HttpRequestMessage RequestMessage() => RequestMessage(m_HttpMethod, m_Url);
        private static HttpRequestMessage RequestMessage(HttpMethod httpMethod, string url) => new HttpRequestMessage(httpMethod, url);
        private Task<HttpResponseMessage> SendRequest() => SendRequest(RequestMessage());
        private async Task<HttpResponseMessage> SendRequest(HttpRequestMessage httpRequestMessage) => await m_TrackingBuilder.Build(m_MockLogger.Object).Manual().CreateClient().SendAsync(httpRequestMessage);

        public TeePeeTests(ITestOutputHelper testOutputHelper)
        {
            m_MockLogger = new Mock<ILogger<TeePee>>();
            m_MockLogger.Setup(l => l.Log(It.IsAny<LogLevel>(), It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception, string>>()))
                        .Callback(new InvocationAction(invocation =>
                                                       {
                                                           var logLevel = (LogLevel)invocation.Arguments[0];               
                                                           var state = invocation.Arguments[2];
                                                           var exception = (Exception)invocation.Arguments[3];
                                                           var formatter = invocation.Arguments[4];
                                                           var invokeMethod = formatter.GetType().GetMethod("Invoke");
                                                           var logMessage = (string)invokeMethod?.Invoke(formatter, new[] { state, exception });
                                                           testOutputHelper.WriteLine($"[{logLevel}] {logMessage}");
                                                       }));
        }

        #region Matches

        [Theory]
        [ClassData(typeof(ContentTypesData))]
        public async Task MatchesBodyWithContentType(string mediaType, Encoding encoding)
        {
            // Given
            var bodyObject = new { Test = 1, Other = new[] { new { Thing = "Yes" }, new { Thing = "No" } } };
            var verify = RequestMatchBuilder().WithBody(bodyObject, mediaType, encoding)
                                              .TrackRequest();

            var httpRequestMessage = RequestMessage();
            httpRequestMessage.Content = new StringContent(JsonSerializer.Serialize(bodyObject), encoding, mediaType);

            // When
            await SendRequest(httpRequestMessage);

            // Then
            verify.WasCalled();
        }

        [Fact]
        public async Task MatchesBodyWithDefaultContentType()
        {
            // Given
            var bodyObject = new { Test = 1, Other = new[] { new { Thing = "Yes" }, new { Thing = "No" } } };
            var verify = RequestMatchBuilder().WithBody(bodyObject)
                                              .TrackRequest();

            var httpRequestMessage = RequestMessage();
            httpRequestMessage.Content = new StringContent(JsonSerializer.Serialize(bodyObject), Encoding.UTF8, "application/json");

            // When
            await SendRequest(httpRequestMessage);

            // Then
            verify.WasCalled();
        }

        [Theory]
        [ClassData(typeof(ContentTypesData))]
        public async Task DoesNotMatchBodyIfMediaTypeDifferent(string mediaType, Encoding encoding)
        {
            // Given
            var bodyObject = new { Test = 1, Other = new[] { new { Thing = "Yes" }, new { Thing = "No" } } };
            var verify = RequestMatchBuilder().WithBody(bodyObject, mediaType, encoding)
                                              .TrackRequest();

            var httpRequestMessage = RequestMessage();
            httpRequestMessage.Content = new StringContent(JsonSerializer.Serialize(bodyObject), encoding, "wrong/mediatype");

            // When
            await SendRequest(httpRequestMessage);

            // Then
            verify.WasNotCalled();
        }

        [Theory]
        [ClassData(typeof(ContentTypesData))]
        public async Task DoesNotMatchBodyIfContentTypeDifferent(string mediaType, Encoding encoding)
        {
            // Given
            var bodyObject = new { Test = 1, Other = new[] { new { Thing = "Yes" }, new { Thing = "No" } } };
            var verify = RequestMatchBuilder().WithBody(bodyObject, mediaType, encoding)
                                              .TrackRequest();

            var httpRequestMessage = RequestMessage();
            httpRequestMessage.Content = new StringContent(JsonSerializer.Serialize(bodyObject), encoding.WebName == Encoding.UTF32.WebName ? Encoding.UTF7 : Encoding.UTF32, mediaType);

            // When
            await SendRequest(httpRequestMessage);

            // Then
            verify.WasNotCalled();
        }
        
        private class ReferenceBodyType 
        {
            public int Test { get; set; }
        }

        [Fact]
        public async Task MatchesBodyIfReferenceTypeAndAlteredAfterAssigning()
        {
            // Given
            var bodyObject = new ReferenceBodyType { Test = 1 };
            var verify = RequestMatchBuilder().WithBody(bodyObject)
                                              .TrackRequest();

            bodyObject.Test = 23;

            var httpRequestMessage = RequestMessage();
            httpRequestMessage.Content = new StringContent(JsonSerializer.Serialize(new { Test = 23 }), Encoding.UTF8, "application/json");

            // When
            await SendRequest(httpRequestMessage);

            // Then
            verify.WasCalled();
        }

        [Theory]
        [ClassData(typeof(CommonHttpMethodsData))]
        public async Task MatchesQueryStringInUrl(HttpMethod httpMethod)
        {
            // Given
            m_Url = "https://www.test.co.uk/api/items?thing=value";
            m_HttpMethod = httpMethod;
            var verify = RequestMatchBuilder().TrackRequest();

            // When
            await SendRequest();

            // Then
            verify.WasCalled();
        }

        [Theory]
        [ClassData(typeof(CommonHttpMethodsData))]
        public async Task MatchesQueryParamsIfAllMatch(HttpMethod httpMethod)
        {
            // Given
            m_HttpMethod = httpMethod;
            var verify = RequestMatchBuilder().ContainingQueryParam("name1", "val1")
                                              .ContainingQueryParam("name2", "val2")
                                              .TrackRequest();

            var httpRequestMessage = RequestMessage(m_HttpMethod, $"{m_Url}?Name1=val1&name2=VAL2&name3=val3");

            // When
            await SendRequest(httpRequestMessage);

            // Then
            verify.WasCalled();
        }

        [Theory]
        [ClassData(typeof(CommonHttpMethodsData))]
        public async Task DoesNotMatchQueryParamsIfNotAllMatched(HttpMethod httpMethod)
        {
            // Given
            m_HttpMethod = httpMethod;
            var verify = RequestMatchBuilder().ContainingQueryParam("name1", "val1")
                                              .ContainingQueryParam("name2", "val2")
                                              .TrackRequest();

            var httpRequestMessage = RequestMessage(m_HttpMethod, $"{m_Url}?Name1=val1&name3=val3");

            // When
            await SendRequest(httpRequestMessage);

            // Then
            verify.WasNotCalled();
        }

        [Theory]
        [ClassData(typeof(CommonHttpMethodsData))]
        public async Task MatchesHeadersIfAllMatch(HttpMethod httpMethod)
        {
            // Given
            m_HttpMethod = httpMethod;
            var verify = RequestMatchBuilder().ContainingHeader("name1", "val1")
                                              .ContainingHeader("name2", "val2")
                                              .TrackRequest();

            var httpRequestMessage = RequestMessage();
            httpRequestMessage.Headers.Add("Name1", "val1");
            httpRequestMessage.Headers.Add("name2", "VAL2");
            httpRequestMessage.Headers.Add("name3", "val3");

            // When
            await SendRequest(httpRequestMessage);

            // Then
            verify.WasCalled();
        }

        [Theory]
        [ClassData(typeof(CommonHttpMethodsData))]
        public async Task DoesNotMatchHeadersIfNotAllMatched(HttpMethod httpMethod)
        {
            // Given
            m_HttpMethod = httpMethod;
            var verify = RequestMatchBuilder().ContainingHeader("name1", "val1")
                                              .TrackRequest();

            var httpRequestMessage = RequestMessage();
            httpRequestMessage.Headers.Add("name2", "val2");

            // When
            await SendRequest(httpRequestMessage);

            // Then
            verify.WasNotCalled();
        }
        
        [Fact]
        public async Task MatchesMoreSpecificRequest()
        {
            // Given
            var bodyObject = new { Test = 1 };
            var verifyUrlOnly = RequestMatchBuilder()
                                    .TrackRequest();
            var verifyUrlAndBody = RequestMatchBuilder().WithBody(bodyObject)
                                        .TrackRequest();

            var httpRequestMessage = RequestMessage();
            httpRequestMessage.Content = new StringContent(JsonSerializer.Serialize(bodyObject), Encoding.UTF8, "application/json");

            // When
            await SendRequest(httpRequestMessage);

            // Then
            verifyUrlOnly.WasNotCalled();
            verifyUrlAndBody.WasCalled();
        }
        
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task LogsMessage(bool isMatch)
        {
            // Given
            RequestMatchBuilder();
            if (!isMatch)
                m_HttpMethod = HttpMethod.Options;

            // When
            await SendRequest(RequestMessage());

            // Then
            m_MockLogger.Verify(l => l.Log(It.Is<LogLevel>(level => level == (isMatch ? LogLevel.Information : LogLevel.Warning)), It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Once);
        }

        #endregion

        #region Responds With

        [Fact]
        public async Task ThrowsIfNoMatchInStrictMode()
        {
            // Given
            m_TrackingBuilder = new TeePeeBuilder(mode: TeePeeMode.Strict);

            // When
            var ex = await Record.ExceptionAsync(async () => await SendRequest());

            // Then
            Assert.NotNull(ex);
            var nex = Assert.IsType<NotSupportedException>(ex);
            Assert.Contains("request was made which did not match any of the TeePee rules.", nex.Message);
        }

        [Theory]
        [ClassData(typeof(CommonHttpMethodsData))]
        public async Task RespondsWithDefaultResponseIfNoMatch(HttpMethod httpMethod)
        {
            // Given
            m_HttpMethod = httpMethod;

            // When
            var response = await SendRequest();

            // Then
            Assert.NotNull(response);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Null(response.Content);
            Assert.Empty(response.Headers);
        }
        
        [Theory]
        [ClassData(typeof(CommonHttpMethodsData))]
        public async Task RespondsWithCustomDefaultResponseIfNoMatch(HttpMethod httpMethod)
        {
            // Given
            m_TrackingBuilder.WithDefaultResponse(HttpStatusCode.BadGateway, "--bad-gateway--");
            m_HttpMethod = httpMethod;

            // When
            var response = await SendRequest();

            // Then
            Assert.NotNull(response);
            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
            Assert.NotNull(response.Content);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Equal("--bad-gateway--", body);
            Assert.Empty(response.Headers);
        }

        [Theory]
        [ClassData(typeof(CommonHttpMethodsData))]
        public async Task RespondsWithDefaultResponseIfNoResponseConfigured(HttpMethod httpMethod)
        {
            // Given
            m_HttpMethod = httpMethod;
            RequestMatchBuilder();

            // When
            var response = await SendRequest();

            // Then
            Assert.NotNull(response);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            Assert.Null(response.Content);
            Assert.Empty(response.Headers);
        }

        [Theory]
        [ClassData(typeof(CommonHttpMethodsData))]
        public async Task ResponseWithDefaultStatusIfResponseConfiguredWithoutStatus(HttpMethod httpMethod)
        {
            // Given
            m_HttpMethod = httpMethod;
            RequestMatchBuilder().Responds();

            // When
            var response = await SendRequest();

            // Then
            Assert.NotNull(response);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        [Theory]
        [ClassData(typeof(CommonHttpMethodsData))]
        public async Task ResponseWithCorrectStatus(HttpMethod httpMethod)
        {
            // Given
            m_HttpMethod = httpMethod;
            RequestMatchBuilder().Responds()
                                 .WithStatus(HttpStatusCode.InternalServerError);

            // When
            var response = await SendRequest();

            // Then
            Assert.NotNull(response);
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        }

        [Theory]
        [ClassData(typeof(CommonHttpMethodsData))]
        public async Task RespondsWithCorrectBodyIfNoContentTypeConfigured(HttpMethod httpMethod)
        {
            // Given
            m_HttpMethod = httpMethod;
            var bodyObject = new { Test = 1, Other = new[] { new { Thing = "Yes" }, new { Thing = "No" } } };
            RequestMatchBuilder().Responds()
                                 .WithBody(bodyObject);

            // When
            var response = await SendRequest();

            // Then
            Assert.NotNull(response);
            var responseBody = await response.Content.ReadAsStringAsync();
            Assert.Equal(JsonSerializer.Serialize(bodyObject), responseBody);
            Assert.Equal("application/json", response.Content.Headers.ContentType.MediaType);
            Assert.Equal("utf-8", response.Content.Headers.ContentType.CharSet);
        }

        [Theory]
        [ClassData(typeof(ContentTypesData))]
        public async Task RespondsWithCorrectBody(string mediaType, Encoding encoding)
        {
            // Given
            var bodyObject = new { Test = 1, Other = new[] { new { Thing = "Yes" }, new { Thing = "No" } }, EnumVal = ToTestJsonSettings.Off };
            RequestMatchBuilder().Responds()
                                 .WithBody(bodyObject, mediaType, encoding);

            // When
            var response = await SendRequest();

            // Then
            Assert.NotNull(response);
            var responseBody = await response.Content.ReadAsStringAsync();
            Assert.Equal(JsonSerializer.Serialize(bodyObject, new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() }}), responseBody);
            Assert.Equal(mediaType, response.Content.Headers.ContentType.MediaType);
            Assert.Equal(encoding.WebName, response.Content.Headers.ContentType.CharSet);
        }
        
        [Fact]
        public async Task RespondsWithCorrectBodyIfReferenceTypeAndAlteredAfterAssigning()
        {
            // Given
            var bodyObject = new ReferenceBodyType { Test = 1 };
            RequestMatchBuilder().Responds()
                                 .WithBody(bodyObject);

            bodyObject.Test = 23;

            // When
            var response = await SendRequest();

            // Then
            Assert.NotNull(response);
            var responseBody = await response.Content.ReadAsStringAsync();
            Assert.Equal(JsonSerializer.Serialize(new { Test = 23 }, new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() }}), responseBody);
        }

        [Theory]
        [ClassData(typeof(CommonHttpMethodsData))]
        public async Task RespondsWithCorrectHeaders(HttpMethod httpMethod)
        {
            // Given
            m_HttpMethod = httpMethod;
            RequestMatchBuilder().Responds()
                                 .WithHeader("Set-Cookie", ".aspnetcookie=123");

            // When
            var response = await SendRequest();

            // Then
            Assert.NotNull(response);
            var (key, values) = Assert.Single(response.Headers);
            Assert.Equal("Set-Cookie", key);
            var headerValue = Assert.Single(values);
            Assert.Equal(".aspnetcookie=123", headerValue);
        }
        [Fact]
        public async Task RespondsWithCorrectBodyWithDefaultJsonSerializerOptions()
        {
            // Given
            var bodyObject = new { Nullable = (string)null, Case = "value", EnumVal = ToTestJsonSettings.Off };
            RequestMatchBuilder().Responds()
                                 .WithBody(bodyObject);

            // When
            var response = await SendRequest();

            // Then
            Assert.NotNull(response);
            var responseBody = await response.Content.ReadAsStringAsync();
            Assert.Equal("{\"Nullable\":null,\"Case\":\"value\",\"EnumVal\":\"Off\"}", responseBody);
        }

        [Fact]
        public async Task RespondsWithCorrectBodyWithCustomJsonSerializerOptions()
        {
            // Given
            var jsonSerializeOptions = new JsonSerializerOptions { IgnoreNullValues = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            m_TrackingBuilder = new TeePeeBuilder(bodySerializeOptions: jsonSerializeOptions);
            var bodyObject = new { Nullable = (string)null, Case = "value", EnumVal = ToTestJsonSettings.Off };
            RequestMatchBuilder().Responds()
                                 .WithBody(bodyObject);

            // When
            var response = await SendRequest();

            // Then
            Assert.NotNull(response);
            var responseBody = await response.Content.ReadAsStringAsync();
            Assert.Equal("{\"case\":\"value\",\"enumVal\":3}", responseBody);
        }

        private enum ToTestJsonSettings
        {
            On = 2,
            Off = 3
        }

        #endregion
    }
}