# An easy to deploy Azure webapp

Fullstack C#/.NET (frontend + backend + Infrastructure), Azure-first design.

* Create storage account with a pulumi-state blob container
* Create Azure Key Vault, make sure your Azure user have all permissions (or add individually until pulumi new works)
* Create `.env.sh` in the root folder with the following content:

```sh
#!/bin/bash

export SOLUTION_ROOT_DIRECTORY=$(pwd)
export AZURE_KEYVAULT_AUTH_VIA_CLI=true
export AZURE_STORAGE_ACCOUNT="moistorage"
export AZURE_STORAGE_KEY="xxxx"
```

Then in the commandline:

```sh
source .env.sh
cd src/Infrastructure
pulumi login --cloud-url azblob://pulumi-state
pulumi new azure-csharp --secrets-provider="azurekeyvault://moi-key-vault.vault.azure.net/keys/pulumi-secret"
```