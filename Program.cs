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
            string vpnConnectionName = Utilities.CreateRandomName("vnet-to-vnet-");
            string networkWatcherName = Utilities.CreateRandomName("watcher");
            string storageAccountName = Utilities.CreateRandomName("azstorageaccoun");

            rgName = "NetworkSampleRG000";
            vnetName1 = "vnet1-00";
            vnetName2 = "vnet2-00";
            vnetGatewayName1 = "vnetGateway1-00";
            vnetGatewayName2 = "vnetGateway2-00";
            pipName1 = "pip1-00";
            pipName2 = "pip2-00";
            networkWatcherName = "watcher-00";
            storageAccountName = "azstorageaccoun5154";
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
                var connectionLro = await resourceGroup.GetVirtualNetworkGatewayConnections().CreateOrUpdateAsync(WaitUntil.Completed, vpnConnectionName, gatewayConnectionInput);
                VirtualNetworkGatewayConnectionResource connection = connectionLro.Value;
                Utilities.Log($"Created virtual network gateway connection: {connection.Data.Name}");

                //============================================================
                // Troubleshoot the connection

                // create Network Watcher
                NetworkWatcherData networkWatcherInput = new NetworkWatcherData()
                {
                    //Location = resourceGroup.Data.Location,
                    Location = AzureLocation.WestUS2,
                };
                var networkWatcherLro = await resourceGroup.GetNetworkWatchers().CreateOrUpdateAsync(WaitUntil.Completed, networkWatcherName, networkWatcherInput);
                NetworkWatcherResource networkWatcher = networkWatcherLro.Value;

                // Create storage account to store troubleshooting information
                StorageSku storageSku = new StorageSku(StorageSkuName.StandardGrs);
                StorageKind storageKind = StorageKind.Storage;
                StorageAccountCreateOrUpdateContent storagedata = new StorageAccountCreateOrUpdateContent(storageSku, storageKind, resourceGroup.Data.Location)
                {
                };
                var storageAccountLro = await resourceGroup.GetStorageAccounts().CreateOrUpdateAsync(WaitUntil.Completed, storageAccountName, storagedata);
                StorageAccountResource storageAccount = storageAccountLro.Value;

                // Create storage container to store troubleshooting results 
                var accountKeyList = await storageAccount.GetKeysAsync().ToEnumerableAsync();
                string accountKey = accountKeyList.First().Value;
                string connectionString = $"DefaultEndpointsProtocol=https;AccountName={storageAccount.Data.Name};AccountKey={accountKey}";
                //Utilities.CreateContainer(connectionString, "");

                //// Run troubleshooting for the connection - result will be 'UnHealthy' as need to create symmetrical connection from second gateway to the first
                //ITroubleshooting troubleshooting = nw.Troubleshoot()
                //        .WithTargetResourceId(connection.Id)
                //        .WithStorageAccount(storageAccount.Id)
                //        .WithStoragePath(storageAccount.EndPoints.Primary.Blob + containerName)
                //        .Execute();
                //Utilities.Log("Troubleshooting status is: " + troubleshooting.Code);

                ////============================================================
                ////  Create virtual network connection from second gateway to the first and run troubleshooting. Result will be 'Healthy'.
                //vngw2.Connections
                //        .Define(connection2Name)
                //        .WithVNetToVNet()
                //        .WithSecondVirtualNetworkGateway(vngw1)
                //        .WithSharedKey("MySecretKey")
                //        .Create();
                //// Delay before running troubleshooting to wait for connection settings to propagate
                //SdkContext.DelayProvider.Delay(250000);
                //troubleshooting = nw.Troubleshoot()
                //        .WithTargetResourceId(connection.Id)
                //        .WithStorageAccount(storageAccount.Id)
                //        .WithStoragePath(storageAccount.EndPoints.Primary.Blob + containerName)
                //        .Execute();
                //Utilities.Log("Troubleshooting status is: " + troubleshooting.Code);

                ////============================================================
                //// List VPN Gateway connections for particular gateway
                //var connections = vngw1.ListConnections();
                //foreach (var conn in connections)
                //{
                //    Utilities.Print(conn);
                //}

                ////============================================================
                //// Create 2 virtual machines, each one in its network and verify connectivity between them
                //List<ICreatable<IVirtualMachine>> vmDefinitions = new List<ICreatable<IVirtualMachine>>();

                //vmDefinitions.Add(azure.VirtualMachines.Define(vm1Name)
                //        .WithRegion(region)
                //        .WithExistingResourceGroup(rgName)
                //        .WithExistingPrimaryNetwork(network1)
                //        .WithSubnet("Subnet1")
                //        .WithPrimaryPrivateIPAddressDynamic()
                //        .WithoutPrimaryPublicIPAddress()
                //        .WithPopularLinuxImage(KnownLinuxVirtualMachineImage.UbuntuServer16_04_Lts)
                //        .WithRootUsername(rootname)
                //        .WithRootPassword(password)
                //        // Extension currently needed for network watcher support
                //        .DefineNewExtension("networkWatcher")
                //            .WithPublisher("Microsoft.Azure.NetworkWatcher")
                //            .WithType("NetworkWatcherAgentLinux")
                //            .WithVersion("1.4")
                //            .Attach());
                //vmDefinitions.Add(azure.VirtualMachines.Define(vm2Name)
                //        .WithRegion(region)
                //        .WithExistingResourceGroup(rgName)
                //        .WithExistingPrimaryNetwork(network2)
                //        .WithSubnet("Subnet2")
                //        .WithPrimaryPrivateIPAddressDynamic()
                //        .WithoutPrimaryPublicIPAddress()
                //        .WithPopularLinuxImage(KnownLinuxVirtualMachineImage.UbuntuServer16_04_Lts)
                //        .WithRootUsername(rootname)
                //        .WithRootPassword(password)
                //        // Extension currently needed for network watcher support
                //        .DefineNewExtension("networkWatcher")
                //            .WithPublisher("Microsoft.Azure.NetworkWatcher")
                //            .WithType("NetworkWatcherAgentLinux")
                //            .WithVersion("1.4")
                //            .Attach());
                //ICreatedResources<IVirtualMachine> createdVMs = azure.VirtualMachines.Create(vmDefinitions);
                //IVirtualMachine vm1 = createdVMs.FirstOrDefault(vm => vm.Key == vmDefinitions[0].Key);
                //IVirtualMachine vm2 = createdVMs.FirstOrDefault(vm => vm.Key == vmDefinitions[1].Key);

                //IConnectivityCheck connectivity = nw.CheckConnectivity()
                //        .ToDestinationResourceId(vm2.Id)
                //        .ToDestinationPort(22)
                //        .FromSourceVirtualMachine(vm1.Id)
                //        .Execute();
                //Utilities.Log("Connectivity status: " + connectivity.ConnectionStatus);
            }
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
            //=================================================================
            // Authenticate
            var clientId = Environment.GetEnvironmentVariable("CLIENT_ID");
            var clientSecret = Environment.GetEnvironmentVariable("CLIENT_SECRET");
            var tenantId = Environment.GetEnvironmentVariable("TENANT_ID");
            var subscription = Environment.GetEnvironmentVariable("SUBSCRIPTION_ID");
            ClientSecretCredential credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
            ArmClient client = new ArmClient(credential, subscription);

            await RunSample(client);
            try
            {

            }
            catch (Exception ex)
            {
                Utilities.Log(ex);
            }
        }
    }
}