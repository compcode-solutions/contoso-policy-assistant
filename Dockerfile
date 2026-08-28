# Contoso Policy Assistant API — multi-stage build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/Api/Contoso.PolicyAssistant.Api.csproj src/Api/
RUN dotnet restore src/Api/Contoso.PolicyAssistant.Api.csproj

COPY src/Api/ src/Api/
RUN dotnet publish src/Api/Contoso.PolicyAssistant.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    Policies__RootPath=/app/data/policies \
    Ai__Provider=Lexical \
    Jwt__Issuer=contoso-policy-assistant \
    Jwt__Audience=contoso-policy-assistant-web \
    Jwt__Key=container-dev-only-change-me-signing-key-32+

COPY --from=build /app/publish .
COPY data/policies/ /app/data/policies/
COPY evals/golden.json /app/evals/golden.json

EXPOSE 8080
ENTRYPOINT ["dotnet", "Contoso.PolicyAssistant.Api.dll"]
