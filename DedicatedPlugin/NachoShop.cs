using NachoPluginSystem;
using Sandbox.Common.ObjectBuilders.Definitions;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using System.IO;
using System.Text;
using System.Xml.Serialization;
using VRage.FileSystem;
using VRageMath;
using VRage.ObjectBuilders;
using VRage;
using System.Windows.Forms;

namespace NachoPluginSystem
{


    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
    public class ServerShopHandler : MySessionComponentBase
    {
        private const ushort ServerCommunicationId = 22346; // Same unique identifier as the client
        private Dictionary<string, ShopItem> shopItems;
        private NachoPlugin nachoPlugin;

        public override void Init(MyObjectBuilder_SessionComponent sessionComponent)
        {
            if (MyAPIGateway.Multiplayer.IsServer)
            {
                shopItems = new Dictionary<string, ShopItem>();

                // Load shop items from XML file
                LoadShopItemsFromXML();

                MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(ServerCommunicationId, OnMessageReceived);
                Console.WriteLine("ServerShopHandler", "Server shop handler initialized.");
                Console.WriteLine("NACHO SHOP LOADED");
            }
        }
        public override void BeforeStart()
        {
            base.BeforeStart();
            nachoPlugin = new NachoPlugin();
        }

        protected override void UnloadData()
        {
            if (MyAPIGateway.Multiplayer.IsServer)
            {
                MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(ServerCommunicationId, OnMessageReceived);
            }
        }

        private void OnMessageReceived(ushort handler, byte[] message, ulong senderId, bool fromServer)
        {
            try
            {
                Console.WriteLine("MESSAGE RECEIVED");
                string request = Encoding.UTF8.GetString(message);
                Console.WriteLine(request);

                if (request == "RequestItems")
                {
                    LoadShopItemsFromXML();
                    NachoPlugin.Log("Shop items reloaded:");
                    foreach (var item in shopItems)
                    {
                        NachoPlugin.Log($"Item: {item.Key}, Name: {item.Value.Name}, Price: {item.Value.Price}, Subtype: {item.Value.Subtype}, Type: {item.Value.Type}");
                    }
                    // Prepare the item list
                    var itemList = shopItems.Values.ToList();
                    string itemListXml = MyAPIGateway.Utilities.SerializeToXML(itemList);
                    byte[] response = Encoding.UTF8.GetBytes("ItemsList:" + itemListXml);

                    // Send the item list back to the client
                    MyAPIGateway.Multiplayer.SendMessageTo(ServerCommunicationId, response, senderId);
                    Console.WriteLine("sending data!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!");
                }
                else
                {
                    // Try to determine the type of request
                    if (request.Contains("<SellRequest"))
                    {
                        var sellRequestObj = MyAPIGateway.Utilities.SerializeFromXML<SellRequest>(request);
                        HandleSellingRequest(senderId, sellRequestObj.Type, sellRequestObj.ItemName, sellRequestObj.Quantity, sellRequestObj.TotalCost);
                    }
                    else if (request.Contains("<PurchaseRequest"))
                    {
                        var purchaseRequestObj = MyAPIGateway.Utilities.SerializeFromXML<PurchaseRequest>(request);
                        HandlePurchaseRequest(senderId, purchaseRequestObj.Type, purchaseRequestObj.ItemName, purchaseRequestObj.Quantity, purchaseRequestObj.TotalCost);
                    }
                    else
                    {
                        MyAPIGateway.Utilities.ShowMessage("ServerShopHandler", "Unknown request received.");
                        NachoPlugin.Log($"Unknown request received: {request}");
                    }
                }
            }
            catch (Exception ex)
            {
                NachoPlugin.Log($"Error in OnMessageReceived: {ex.Message}");
            }
        }
        // Method to check if a player has the specified items in their inventory
        public bool PlayerHasItems(IMyPlayer player, string itemType, string itemSubtype, ulong quantity)
        {
            NachoPlugin.Log($"{itemType}{itemSubtype}");
            IMyInventory inventory = player.Character.GetInventory();
            if (inventory == null)
                return false;
            var (itemT, itemS1) = ItemSubtypeHelper.GetItemSubtype($"{itemSubtype}");
            MyDefinitionId itemId = new MyDefinitionId(MyObjectBuilderType.Parse(itemT), itemS1);
            var items = inventory.GetItems();
            ulong itemCount = 0;

            foreach (var item in items)
            {
                if (item.Content.GetId() == itemId)
                {
                    itemCount += (ulong)item.Amount.RawValue;
                    if (itemCount >= quantity)
                    {
                        return true;
                    }
                }
            }

            return itemCount >= quantity;
        }

        // Method to remove specified items from a player's inventory
        public void RemovePlayerItems(IMyPlayer player, string itemType, string itemSubtype, int quantity)
        {
            IMyInventory inventory = player.Character.GetInventory();
            if (inventory == null)
                return;
            var (itemT, itemS1) = ItemSubtypeHelper.GetItemSubtype($"{itemSubtype}");
            MyDefinitionId itemId = new MyDefinitionId(MyObjectBuilderType.Parse(itemT), itemS1);
            var items = inventory.GetItems();

            foreach (var item in items)
            {
                if (item.Content.GetId() == itemId)
                {
                    int itemAmount = (int)item.Amount;
                    if (itemAmount <= quantity)
                    {
                        quantity -= itemAmount;
                        inventory.RemoveItemsOfType((MyFixedPoint)itemAmount, item.Content.GetId());
                    }
                    else
                    {
                        inventory.RemoveItemsOfType((MyFixedPoint)quantity, item.Content.GetId());
                        quantity = 0;
                    }

                    if (quantity == 0)
                        break;
                }
            }
        }
        private void HandlePurchaseRequest(ulong senderId, string itemTypes, string itemName, ulong quantity, ulong totalCost)
        {
            NachoPrefabPrinter nachoPrefabPrinter = new NachoPrefabPrinter();
            NachoPlugin.Log(itemName);
            NachoPlugin.Log(itemTypes);
            try
            {
                if (itemTypes == "MyObjectBuilder_PrefabDefinition")
                {
                    string amount = quantity.ToString();
                    ShopItem selectedItem = shopItems[itemName];
                    ulong itemPrice = selectedItem.Price;
                    ulong expectedCost = itemPrice * quantity;
                    Console.WriteLine(expectedCost);
                    if (expectedCost == totalCost && totalCost > 0)
                    {
                        string playerName = nachoPlugin.GetPlayerNameFromSteamId(senderId);
                        long senderIdentityId = MyAPIGateway.Players.TryGetIdentityId(senderId);
                        var senderID = senderIdentityId.ToString();
                        string sender = nachoPlugin.GetPlayerNameFromSteamId(senderId);
                        nachoPlugin.GetPlayerBalance(sender, out long senderBank);
                        ulong senderBankUlong = (ulong)senderBank;
                        Console.WriteLine($"{senderBank}{senderBankUlong}");
                        var player = nachoPlugin.GetPlayerByName(sender);

                        if (senderBankUlong < expectedCost)
                        {
                            NachoPlugin.Log($"Insufficient balance for sender: {nachoPlugin.GetPlayerNameFromSteamId(senderId)}");
                            var message2 = Encoding.UTF8.GetBytes("Error:Whoa there, Bank Account ain't got that much! :P");
                            MyAPIGateway.Multiplayer.SendMessageTo(ServerCommunicationId, message2, senderId);
                            return;
                        }
                        long expectedCostLong = (long)expectedCost;
                        Console.WriteLine($"Expected costs?{expectedCost}{expectedCostLong}");
                        MyAPIGateway.Players.RequestChangeBalance(senderIdentityId, -expectedCostLong);
                        //This is the place where we have to find the players location and spawn the prefab in front of them, with a added height of 3 meters to make sure its a safe spawn
                        nachoPrefabPrinter.SpawnPrefab(player, itemName);
                        var successMessage = Encoding.UTF8.GetBytes("Success:Purchase complete");
                        MyAPIGateway.Multiplayer.SendMessageTo(ServerCommunicationId, successMessage, senderId);


                    }
                }
                // Validate the item
                if (itemTypes != "MyObjectBuilder_PrefabDefinition")
                {
                    string finalType = "MyObjectBuilder_" + itemTypes;
                    NachoPlugin.Log(finalType);
                    string amount = quantity.ToString();
                    ShopItem selectedItem = shopItems[itemName];
                    NachoPlugin.Log($"{selectedItem}");
                    ulong itemPrice = selectedItem.Price;
                    ulong expectedCost = itemPrice * quantity;
                    Console.WriteLine(expectedCost);
                    if (expectedCost == totalCost && totalCost > 0)
                    {

                        string playerName = nachoPlugin.GetPlayerNameFromSteamId(senderId);
                        NachoPlugin.Log($"{playerName} wants to buy {quantity} of {selectedItem.Name} (Subtype: {selectedItem.Subtype}) for a total of {totalCost} SC.");
                        MyAPIGateway.Utilities.ShowMessage("ServerShopHandler", $"{playerName} wants to buy {quantity} of {selectedItem.Name} (Subtype: {selectedItem.Subtype}) for a total of {totalCost} SC.");

                        long senderIdentityId = MyAPIGateway.Players.TryGetIdentityId(senderId);
                        var senderID = senderIdentityId.ToString();
                        string sender = nachoPlugin.GetPlayerNameFromSteamId(senderId);
                        nachoPlugin.GetPlayerBalance(sender, out long senderBank);
                        ulong senderBankUlong = (ulong)senderBank;
                        Console.WriteLine($"{senderBank}{senderBankUlong}");
                        var player = nachoPlugin.GetPlayerByName(sender);
                        NachoPlugin.Log($"{player}{sender}{senderId}");
                        float playerInventoryVolume = nachoPlugin.GetPlayerInventoryVolume(player);
                        float playerMaxInventoryVolume = nachoPlugin.GetPlayerMaxInventoryVolume(player);
                        float playerAvailableInventoryVolume = playerMaxInventoryVolume - playerInventoryVolume;

                        // Get the volume and subtype of the item dynamically
                        float itemVolume = ComponentVolumeHelper.GetComponentVolume(itemName);
                        NachoPlugin.Log($"{itemVolume}");
                        float totalItemVolume = itemVolume * quantity;

                        // Check if the sender has sufficient balance
                        if (senderBankUlong < expectedCost)
                        {
                            NachoPlugin.Log($"Insufficient balance for sender: {nachoPlugin.GetPlayerNameFromSteamId(senderId)}");
                            var message2 = Encoding.UTF8.GetBytes("Error:Whoa there, Bank Account ain't got that much! :P");
                            MyAPIGateway.Multiplayer.SendMessageTo(ServerCommunicationId, message2, senderId);
                            return;
                        }
                        // Check if the player can hold the purchased items
                        if (totalItemVolume > playerAvailableInventoryVolume)
                        {
                            NachoPlugin.Log($"Insufficient inventory space for sender: {nachoPlugin.GetPlayerNameFromSteamId(senderId)}");
                            var message2 = Encoding.UTF8.GetBytes("Error:Whoa there, Inventory ain't got that much space! :P");
                            MyAPIGateway.Multiplayer.SendMessageTo(ServerCommunicationId, message2, senderId);
                            return;
                        }

                        long expectedCostLong = (long)expectedCost;
                        Console.WriteLine($"Expected costs?{expectedCost}{expectedCostLong}");
                        MyAPIGateway.Players.RequestChangeBalance(senderIdentityId, -expectedCostLong);
                        NachoPlugin.Log($"{senderID + "1"},{finalType + "2"},{itemName+"3"},{amount+"--4"}");
                        nachoPlugin.GivePlayerItem(senderId, finalType, itemName, amount);
                        var successMessage = Encoding.UTF8.GetBytes("Success:Purchase complete");
                        MyAPIGateway.Multiplayer.SendMessageTo(ServerCommunicationId, successMessage, senderId);
                    }
                    else
                    {
                        MyAPIGateway.Utilities.ShowMessage("ServerShopHandler", $"Purchase request total cost mismatch or negative total cost. {itemName}{quantity}");
                    }
                }
                else
                {
                    NachoPlugin.Log($"Item {itemName} not found in shop, due to plugin/file error.");
                }
            }
            catch (Exception ex)
            {
                NachoPlugin.Log($"Error in HandlePurchaseRequest: {ex.Message},{ex.InnerException},{ex.Source},{ex.Data},{ex.StackTrace},{ex.TargetSite}");
            }
        }
        private void HandleSellingRequest(ulong senderId, string itemType, string itemName, ulong quantity, ulong totalCost)
        {
            NachoPlugin.Log(itemName);
            try
            {
                    itemName = itemName.Trim();                   
                    ShopItem selectedItem = shopItems[itemName];
                    ulong itemSellPrice = selectedItem.SellPrice;
                    ulong expectedPayment = itemSellPrice * quantity;

                    if (expectedPayment == totalCost && totalCost > 0)
                    {
                        string playerName = nachoPlugin.GetPlayerNameFromSteamId(senderId);
                        long senderIdentityId = MyAPIGateway.Players.TryGetIdentityId(senderId);
                        var player = nachoPlugin.GetPlayerByName(playerName);
                        float playerInventoryVolume = nachoPlugin.GetPlayerInventoryVolume(player);
                        float playerMaxInventoryVolume = nachoPlugin.GetPlayerMaxInventoryVolume(player);

                        // Check if the player has the items in their inventory
                        bool hasItems = PlayerHasItems(player, itemType, itemName, quantity);
                        if (!hasItems)
                        {
                            var errorMessage = Encoding.UTF8.GetBytes("Error:You don't have enough items to sell.");
                            MyAPIGateway.Multiplayer.SendMessageTo(ServerCommunicationId, errorMessage, senderId);
                            return;
                        }

                        // Remove items from player's inventory and add money to their balance
                        RemovePlayerItems(player, itemType, itemName, (int)quantity);
                        MyAPIGateway.Players.RequestChangeBalance(senderIdentityId, (long)expectedPayment);

                        var successMessage = Encoding.UTF8.GetBytes("Success:Sale complete");
                        MyAPIGateway.Multiplayer.SendMessageTo(ServerCommunicationId, successMessage, senderId);
                    }
                    else
                    {
                        MyAPIGateway.Utilities.ShowMessage("ServerShopHandler", $"Sale request total payment mismatch or negative total payment. {itemName} {quantity}");
                    }
            }
            catch (Exception ex)
            {
                NachoPlugin.Log($"Error in HandleSellingRequest: {ex.Message}\nStack Trace: {ex.StackTrace}");
            }
        }

        private void LoadShopItemsFromXML()
        {
            try
            {
                string filePath = Path.Combine(MyFileSystem.UserDataPath, "ShopItems.xml");
                if (File.Exists(filePath))
                {
                    using (StreamReader reader = new StreamReader(filePath, Encoding.UTF8))
                    {
                        XmlSerializer serializer = new XmlSerializer(typeof(ShopItems));
                        ShopItems items = (ShopItems)serializer.Deserialize(reader);
                        shopItems = items.Items.ToDictionary(item => item.Subtype, item => item);
                    }
                }
                else
                {
                    NachoPlugin.Log( "ShopItems.xml not found.");
                    CreateDefaultShopItemsXML(filePath);
                }
            }
            catch (Exception ex)
            {
                NachoPlugin.Log($"Error loading shop items from XML: {ex.Message}");
            }
        }

        public void ReloadShopItems()
        {
            LoadShopItemsFromXML();
            NachoPlugin.Log("Reloaded Shop Items!");
        }

        private void CreateDefaultShopItemsXML(string filePath)
        {
            try
            {
                ShopItems defaultItems = new ShopItems
                {
                    Items = new List<ShopItem>
                    {
                        new ShopItem { Name = "Steel Plates", Price = 10000, SellPrice = 0, Subtype = "SteelPlate", Type = "Component" },
                        new ShopItem { Name = "Power Cells", Price = 20000, SellPrice = 0, Subtype = "PowerCell", Type = "Component" },
                        new ShopItem { Name = "Solar Cells", Price = 30000, SellPrice = 0, Subtype = "SolarCell", Type = "Component" },
                        new ShopItem { Name = "Large Grid", Price = 100000, SellPrice = 0, Subtype = "MetalGrid", Type = "Component" },
                        new ShopItem { Name = "Large Tube", Price = 100000, SellPrice = 0, Subtype = "LargeTube", Type = "Component" }
                    }
                };

                using (StreamWriter writer = new StreamWriter(filePath, false, Encoding.UTF8))
                {
                    XmlSerializer serializer = new XmlSerializer(typeof(ShopItems));
                    serializer.Serialize(writer, defaultItems);
                }

                NachoPlugin.Log("Default ShopItems.xml created.");
            }
            catch (Exception ex)
            {
                NachoPlugin.Log($"Error creating default shop items XML: {ex.Message}");
            }
        }
    }

    public class ComponentVolumeHelper
    {
        public static float GetComponentVolume(string componentName)
        {
            var definitionManager = MyDefinitionManager.Static;
            MyDefinitionBase definition = null;

            var itemTypes = new[]
            {
            typeof(MyObjectBuilder_Component),
            typeof(MyObjectBuilder_Ore),
            typeof(MyObjectBuilder_Ingot),
            typeof(MyObjectBuilder_PhysicalGunObject),
            typeof(MyObjectBuilder_AmmoMagazine),
            typeof(MyObjectBuilder_OxygenContainerObject),
            typeof(MyObjectBuilder_GasContainerObject)
        };

            foreach (var itemType in itemTypes)
            {
                definition = definitionManager.GetDefinition(new MyDefinitionId(itemType, componentName));
                if (definition is MyPhysicalItemDefinition physicalItemDef)
                {
                    return physicalItemDef.Volume;
                }
            }

            return 0f;
        }
    }



    public class ItemSubtypeHelper
    {
        public static (string itemType, string itemSubtype) GetItemSubtype(string itemName)
        {
            var definitionManager = MyDefinitionManager.Static;
            MyDefinitionBase itemDefinition = null;

            var itemTypes = new[]
            {
            typeof(MyObjectBuilder_Component),
            typeof(MyObjectBuilder_Ore),
            typeof(MyObjectBuilder_Ingot),
            typeof(MyObjectBuilder_PhysicalGunObject),
            typeof(MyObjectBuilder_AmmoMagazine),
            typeof(MyObjectBuilder_OxygenContainerObject),
            typeof(MyObjectBuilder_GasContainerObject)
            };

            // Iterate through the item types to find the correct definition
            foreach (var itemType in itemTypes)
            {
                itemDefinition = definitionManager.GetDefinition(new MyDefinitionId(itemType, itemName));
                if (itemDefinition != null)
                {
                    return (itemType.Name, itemDefinition.Id.SubtypeName);
                }
            }

            return (string.Empty, string.Empty);
        }
    }

    public class NachoPrefabPrinter
    {
        public void SpawnPrefab(IMyPlayer player, string prefabName)
        {
            // Ensure the player and prefab name are valid
            if (player == null || string.IsNullOrEmpty(prefabName))
                return;

            // Get the player's position and orientation
            Vector3D playerPosition = player.GetPosition();
            MatrixD playerMatrix = player.Character.WorldMatrix;

            // Calculate the spawn position in front of the player
            Vector3D forwardVector = playerMatrix.Forward;
            Vector3D upVector = playerMatrix.Up;
            Vector3D spawnPosition = playerPosition + forwardVector * 10.0 + upVector * 10.0; // Adjust the distance as needed

            // Load the prefab definition
            MyPrefabDefinition prefabDefinition = MyDefinitionManager.Static.GetPrefabDefinition(prefabName);
            if (prefabDefinition == null)
            {
                MyAPIGateway.Utilities.ShowMessage("Error", $"Prefab '{prefabName}' not found!");
                return;
            }

            // Create a list to store the spawned grids
            List<IMyCubeGrid> spawnedGrids = new List<IMyCubeGrid>();

            // Spawn the prefab
            MyAPIGateway.PrefabManager.SpawnPrefab(
                spawnedGrids,
                prefabName,
                spawnPosition,
                playerMatrix.Forward,
                playerMatrix.Up,
                Vector3.Zero,
                Vector3.Zero,
                null,
                SpawningOptions.None,
                false,
                () =>
                {
                    MyAPIGateway.Utilities.ShowMessage("Success", $"Spawned prefab '{prefabName}' in front of the player.");
                    SetBlocksToHalfBuilt(spawnedGrids);
                }
            );
        }
        private void SetBlocksToHalfBuilt(List<IMyCubeGrid> grids)
        {
            foreach (var grid in grids)
            {
                var cubeGrid = grid as IMyCubeGrid;
                if (cubeGrid != null)
                {
                    List<IMySlimBlock> blocks = new List<IMySlimBlock>();
                    cubeGrid.GetBlocks(blocks, block => block != null);
                    foreach (var block in blocks)
                    {
                        block.DecreaseMountLevel(block.BuildIntegrity / 2, null);
                        block.ClearConstructionStockpile(null);
                    }
                }
            }
        }
    }


    public class ShopItem
    {
        public string Name { get; set; }
        public ulong Price { get; set; }
        public ulong SellPrice { get; set; }
        public string Subtype { get; set; }
        public string Type { get; set; }
    }
    public class PurchaseRequest
    {
        public string ItemName { get; set; }
        public ulong Quantity { get; set; }
        public ulong TotalCost { get; set; }
        public string Type { get; set; }
    }
    public class SellRequest
    {
        public string ItemName { get; set; }
        public ulong Quantity { get; set; }
        public ulong TotalCost { get; set; }
        public string Type { get; set; }
    }
    [XmlRoot("ShopItems")]
    public class ShopItems
    {
        [XmlElement("ShopItem")]
        public List<ShopItem> Items { get; set; }
    }
}