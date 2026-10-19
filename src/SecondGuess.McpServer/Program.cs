using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SecondGuess.Simulation;

// Second Guess MCP server: exposes the what-if simulation engine as tools that any MCP client
// (GitHub Copilot in VS Code, the Second Guess agents, MCP Inspector) can call over stdio.
//
// Usage: dotnet run --project src/SecondGuess.McpServer -- --data <path to data folder>

var builder = Host.CreateApplicationBuilder(args);

// stdout is reserved for MCP JSON-RPC messages, so all logs go to stderr.
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

string dataRoot = builder.Configuration["data"] ?? "data";

builder.Services.AddSingleton(new MatchRepository(dataRoot));
builder.Services.AddSingleton(new CounterfactualStore(dataRoot));
builder.Services.AddSingleton<WhatIfEngine>();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
