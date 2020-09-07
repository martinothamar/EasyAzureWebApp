using System.IO;
using Pulumi;
using Pulumi.Azure.AppService;
using Pulumi.Azure.AppService.Inputs;
using Pulumi.Azure.Authorization;
using Pulumi.Azure.Core;
using Pulumi.Azure.KeyVault;
using Pulumi.Azure.Storage;

class WebAppStack : Stack
{
    public WebAppStack()
    {
        var resourceGroup = new ResourceGroup("rg-easy-azure-webapp");

        var clientConfig = Output.Create(GetClientConfig.InvokeAsync());

        var tenantId = clientConfig.Apply(config => config.TenantId);
        var currentPrincipal = clientConfig.Apply(config => config.ObjectId);

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
            SoftDeleteEnabled = true,
        });

        var keyVaultPolicy = new AccessPolicy("key-vault-policy", new AccessPolicyArgs
        {
            KeyVaultId = keyVault.Id,
            TenantId = tenantId,
            ObjectId = currentPrincipal,
            SecretPermissions = new[] { "delete", "get", "list", "set" },
        });

        var codeBlobSecret = new Secret("zip-secret", new SecretArgs
        {
            KeyVaultId = keyVault.Id,
            Value = SharedAccessSignature.SignedBlobReadUrl(codeBlob, storageAccount),
        });

        var codeBlobSecretUrl = Output.All(keyVault.VaultUri, codeBlobSecret.Name, codeBlobSecret.Version)
            .Apply(d => $"{d[0]}secrets/{d[1]}/{d[2]}");

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
            AppSettings = new InputMap<string>
            {
                { "WEBSITE_RUN_FROM_ZIP", codeBlobSecretUrl.Apply(url => $"@Microsoft.KeyVault(SecretUri={url})") },
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
                DependsOn = appService,
            }
        );

        var principalId = appServiceGet.Identity.Apply(id => id.PrincipalId!);

        var policy = new AccessPolicy("app-policy", new AccessPolicyArgs
        {
            KeyVaultId = keyVault.Id,
            TenantId = tenantId,
            ObjectId = principalId,
            SecretPermissions = "get",
        });

        var subscriptionOutput = Output.Create(GetSubscription.InvokeAsync());
        var scope = Output.All(
            subscriptionOutput.Apply(s => s.SubscriptionId),
            resourceGroup.Name,
            storageAccount.Name,
            storageContainer.Name
        ).Apply(s => $"/subscriptions/{s[0]}/resourcegroups/{s[1]}/providers/Microsoft.Storage/storageAccounts/{s[2]}/blobServices/default/containers/{s[3]}");
        
        var codeBlobPermission = new Assignment("read-code-blob", new AssignmentArgs
        {
            PrincipalId = principalId!,
            Scope = scope,
            RoleDefinitionName = "Storage Blob Data Reader",
        },
        new CustomResourceOptions
        {
            DependsOn = new Resource[] { appServiceGet, policy, storageContainer },
        });

        WebAppUrl = appService.DefaultSiteHostname.Apply(url => $"https://{url}");
    }

    [Output]
    public Output<string> WebAppUrl { get; set; }
}
