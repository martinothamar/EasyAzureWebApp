# An easy to deploy Azure webapp

Fullstack C#/.NET (frontend + backend + Infrastructure), Azure-first design.

* Create storage account with a pulumi-state blob container
* Create Azure Key Vault, make sure your Azure user have all permissions (or add individually until pulumi new works)
* Create `.env.sh` in the root folder with the following content:

```sh
#!/bin/bash

export SOLUTION_ROOT_DIRECTORY=$(pwd)
export AZURE_KEYVAULT_AUTH_VIA_CLI=true
export AZURE_STORAGE_ACCOUNT="moistorage" # State storage
export AZURE_STORAGE_KEY="xxxx"           
```

Then in the commandline:

```sh
source .env.sh          # Set environment variables
dotnet publish          # Build and publish the ASP.NET Core Blazor app
cd src/Infrastructure
pulumi stack init dev   # First time init
az login                # Make sure you're logged in to Azure
pulumi up               # Applies updates to Azure
```

Now you can open the `WebAppUrl` output url.

If you make any changes to the ASP.NET Core app, just rerun `pulumi up` and it will update the zip file.

## From scratch

```sh
pulumi login --cloud-url azblob://pulumi-state
pulumi new azure-csharp --secrets-provider="azurekeyvault://moi-key-vault.vault.azure.net/keys/pulumi-secret"
```