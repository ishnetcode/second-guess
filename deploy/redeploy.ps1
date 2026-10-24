# Rolls out the newest image after GitHub Actions has published it (check the "Publish image" run is green first).
# Settings, identity and scaling are kept.
param(
    [Parameter(Mandatory)] [string] $Image,           # e.g. ghcr.io/<github-user>/second-guess:latest
    [string] $ResourceGroup = "rg-blog",
    [string] $AppName       = "second-guess"
)
# A new revision suffix makes Azure pull the image again, even though the tag is still "latest".
az containerapp update --name $AppName --resource-group $ResourceGroup --image $Image `
    --revision-suffix ("r" + (Get-Date -Format "MMddHHmm"))
