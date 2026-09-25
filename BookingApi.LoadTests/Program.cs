using System.Net.Http.Json;
using System.Text;
using NBomber.CSharp;
using NBomber.Http.CSharp;

// Get a real token first — log in as a regular test user
using var authClient = new HttpClient();
var loginResponse = await authClient.PostAsJsonAsync("http://localhost:5013/api/auth/login",
    new { username = "Testusername", password = "Test@123" });
var authResult = await loginResponse.Content.ReadFromJsonAsync<AuthResult>(
    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
var token = authResult!.Token;

const int resourceId = 4;
var startTime = DateTime.UtcNow.AddDays(1).ToString("O"); // tomorrow, same instant for every request
var endTime = DateTime.UtcNow.AddDays(1).AddHours(1).ToString("O");

var httpClient = new HttpClient();

var scenario = Scenario.Create("overbook_attempt", async context =>
    {
        var request = Http.CreateRequest("POST", "http://localhost:5013/api/booking")
            .WithHeader("Authorization", $"Bearer {token}")
            .WithBody(new StringContent(
                $$"""{ "resourceId": {{resourceId}}, "startTime": "{{startTime}}", "endTime": "{{endTime}}" }""",
                Encoding.UTF8,
                "application/json"));

        var response = await Http.Send(httpClient, request);
        return response;
    })
    .WithLoadSimulations(                    // <-- plural, this was the bug
        Simulation.Inject(rate: 20, interval: TimeSpan.FromMilliseconds(50), during: TimeSpan.FromSeconds(2))
    );

NBomberRunner
    .RegisterScenarios(scenario)
    .Run();

record AuthResult(string Token, string Username);