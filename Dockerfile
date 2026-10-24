# Second Guess: web app (studio + live), MCP server and synthetic match data in one image.

# ---- build ------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .

RUN dotnet restore SecondGuess.slnx
RUN dotnet publish src/SecondGuess.Web -c Release -o /out/web --no-restore
RUN dotnet publish src/SecondGuess.McpServer -c Release -o /out/mcp --no-restore

# Generate the synthetic matches during the build. Seeds are fixed, so this is exactly the data used in the demo.
RUN dotnet run --project src/SecondGuess.Generator -c Release -- generate --seed 100 --matches 5 --out /out/data

# ---- run --------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

COPY --from=build /out/web ./
COPY --from=build /out/mcp ./mcp/
# The app writes counterfactual records next to each match, so the data folder belongs to the non-root app user.
COPY --from=build --chown=app:app /out/data ./data/

ENV SecondGuess__RepoRoot=/app \
    SecondGuess__DataRoot=/app/data \
    SecondGuess__McpServerDll=/app/mcp/SecondGuess.McpServer.dll \
    ASPNETCORE_FORWARDEDHEADERS_ENABLED=true

USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "SecondGuess.Web.dll"]
