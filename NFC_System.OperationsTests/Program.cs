using NFC_System;
using System.Net;
using System.Text;
using System.Text.Json;

int passed = 0, failed = 0;
void Assert(bool value) { if (!value) throw new Exception("Assertion failed"); }
async Task Test(string name, Func<Task> test)
{
    try { await test(); passed++; Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failed++; Console.WriteLine($"FAIL {name}: {ex.Message}"); }
}
await Test("All pages including empty intermediate pages are downloaded before returning", async () =>
{
    var handler = new Pages(
        "{\"documents\":[{\"name\":\"A\"}],\"nextPageToken\":\"a+/= &\"}",
        "{\"nextPageToken\":\"last\"}",
        "{\"documents\":[{\"name\":\"B\"}]}");
    using var http = new HttpClient(handler);
    using var result = await CloudDocumentReader.ReadCollectionAsync(http, "https://test.invalid/students?pageSize=1000&key=test");
    Assert(result.RootElement.GetProperty("documents").GetArrayLength() == 2 && handler.Urls.Count == 3);
    Assert(handler.Urls[1].Contains("pageToken=a%2B%2F%3D%20%26"));
    Assert(!handler.Urls[2].Contains("pageToken=a") && handler.Urls[2].Contains("pageToken=last"));
});
await Test("An empty collection returns a usable empty array", async () =>
{
    using var http = new HttpClient(new Pages("{}"));
    using var result = await CloudDocumentReader.ReadCollectionAsync(http, "https://test.invalid/students");
    Assert(result.RootElement.GetProperty("documents").GetArrayLength() == 0);
});
await Test("Repeated page tokens fail instead of looping or returning a partial collection", async () =>
{
    using var http = new HttpClient(new Pages("{\"nextPageToken\":\"again\"}", "{\"nextPageToken\":\"again\"}"));
    bool threw = false;
    try { using var result = await CloudDocumentReader.ReadCollectionAsync(http, "https://test.invalid/students"); }
    catch (InvalidDataException) { threw = true; }
    Assert(threw);
});
foreach (string failure in new[] { "HTTP_FAILURE", "{bad", "{\"documents\":{}}" })
await Test($"Later page failure returns no partial collection: {failure}", async () =>
{
    using var http = new HttpClient(new Pages("{\"documents\":[{\"name\":\"A\"}],\"nextPageToken\":\"next\"}", failure));
    bool threw = false;
    try { using var result = await CloudDocumentReader.ReadCollectionAsync(http, "https://test.invalid/students"); }
    catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidDataException) { threw = true; }
    Assert(threw);
});
await FeatureTests.RunAsync(Test);
await AuditRegressionTests.RunAsync(Test);
if (args.Contains("--mysql")) await DatabaseTests.RunAsync(Test);
else Console.WriteLine("SQL integration tests skipped; use --mysql with the disposable server on port 23306.");
Console.WriteLine($"{passed} passed; {failed} failed.");
return failed == 0 ? 0 : 1;

sealed class Pages(params string[] pages) : HttpMessageHandler
{
    public List<string> Urls { get; } = new();
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Urls.Add(request.RequestUri!.AbsoluteUri);
        string page = pages[Urls.Count - 1];
        return Task.FromResult(new HttpResponseMessage(page == "HTTP_FAILURE" ? HttpStatusCode.Forbidden : HttpStatusCode.OK)
        {
            Content = new StringContent(page, Encoding.UTF8, "application/json")
        });
    }
}
