using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using VsDebugBridge.Contracts;

namespace VsDebugBridge.McpServer.Tests;

public class McpServerIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public McpServerIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    // =================================================================
    // POST /api/debug-state
    // =================================================================

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

        var response = await _client.PostAsJsonAsync("/api/debug-state", state);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PostDebugState_ReturnsOk_ForMinimalState()
    {
        var state = new DebugState { IsInBreakMode = false };

        var response = await _client.PostAsJsonAsync("/api/debug-state", state);

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

        var response1 = await _client.PostAsJsonAsync("/api/debug-state", state1);
        var response2 = await _client.PostAsJsonAsync("/api/debug-state", state2);

        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
    }

    // =================================================================
    // GET /api/health
    // =================================================================

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

    // =================================================================
    // POST /api/debug-state/expression
    // =================================================================

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

        var response = await _client.PostAsJsonAsync("/api/debug-state/expression", expression);

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

        var response = await _client.PostAsJsonAsync("/api/debug-state/expression", expression);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
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

        var response = await _client.PostAsJsonAsync("/api/debug-state/expression", expression);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
