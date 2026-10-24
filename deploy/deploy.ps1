# Deploys Second Guess to Azure Container Apps.
# The image is built by GitHub Actions (.github/workflows/publish-image.yml) and published to ghcr.io.
# Run from the repo root after `az login`:
#   ./deploy/deploy.ps1 -FoundryName <your-foundry-resource> -Image ghcr.io/<github-user>/second-guess:latest
# No keys are used: the app signs in to your model with its own managed identity.

param(
    [Parameter(Mandatory)] [string] $FoundryName,     # the Foundry / Azure OpenAI resource that hosts gpt-4.1-mini
    [Parameter(Mandatory)] [string] $Image,           # e.g. ghcr.io/<github-user>/second-guess:latest (lowercase)
    [string] $ResourceGroup = "rg-blog",
    [string] $AppName       = "second-guess",
    [string] $Deployment    = "gpt-4.1-mini",
    [string] $Location      = "",                     # defaults to the resource group's region
    [string] $Endpoint      = ""                      # defaults to https://<FoundryName>.openai.azure.com/ (same value as your user secret)
)

$ErrorActionPreference = "Stop"

# az is an external program, so PowerShell doesn't stop when it fails. Stop the script ourselves.
function Assert-Ok([string] $step) {
    if ($LASTEXITCODE -ne 0) { Write-Host "Stopped: $step failed. Fix the error above and run the script again." -ForegroundColor Red; exit 1 }
}
if (-not $Location) { $Location = az group show -n $ResourceGroup --query location -o tsv }

Write-Host "1/5  Preparing the Azure CLI and providers..."
az extension add --name containerapp --upgrade --only-show-errors
az provider register --namespace Microsoft.App --wait
az provider register --namespace Microsoft.OperationalInsights --wait
Assert-Ok "Registering providers"

Write-Host "2/5  Creating the app from $Image (takes a few minutes)..."
az containerapp up `
    --name $AppName `
    --resource-group $ResourceGroup `
    --location $Location `
    --image $Image `
    --ingress external `
    --target-port 8080
Assert-Ok "Creating the app"

Write-Host "3/5  Giving the app its own identity..."
$principalId = az containerapp identity assign --name $AppName --resource-group $ResourceGroup `
    --system-assigned --query principalId -o tsv
Assert-Ok "Assigning the identity"

Write-Host "4/5  Allowing that identity to call the model..."
$foundry  = az cognitiveservices account show --name $FoundryName --resource-group $ResourceGroup -o json | ConvertFrom-Json
Assert-Ok "Finding the Foundry resource"
$endpoint = if ($Endpoint) { $Endpoint } else { "https://$FoundryName.openai.azure.com/" }
az role assignment create `
    --assignee-object-id $principalId `
    --assignee-principal-type ServicePrincipal `
    --role "Cognitive Services OpenAI User" `
    --scope $foundry.id --only-show-errors | Out-Null
Assert-Ok "Granting the model role"

Write-Host "5/5  Setting the model settings and scaling..."
# One replica at most: Blazor Server keeps each visitor's session on one instance.
az containerapp update --name $AppName --resource-group $ResourceGroup `
    --set-env-vars "AZURE_OPENAI_ENDPOINT=$endpoint" "AZURE_OPENAI_DEPLOYMENT=$Deployment" `
    --min-replicas 0 --max-replicas 1 --only-show-errors | Out-Null
Assert-Ok "Updating settings"

$fqdn = az containerapp show --name $AppName --resource-group $ResourceGroup --query properties.configuration.ingress.fqdn -o tsv
Write-Host ""
Write-Host "Done:  https://$fqdn"
Write-Host "Check: https://$fqdn/health   (role assignments can take up to 5 minutes to apply)"
