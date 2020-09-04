using System.Collections.Generic;
using System.IO;
using Pulumi;
using Pulumi.Azure.AppService;
using Pulumi.Azure.AppService.Inputs;
using Pulumi.Azure.Core;
using Pulumi.Azure.KeyVault;
using Pulumi.Azure.KeyVault.Inputs;
using Pulumi.Azure.Authorization;
using Pulumi.Azure.Storage;

class MyStack : Stack
{
    public MyStack()
    {
        var resourceGroup = new ResourceGroup("rg-easy-azure-webapp");

        var clientConfig = Output.Create(GetClientConfig.InvokeAsync());

        var tenantId = clientConfig.Apply(config => config.TenantId);
        var objectId = clientConfig.Apply(config => config.ObjectId);

        var solutionRoot = System.Environment.GetEnvironmentVariable("SOLUTION_ROOT_DIRECTORY");

        var storageAccount = new Account("storage", new AccountArgs
        {
            ResourceGroupName = resourceGroup.Name,
            AccountReplicationType = "LRS",
            AccountTier = "Standard",
        });

        var storageContainer = new Container("files", new ContainerArgs
        {
            StorageAccountName = storageAccount.Name,
            ContainerAccessType = "private",
        });

        var codeBlob = new Blob("zip", new BlobArgs
        {
            StorageAccountName = storageAccount.Name,
            StorageContainerName = storageContainer.Name,
            Type = "Block",

            Source = new FileArchive(Path.Join(solutionRoot, "src/Services/EasyAzureWebApp/bin/Debug/netcoreapp3.1/publish"))
        });

        var keyVault = new KeyVault("key-vault", new KeyVaultArgs
        {
            ResourceGroupName = resourceGroup.Name,
            SkuName = "standard",
            TenantId = tenantId,
            AccessPolicies = new KeyVaultAccessPolicyArgs
            {
                TenantId = tenantId,
                ObjectId = objectId,
                SecretPermissions = new[] { "delete", "get", "list", "set" }, 
            },
        });

        var codeBlobSecret = new Secret("zip-secret", new SecretArgs
        {
            KeyVaultId = keyVault.Id,
            Value = SharedAccessSignature.SignedBlobReadUrl(codeBlob, storageAccount),
        });
        var codeBlobSecretUrl = $"{keyVault.VaultUri}secrets/${codeBlobSecret.Name}/{codeBlobSecret.Version}";

        var appServicePlan = new Plan("easy-azure-webapp-plan", new PlanArgs
        {
            ResourceGroupName = resourceGroup.Name,
            Kind = "App",
            Sku = new PlanSkuArgs
            {
                Tier = "Basic",
                Size = "B1",
            },
        });

        var appService = new AppService("easy-azure-webapp", new AppServiceArgs
        {
            ResourceGroupName = resourceGroup.Name,
            AppServicePlanId = appServicePlan.Id,
            Identity = new AppServiceIdentityArgs
            {
                Type = "SystemAssigned",
            },
            AppSettings = new Dictionary<string, string>
            {
                { "WEBSITE_RUN_FROM_ZIP", $"@Microsoft.KeyVault(SecretUri={codeBlobSecretUrl})" },
            }
        });

        var appServiceGet = AppService.Get("easy-azure-webapp-get", appService.Id, 
            new AppServiceState
            {
                ResourceGroupName = resourceGroup.Name,
                AppServicePlanId = appServicePlan.Id,
            },
            new CustomResourceOptions
            {
                DependsOn = appService
            }
        );

        var principalId = appServiceGet.Identity.Apply(id => id.PrincipalId!);

        var policy = new AccessPolicy("app-policy", new AccessPolicyArgs
        {
            KeyVaultId = keyVault.Id,
            TenantId = tenantId,
            ObjectId = objectId,
            SecretPermissions = "get",
        });

        var codeBlobPermission = new Assignment("read-code-blob", new AssignmentArgs
        {
            PrincipalId = principalId!,
            Scope = Output.Create(GetSubscription.InvokeAsync()).Apply(s => $"/subscriptions/{s.SubscriptionId}/resourcegroups/{resourceGroup.Name}/providers/Microsoft.Storage/storageAccounts/{storageAccount.Name}/blobServices/default/containers/{storageContainer.Name}"),
            RoleDefinitionName = "Storage Blob Data Reader",
        });

        WebAppUrl = appService.DefaultSiteHostname.Apply(url => $"https://{url}");
    }

    [Output]
    public Output<string> WebAppUrl { get; set; }
}
