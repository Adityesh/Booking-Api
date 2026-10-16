using System.Net;
using System.Text.Json;
using BookingApi.Tests.Fixtures;

namespace BookingApi.Tests.Integration;

// The OpenAPI document is the contract the frontend's TypeScript client is generated from. [ProducesResponseType]
// attributes are not checked against what an action really returns, so a wrong or missing one compiles fine and
// only surfaces as a wrongly typed client. These tests pin the document; BookingEndpointTests pin the real responses.
// WebApplicationFactory runs in the Development environment, where Program.cs maps /openapi/v1.json.
[Collection("Postgres")]
public class OpenApiContractTests(PostgresFixture fixture) : ApiTestBase(fixture)
{
    private const string DocumentRoute = "/openapi/v1.json";

    // Controllers whose responses have been annotated. Add a prefix here as each controller is done; the general rules
    // below then apply to it automatically.
    public static TheoryData<string> AnnotatedPathPrefixes => new() { "/api/Booking" };

    private async Task<JsonElement> GetDocumentAsync()
    {
        using var client = CreateClient();
        var response = await client.GetAsync(DocumentRoute);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = await ReadJsonAsync(response);
        return doc.RootElement.Clone();
    }

    private static IEnumerable<(string Path, string Method, JsonElement Operation)> Operations(JsonElement document) =>
        document.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject()
                .Select(op => (path.Name, op.Name.ToUpperInvariant(), op.Value)));

    private static JsonElement Response(JsonElement document, string method, string path, int status)
    {
        var operation = document.GetProperty("paths").GetProperty(path).GetProperty(method.ToLowerInvariant());
        Assert.True(operation.GetProperty("responses").TryGetProperty(status.ToString(), out var response),
            $"{method} {path} does not document a {status} response");
        return response;
    }

    // Asserts the response has exactly one content type, and returns its schema.
    private static JsonElement OnlySchema(JsonElement response, string expectedContentType)
    {
        Assert.True(response.TryGetProperty("content", out var content), "response has no body in the document");
        var single = Assert.Single(content.EnumerateObject());
        Assert.Equal(expectedContentType, single.Name);
        return single.Value.GetProperty("schema");
    }

    private static void AssertRef(JsonElement schema, string dtoName) =>
        Assert.Equal($"#/components/schemas/{dtoName}", schema.GetProperty("$ref").GetString());

    private static void AssertPlainString(JsonElement response) =>
        Assert.Equal("string", OnlySchema(response, "text/plain").GetProperty("type").GetString());

    // ---------- /api/Booking, response by response ----------

    [Fact]
    public async Task CreateBooking_DocumentsEveryOutcome_WithTheRightBody()
    {
        var doc = await GetDocumentAsync();
        const string path = "/api/Booking";

        AssertRef(OnlySchema(Response(doc, "POST", path, 201), "application/json"), "BookingResponseDto");
        // 202 is a waitlist entry, NOT a booking. A copy-pasted <BookingResponseDto> here once made the contract lie.
        AssertRef(OnlySchema(Response(doc, "POST", path, 202), "application/json"), "WaitlistEntryResponseDto");
        AssertRef(OnlySchema(Response(doc, "POST", path, 400), "application/problem+json"), "ValidationProblemDetails");
        AssertPlainString(Response(doc, "POST", path, 404));
        AssertPlainString(Response(doc, "POST", path, 409));
    }

    [Fact]
    public async Task GetBooking_Documents200Booking_And404Text()
    {
        var doc = await GetDocumentAsync();
        const string path = "/api/Booking/{id}";

        AssertRef(OnlySchema(Response(doc, "GET", path, 200), "application/json"), "BookingResponseDto");
        AssertPlainString(Response(doc, "GET", path, 404));
    }

    [Fact]
    public async Task CancelBooking_Documents204WithoutBody_404Text_And409Text()
    {
        var doc = await GetDocumentAsync();
        const string path = "/api/Booking/{id}/cancel";

        Assert.False(Response(doc, "POST", path, 204).TryGetProperty("content", out _));
        AssertPlainString(Response(doc, "POST", path, 404));
        AssertPlainString(Response(doc, "POST", path, 409));
    }

    [Fact]
    public async Task ResponseDtos_ArePublishedAsSchemas()
    {
        var doc = await GetDocumentAsync();
        var schemas = doc.GetProperty("components").GetProperty("schemas");

        Assert.True(schemas.TryGetProperty("BookingResponseDto", out _));
        Assert.True(schemas.TryGetProperty("WaitlistEntryResponseDto", out _));
    }

    // ---------- general rules for every annotated controller ----------

    [Theory]
    [MemberData(nameof(AnnotatedPathPrefixes))]
    public async Task AnnotatedOperations_DocumentASuccessResponse_WithJsonBodyOr204(string prefix)
    {
        var doc = await GetDocumentAsync();
        var operations = Operations(doc).Where(o => o.Path.StartsWith(prefix, StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(operations);   // a typo in the prefix must not make this test pass vacuously

        foreach (var (path, method, operation) in operations)
        {
            var successes = operation.GetProperty("responses").EnumerateObject()
                .Where(r => r.Name.StartsWith('2')).ToList();
            Assert.True(successes.Count > 0, $"{method} {path} documents no 2xx response");

            foreach (var success in successes)
            {
                var hasBody = success.Value.TryGetProperty("content", out var content);
                if (success.Name == "204")
                {
                    Assert.False(hasBody, $"{method} {path}: a 204 must not declare a body");
                    continue;
                }

                // "200, no body" is what an un-annotated IActionResult action produces: the client would get `unknown`.
                Assert.True(hasBody, $"{method} {path} {success.Name}: success response has no documented body");
                // text/plain or text/json next to application/json means the content type was not pinned.
                Assert.Equal(["application/json"], content.EnumerateObject().Select(c => c.Name).ToArray());
            }
        }
    }

    [Fact]
    public async Task No204Anywhere_DeclaresABody()
    {
        var doc = await GetDocumentAsync();

        foreach (var (path, method, operation) in Operations(doc))
        {
            if (operation.GetProperty("responses").TryGetProperty("204", out var noContent))
                Assert.False(noContent.TryGetProperty("content", out _), $"{method} {path}: 204 declares a body");
        }
    }
}
