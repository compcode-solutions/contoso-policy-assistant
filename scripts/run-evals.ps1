# Runs golden groundedness / ACL evals (Lexical RAG, no API key required).
$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")
dotnet test --filter "FullyQualifiedName~GoldenEvalTests" --verbosity minimal
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "Golden evals passed."
