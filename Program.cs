// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager.Resources.Models;
using Azure.ResourceManager.Samples.Common;
using Azure.ResourceManager.Resources;
using Azure.ResourceManager;
using Azure.ResourceManager.Network;
using Azure.ResourceManager.Network.Models;
using Azure.ResourceManager.Storage.Models;
using Azure.ResourceManager.Storage;
using Microsoft.Identity.Client.Extensions.Msal;
using Azure.ResourceManager.Compute.Models;
using Azure.ResourceManager.Compute;

namespace ManageVpnGatewayVNet2VNetConnection
{
    public class Program
    {
        private static ResourceIdentifier? _resourceGroupId = null;

        /**
         * Azure Network sample for managing virtual network gateway.
         *  - Create 2 virtual networks with subnets and 2 virtual network gateways corresponding to each network
         *  - Create VPN VNet-to-VNet connection
         *  - Troubleshoot the connection
         *    - Create network watcher in the same region as virtual network gateway
         *    - Create storage account to store troubleshooting information
         *    - Run troubleshooting for the connection - result will be 'UnHealthy' as need to create symmetrical connection from second gateway to the first
         *  - Create virtual network connection from second gateway to the first and run troubleshooting. Result will be 'Healthy'.
         *  - List VPN Gateway connections for the first gateway
         *  - Create 2 virtual machines, each one in its network and verify connectivity between them
         */
        public static async Task RunSample(ArmClient client)
        {
            string rgName = Utilities.CreateRandomName("NetworkSampleRG");
            string vnetName1 = Utilities.CreateRandomName("vnet1-");
            string vnetName2 = Utilities.CreateRandomName("vnet2-");
            string vnetGatewayName1 = Utilities.CreateRandomName("vnetGateway1-");
            string vnetGatewayName2 = Utilities.CreateRandomName("vnetGateway2-");
            string pipName1 = Utilities.CreateRandomName("pip1-");
            string pipName2 = Utilities.CreateRandomName("pip2-");
            string networkWatcherName = Utilities.CreateRandomName("watcher");
            string storageAccountName = Utilities.CreateRandomName("azstorageaccoun");
            string containerName = Utilities.CreateRandomName("container");
            string vmName1 = Utilities.CreateRandomName("vm1-");
            string vmName2 = Utilities.CreateRandomName("vm2-");
            string vpnConnectionName1 = "vnet1-to-vnet2-connection";
            string vpnConnectionName2 = "vnet2-to-vnet1-connection";

            try
            {
                // Get default subscription
                SubscriptionResource subscription = await client.GetDefaultSubscriptionAsync();

                // Create a resource group in the EastUS region
                Utilities.Log($"Creating resource group...");
                ArmOperation<ResourceGroupResource> rgLro = await subscription.GetResourceGroups().CreateOrUpdateAsync(WaitUntil.Completed, rgName, new ResourceGroupData(AzureLocation.EastUS));
                ResourceGroupResource resourceGroup = rgLro.Value;
                _resourceGroupId = resourceGroup.Id;
                Utilities.Log("Created a resource group with name: " + resourceGroup.Data.Name);

                //============================================================
                // Create virtual network
                VirtualNetworkData vnetInput1 = new VirtualNetworkData()
                {
                    Location = resourceGroup.Data.Location,
                    AddressPrefixes = { "10.11.0.0/16" },
                    Subnets =
                    {
                        new SubnetData() { AddressPrefix = "10.11.255.0/27", Name = "GatewaySubnet" },
                        new SubnetData() { AddressPrefix = "10.11.0.0/24", Name = "Subnet1" }
                    },
                };
                var vnetLro1 = await resourceGroup.GetVirtualNetworks().CreateOrUpdateAsync(WaitUntil.Completed, vnetName1, vnetInput1);
                VirtualNetworkResource vnet1 = vnetLro1.Value;
                Utilities.Log($"Created a virtual network: {vnet1.Data.Name}");

                //============================================================
                // Create virtual network gateway
                Utilities.Log("Creating virtual network gateway...");

                // Create two public ip for virtual network gateway
                var pip1 = await Utilities.CreatePublicIP(resourceGroup, pipName1);
                var pip2 = await Utilities.CreatePublicIP(resourceGroup, pipName2);

                VirtualNetworkGatewayData vpnGatewayInput1 = new VirtualNetworkGatewayData()
                {
                    Location = resourceGroup.Data.Location,
                    Sku = new VirtualNetworkGatewaySku()
                    {
                        Name = VirtualNetworkGatewaySkuName.Basic,
                        Tier = VirtualNetworkGatewaySkuTier.Basic
                    },
                    Tags = { { "key", "value" } },
                    EnableBgp = false,
                    GatewayType = VirtualNetworkGatewayType.Vpn,
                    VpnType = VpnType.RouteBased,
                    IPConfigurations =
                    {
                        new VirtualNetworkGatewayIPConfiguration()
                        {
                            Name = Utilities.CreateRandomName("config"),
                            PrivateIPAllocationMethod = NetworkIPAllocationMethod.Dynamic,
                            PublicIPAddressId  = pip1.Data.Id,
                            SubnetId = vnet1.Data.Subnets.First(item => item.Name == "GatewaySubnet").Id,
                        }
                    }
                };
                var vpnGatewayLro1 = await resourceGroup.GetVirtualNetworkGateways().CreateOrUpdateAsync(WaitUntil.Completed, vnetGatewayName1, vpnGatewayInput1);
                VirtualNetworkGatewayResource vpnGateway1 = vpnGatewayLro1.Value;
                Utilities.Log($"Created virtual network gateway: {vpnGateway1.Data.Name}");

                //============================================================
                // Create second virtual network
                VirtualNetworkData vnetInput2 = new VirtualNetworkData()
                {
                    Location = resourceGroup.Data.Location,
                    AddressPrefixes = { "10.41.0.0/16" },
                    Subnets =
                    {
                        new SubnetData() { AddressPrefix = "10.41.255.0/27", Name = "GatewaySubnet" },
                        new SubnetData() { AddressPrefix = "10.41.0.0/24", Name = "Subnet2" }
                    },
                };
                var vnetLro2 = await resourceGroup.GetVirtualNetworks().CreateOrUpdateAsync(WaitUntil.Completed, vnetName2, vnetInput2);
                VirtualNetworkResource vnet2 = vnetLro2.Value;
                Utilities.Log($"Created a virtual network: {vnet2.Data.Name}");

                //============================================================
                // Create second virtual network gateway
                Utilities.Log("Creating second virtual network gateway...");
                VirtualNetworkGatewayData vpnGatewayInput2 = new VirtualNetworkGatewayData()
                {
                    Location = resourceGroup.Data.Location,
                    Sku = new VirtualNetworkGatewaySku()
                    {
                        Name = VirtualNetworkGatewaySkuName.Basic,
                        Tier = VirtualNetworkGatewaySkuTier.Basic
                    },
                    Tags = { { "key", "value" } },
                    EnableBgp = false,
                    GatewayType = VirtualNetworkGatewayType.Vpn,
                    VpnType = VpnType.RouteBased,
                    IPConfigurations =
                    {
                        new VirtualNetworkGatewayIPConfiguration()
                        {
                            Name = Utilities.CreateRandomName("config"),
                            PrivateIPAllocationMethod = NetworkIPAllocationMethod.Dynamic,
                            PublicIPAddressId  = pip2.Data.Id,
                            SubnetId = vnet2.Data.Subnets.First(item => item.Name == "GatewaySubnet").Id,
                        }
                    }
                };
                var vpnGatewayLro2 = await resourceGroup.GetVirtualNetworkGateways().CreateOrUpdateAsync(WaitUntil.Completed, vnetGatewayName2, vpnGatewayInput2);
                VirtualNetworkGatewayResource vpnGateway2 = vpnGatewayLro2.Value;
                Utilities.Log($"Created second virtual network gateway: {vnet2.Data.Name}");

                //============================================================
                // Create virtual network gateway connection

                Utilities.Log("Creating virtual network gateway connection...");
                VirtualNetworkGatewayConnectionType connectionType = VirtualNetworkGatewayConnectionType.Vnet2Vnet;
                VirtualNetworkGatewayConnectionData gatewayConnectionInput = new VirtualNetworkGatewayConnectionData(vpnGateway1.Data, connectionType)
                {
                    Location = resourceGroup.Data.Location,
                    VirtualNetworkGateway2 = vpnGateway2.Data,
                    SharedKey = "MySecretKey"
                };
                var connectionLro = await resourceGroup.GetVirtualNetworkGatewayConnections().CreateOrUpdateAsync(WaitUntil.Completed, vpnConnectionName1, gatewayConnectionInput);
                VirtualNetworkGatewayConnectionResource connection = connectionLro.Value;
                Utilities.Log($"Created virtual network gateway connection: {connection.Data.Name}");

                //============================================================
                // Troubleshoot the connection

                // create Network Watcher
                NetworkWatcherData networkWatcherInput = new NetworkWatcherData()
                {
                    Location = resourceGroup.Data.Location,
                };
                //var networkWatcherLro = await resourceGroup.GetNetworkWatchers().CreateOrUpdateAsync(WaitUntil.Completed, networkWatcherName, networkWatcherInput);
                //NetworkWatcherResource networkWatcher = networkWatcherLro.Value;
                var watcherRG = await subscription.GetResourceGroups().GetAsync("NetworkWatcherRG");
                var networkWatcherLro = await watcherRG.Value.GetNetworkWatchers().GetAsync("NetworkWatcher_eastus");
                NetworkWatcherResource networkWatcher = networkWatcherLro.Value;

                //Create storage account to store troubleshooting information
                StorageSku storageSku = new StorageSku(StorageSkuName.StandardGrs);
                StorageKind storageKind = StorageKind.Storage;
                StorageAccountCreateOrUpdateContent storagedata = new StorageAccountCreateOrUpdateContent(storageSku, storageKind, resourceGroup.Data.Location) { };
                var storageAccountLro = await resourceGroup.GetStorageAccounts().CreateOrUpdateAsync(WaitUntil.Completed, storageAccountName, storagedata);
                StorageAccountResource storageAccount = storageAccountLro.Value;

                // Create storage container to store troubleshooting results 
                BlobContainerData containerInput = new BlobContainerData() { };
                var blobContainerLro = await storageAccount.GetBlobService().GetBlobContainers().CreateOrUpdateAsync(WaitUntil.Completed, containerName, containerInput);
                BlobContainerResource container = blobContainerLro.Value;

                // Run troubleshooting for the connection - result will be 'UnHealthy' as need to create symmetrical connection from second gateway to the first
                TroubleshootingContent troubleshootingContent = new TroubleshootingContent(
                    targetResourceId: connection.Id,
                    storageId: storageAccount.Id,
                    storageUri: new Uri($"https://{storageAccount.Data.Name}.blob.core.windows.net/{container.Data.Name}"));

                var troubleshootingResult = await networkWatcher.GetTroubleshootingAsync(WaitUntil.Completed, troubleshootingContent);
                Utilities.Log("Troubleshooting status is: " + troubleshootingResult.Value.Code);

                //============================================================
                //  Create virtual network connection from second gateway to the first and run troubleshooting. Result will be 'Healthy'.
                var gatewayConnectionInput2 = new VirtualNetworkGatewayConnectionData(vpnGateway2.Data, connectionType)
                {
                    Location = resourceGroup.Data.Location,
                    VirtualNetworkGateway2 = vpnGateway1.Data,
                    SharedKey = "MySecretKey"
                };
                _ = await resourceGroup.GetVirtualNetworkGatewayConnections().CreateOrUpdateAsync(WaitUntil.Completed, vpnConnectionName2, gatewayConnectionInput2);
                Utilities.Log("VNet2 to VNet1 gateway connection created");

                // Delay before running troubleshooting to wait for connection settings to propagate
                Thread.Sleep(250000);

                troubleshootingResult = await networkWatcher.GetTroubleshootingAsync(WaitUntil.Completed, troubleshootingContent);
                Utilities.Log("Troubleshooting status is: " + troubleshootingResult.Value.Code);

                //============================================================
                // List VPN Gateway connections for particular gateway
                //var connections = vngw1.ListConnections();
                await foreach (var conn in vpnGateway1.GetConnectionsAsync())
                {
                    Utilities.Log(conn.Name);
                }

                //============================================================
                // Create 2 virtual machines, each one in its network and verify connectivity between them

                // Definate vm extension input data
                string extensionName = "AzureNetworkWatcherExtension";
                var extensionInput = new VirtualMachineExtensionData(resourceGroup.Data.Location)
                {
                    Publisher = "Microsoft.Azure.NetworkWatcher",
                    ExtensionType = "NetworkWatcherAgentWindows",
                    TypeHandlerVersion = "1.4",
                    AutoUpgradeMinorVersion = true,
                };

                // Create vm1
                Utilities.Log("Creating a vm...");
                NetworkInterfaceResource nic1 = await Utilities.CreateNetworkInterface(resourceGroup, vnet1);
                VirtualMachineData vmInput1 = Utilities.GetDefaultVMInputData(resourceGroup, vmName1);
                vmInput1.NetworkProfile.NetworkInterfaces.Add(new VirtualMachineNetworkInterfaceReference() { Id = nic1.Id, Primary = true });
                var vmLro1 = await resourceGroup.GetVirtualMachines().CreateOrUpdateAsync(WaitUntil.Completed, vmName1, vmInput1);
                VirtualMachineResource vm1 = vmLro1.Value;
                _ = await vm1.GetVirtualMachineExtensions().CreateOrUpdateAsync(WaitUntil.Completed, extensionName, extensionInput);
                Utilities.Log($"Created vm: {vm1.Data.Name}");
                // Create vm2
                Utilities.Log("Creating a vm...");
                NetworkInterfaceResource nic2 = await Utilities.CreateNetworkInterface(resourceGroup, vnet2);
                VirtualMachineData vmInput2 = Utilities.GetDefaultVMInputData(resourceGroup, vmName2);
                vmInput2.NetworkProfile.NetworkInterfaces.Add(new VirtualMachineNetworkInterfaceReference() { Id = nic2.Id, Primary = true });
                var vmLro2 = await resourceGroup.GetVirtualMachines().CreateOrUpdateAsync(WaitUntil.Completed, vmName2, vmInput2);
                VirtualMachineResource vm2 = vmLro2.Value;
                _ = await vm2.GetVirtualMachineExtensions().CreateOrUpdateAsync(WaitUntil.Completed, extensionName, extensionInput);
                Utilities.Log($"Created vm: {vm2.Data.Name}");

                // Block: https://github.com/Azure/azure-sdk-for-net/pull/38876
                // System.ArgumentException: 'Value cannot be an empty string. (Parameter 'resourceId')'
                ConnectivityContent content = new ConnectivityContent(
                    new ConnectivitySource(vm1.Id),
                    new ConnectivityDestination() { Port = 22, ResourceId = vm2.Id });
                var connectivityResult = await networkWatcher.CheckConnectivityAsync(WaitUntil.Completed, content);
                Utilities.Log("Connectivity status: " + connectivityResult.Value.NetworkConnectionStatus);
            }
            finally
            {
                try
                {
                    if (_resourceGroupId is not null)
                    {
                        Utilities.Log($"Deleting Resource Group...");
                        await client.GetResourceGroupResource(_resourceGroupId).DeleteAsync(WaitUntil.Completed);
                        Utilities.Log($"Deleted Resource Group: {_resourceGroupId.Name}");
                    }
                }
                catch (NullReferenceException)
                {
                    Utilities.Log("Did not create any resources in Azure. No clean up is necessary");
                }
                catch (Exception ex)
                {
                    Utilities.Log(ex);
                }
            }
        }

        public static async Task Main(string[] args)
        {
            try
            {
                //=================================================================
                // Authenticate
                var clientId = Environment.GetEnvironmentVariable("CLIENT_ID");
                var clientSecret = Environment.GetEnvironmentVariable("CLIENT_SECRET");
                var tenantId = Environment.GetEnvironmentVariable("TENANT_ID");
                var subscription = Environment.GetEnvironmentVariable("SUBSCRIPTION_ID");
                ClientSecretCredential credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
                ArmClient client = new ArmClient(credential, subscription);

                await RunSample(client);
            }
            catch (Exception ex)
            {
                Utilities.Log(ex);
            }
        }
    }
}