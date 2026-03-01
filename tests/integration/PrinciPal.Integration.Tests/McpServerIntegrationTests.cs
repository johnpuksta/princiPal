using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using PrinciPal.Domain.ValueObjects;
using PrinciPal.Server.Configuration;

namespace PrinciPal.Integration.Tests;

public class PrinciPalWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.Configure<IdleShutdownOptions>(opts =>
            {
                opts.InitialConnectionTimeoutSeconds = 3600;
                opts.GracePeriodSeconds = 3600;
            });
        });
    }
}

public class McpServerIntegrationTests : IClassFixture<PrinciPalWebApplicationFactory>
{
    private readonly HttpClient _client;

    private const string TestSessionId = "a1b2c3d4";

    public McpServerIntegrationTests(PrinciPalWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    #region POST /api/sessions/{sessionId}/debug-state

    [Fact]
    public async Task PostDebugState_ReturnsOk_ForValidState()
    {
        var state = new DebugState
        {
            IsInBreakMode = true,
            CurrentLocation = new SourceLocation
            {
                FilePath = @"C:\src\Program.cs",
                Line = 10,
                Column = 1,
                FunctionName = "Main",
                ProjectName = "TestApp"
            },
            Locals = new List<LocalVariable>
            {
                new() { Name = "args", Type = "string[]", Value = "{string[0]}", IsValidValue = true }
            },
            CallStack = new List<StackFrameInfo>
            {
                new() { Index = 0, FunctionName = "Main", Module = "TestApp.dll", Language = "C#", FilePath = @"C:\src\Program.cs", Line = 10 }
            },
            Breakpoints = new List<BreakpointInfo>
            {
                new() { FilePath = @"C:\src\Program.cs", Line = 10, Enabled = true }
            }
        };

        var response = await _client.PostAsJsonAsync($"/api/sessions/{TestSessionId}/debug-state", state);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PostDebugState_ReturnsOk_ForMinimalState()
    {
        var state = new DebugState { IsInBreakMode = false };

        var response = await _client.PostAsJsonAsync($"/api/sessions/{TestSessionId}/debug-state", state);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PostDebugState_StoresState_ThatCanBePostedAgain()
    {
        // Verify the endpoint can handle sequential updates (simulating the VSIX pushing state repeatedly)
        var state1 = new DebugState
        {
            IsInBreakMode = true,
            CurrentLocation = new SourceLocation { FilePath = "A.cs", Line = 1, FunctionName = "A", ProjectName = "P" }
        };
        var state2 = new DebugState
        {
            IsInBreakMode = true,
            CurrentLocation = new SourceLocation { FilePath = "B.cs", Line = 2, FunctionName = "B", ProjectName = "P" }
        };

        var response1 = await _client.PostAsJsonAsync($"/api/sessions/{TestSessionId}/debug-state", state1);
        var response2 = await _client.PostAsJsonAsync($"/api/sessions/{TestSessionId}/debug-state", state2);

        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
    }

    #endregion

    #region GET /api/health

    [Fact]
    public async Task GetHealth_ReturnsOk_WithRunningStatus()
    {
        var response = await _client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        var status = doc.RootElement.GetProperty("status").GetString();
        Assert.Equal("running", status);
    }

    #endregion

    #region POST /api/sessions/{sessionId}/debug-state/expression

    [Fact]
    public async Task PostExpression_ReturnsOk_ForValidExpressionResult()
    {
        var expression = new ExpressionResult
        {
            Expression = "myList.Count",
            Value = "42",
            Type = "int",
            IsValid = true,
            Members = new List<LocalVariable>()
        };

        var response = await _client.PostAsJsonAsync($"/api/sessions/{TestSessionId}/debug-state/expression", expression);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PostExpression_ReturnsOk_ForExpressionWithMembers()
    {
        var expression = new ExpressionResult
        {
            Expression = "customer",
            Value = "{Customer}",
            Type = "Customer",
            IsValid = true,
            Members = new List<LocalVariable>
            {
                new() { Name = "Id", Type = "int", Value = "1", IsValidValue = true },
                new() { Name = "Name", Type = "string", Value = "\"Alice\"", IsValidValue = true }
            }
        };

        var response = await _client.PostAsJsonAsync($"/api/sessions/{TestSessionId}/debug-state/expression", expression);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    #endregion

    #region GET /api/sessions/{sessionId}/debug-state/history

    [Fact]
    public async Task GetHistory_ReturnsOk_WithEmptyList_WhenNoState()
    {
        var response = await _client.GetAsync($"/api/sessions/EmptySession/debug-state/history");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("[]", content);
    }

    [Fact]
    public async Task GetHistory_ReturnsSnapshots_AfterBreakModeUpdates()
    {
        var sessionId = "HistorySession";
        var state = new DebugState
        {
            IsInBreakMode = true,
            CurrentLocation = new SourceLocation
            {
                FilePath = @"C:\src\Test.cs",
                Line = 10,
                FunctionName = "TestMethod",
                ProjectName = "TestApp"
            }
        };

        await _client.PostAsJsonAsync($"/api/sessions/{sessionId}/debug-state", state);
        var response = await _client.GetAsync($"/api/sessions/{sessionId}/debug-state/history");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        // Response is a JSON array with at least one snapshot containing our function name
        Assert.StartsWith("[{", content);
        Assert.Contains("TestMethod", content);
    }

    #endregion

    #region DELETE /api/sessions/{sessionId}/debug-state/history

    [Fact]
    public async Task DeleteHistory_ReturnsOk_AndClearsHistory()
    {
        var sessionId = "DeleteHistSession";
        await _client.PostAsJsonAsync($"/api/sessions/{sessionId}/debug-state", new DebugState
        {
            IsInBreakMode = true,
            CurrentLocation = new SourceLocation { FilePath = "A.cs", Line = 1, FunctionName = "A", ProjectName = "P" }
        });

        var deleteResponse = await _client.DeleteAsync($"/api/sessions/{sessionId}/debug-state/history");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        var historyResponse = await _client.GetAsync($"/api/sessions/{sessionId}/debug-state/history");
        var content = await historyResponse.Content.ReadAsStringAsync();
        Assert.Contains("[]", content);
    }

    [Fact]
    public async Task PostExpression_ReturnsOk_ForInvalidExpression()
    {
        var expression = new ExpressionResult
        {
            Expression = "badExpr",
            Value = "",
            Type = "",
            IsValid = false
        };

        var response = await _client.PostAsJsonAsync($"/api/sessions/{TestSessionId}/debug-state/expression", expression);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    #endregion

    #region GET /api/sessions + DELETE /api/sessions/{sessionId}

    [Fact]
    public async Task GetSessions_ReturnsEmptyList_Initially()
    {
        // Use a fresh factory to avoid state from other tests
        var response = await _client.GetAsync("/api/sessions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Should be a JSON array
        var content = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("[", content);
    }

    [Fact]
    public async Task SessionLifecycle_RegisterAndDeregister()
    {
        var sessionId = "LifecycleTest";

        // Register session by posting state
        await _client.PostAsJsonAsync($"/api/sessions/{sessionId}/debug-state", new DebugState { IsInBreakMode = false });

        // Verify session appears in list
        var listResponse = await _client.GetAsync("/api/sessions");
        var listContent = await listResponse.Content.ReadAsStringAsync();
        Assert.Contains(sessionId, listContent);

        // Deregister
        var deleteResponse = await _client.DeleteAsync($"/api/sessions/{sessionId}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        // Verify session is gone
        var listResponse2 = await _client.GetAsync("/api/sessions");
        var listContent2 = await listResponse2.Content.ReadAsStringAsync();
        Assert.DoesNotContain(sessionId, listContent2);
    }

    [Fact]
    public async Task PostDebugState_WithNameAndPath_StoresSessionMetadata()
    {
        var sessionId = "meta1234";
        var name = "MyApp";
        var path = @"C:\Repos\MyApp\MyApp.sln";

        await _client.PostAsJsonAsync(
            $"/api/sessions/{sessionId}/debug-state?name={Uri.EscapeDataString(name)}&path={Uri.EscapeDataString(path)}",
            new DebugState { IsInBreakMode = false });

        var listResponse = await _client.GetAsync("/api/sessions");
        var listContent = await listResponse.Content.ReadAsStringAsync();
        Assert.Contains(sessionId, listContent);
        Assert.Contains(name, listContent);
        Assert.Contains(path.Replace(@"\", @"\\"), listContent); // JSON-escaped backslashes

        // Cleanup
        await _client.DeleteAsync($"/api/sessions/{sessionId}");
    }

    [Fact]
    public async Task MultipleSessions_AreIsolated()
    {
        var session1 = "IsoA";
        var session2 = "IsoB";

        var state1 = new DebugState
        {
            IsInBreakMode = true,
            CurrentLocation = new SourceLocation { FilePath = "A.cs", Line = 1, FunctionName = "FuncA", ProjectName = "ProjA" }
        };
        var state2 = new DebugState
        {
            IsInBreakMode = true,
            CurrentLocation = new SourceLocation { FilePath = "B.cs", Line = 2, FunctionName = "FuncB", ProjectName = "ProjB" }
        };

        await _client.PostAsJsonAsync($"/api/sessions/{session1}/debug-state", state1);
        await _client.PostAsJsonAsync($"/api/sessions/{session2}/debug-state", state2);

        var hist1 = await _client.GetAsync($"/api/sessions/{session1}/debug-state/history");
        var content1 = await hist1.Content.ReadAsStringAsync();
        Assert.Contains("FuncA", content1);
        Assert.DoesNotContain("FuncB", content1);

        var hist2 = await _client.GetAsync($"/api/sessions/{session2}/debug-state/history");
        var content2 = await hist2.Content.ReadAsStringAsync();
        Assert.Contains("FuncB", content2);
        Assert.DoesNotContain("FuncA", content2);

        // Cleanup
        await _client.DeleteAsync($"/api/sessions/{session1}");
        await _client.DeleteAsync($"/api/sessions/{session2}");
    }

    #endregion
}
