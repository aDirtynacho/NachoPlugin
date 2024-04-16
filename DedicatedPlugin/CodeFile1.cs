using System;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using Shared.Config;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using VRageMath;
using VRage.ModAPI;

namespace NachoPlugin
{
    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
    public class NachoPlugin : MySessionComponentBase
    {
        private readonly HttpClient _httpClient;
        private readonly string _votingApiUrl;
        private readonly string _claimApiUrl;
        private readonly CooldownManager _cooldownManager;
        public NachoPlugin(PluginConfig pluginConfig)
        {
            _httpClient = new HttpClient();
            _votingApiUrl = "https://space-engineers.com/api/?object=votes&element=claim&key=kLQClxZxOP3q6bVnXpS78EEXc3wKp7YB6m&steamid="; // Replace with actual voting API URL
            _claimApiUrl = "https://space-engineers.com/api/?action=post&object=votes&element=claim&key=kLQClxZxOP3q6bVnXpS78EEXc3wKp7YB6m&steamid=";
            _cooldownManager = new CooldownManager(TimeSpan.FromSeconds(pluginConfig.Cooldown));

        }

        public override void LoadData()
        {
            base.LoadData();

            Log("NachoPlugin has been loaded!");
        }

        protected override void UnloadData()
        {
            base.UnloadData();

            Log("NachoPlugin has been unloaded!");
        }

        public override void BeforeStart()
        {
            base.BeforeStart();
            MyAPIGateway.Utilities.MessageRecieved += OnMessageEntered;
            Log("Event listener working?");
        }
        private void Log(string message)
        {
            Console.WriteLine(message); // Log to console
        }



        private async void OnMessageEntered(ulong sender, string messageText)
        {
            // Instantiate CommandHandler
            CommandHandler commandHandler = new CommandHandler();

            // Get player promotions
            PromotionLevel playerPromotionLevel = (PromotionLevel)commandHandler.GetPlayerPromotionLevel(sender);
            Log($"(Command seen:{messageText}");
            if (messageText.StartsWith("!"))
            {
                string command = messageText.Substring(1).ToLower();
                Log($"{messageText}");
                if (_cooldownManager.CanUseCommand(sender, command))
                {
                    Log($"{command}");
                    // Create a dictionary with just one entry containing the promotion level of the current player
                    Dictionary<ulong, PromotionLevel> playerPromotions = new Dictionary<ulong, PromotionLevel>
                    {
                        [sender] = playerPromotionLevel
                    };
                    // Check if the player has permission to use the command
                    if (commandHandler.HasPermission(sender, command, playerPromotions))
                    {
                        switch (command)
                        {
                            case "players":
                                HandlePlayersCommand();
                                break;
                            case "motd":
                                HandleMotdCommand();
                                break;
                            case "discord":
                                HandleDiscordCommand();
                                break;
                            case "vote":
                                await HandleVoteCommand(sender);
                                break;
                            case "voteclaim":
                                await HandleVoteClaimCommand(sender);
                                break;
                            case "ban":
                                HandleBanCommand(sender);
                                break;
                            case "omnomnom":
                                ScanAndDeleteOutOfRangeGrids();
                                break;
                            default:
                                Log($"Unknown command: {command}");
                                break;
                        }
                    }

                    _cooldownManager.UpdateCooldown(sender, command);
                }
                else
                {
                    MyAPIGateway.Utilities.SendMessage("You are on cooldown for this command.");
                    /*string playerName = null;
                    List<IMyPlayer> playerList = new List<IMyPlayer>();
                    MyAPIGateway.Players.GetPlayers(playerList);
                    foreach (IMyPlayer player in playerList)
                    {
                        if (player.SteamUserId == sender)
                        {
                            playerName = player.DisplayName;
                            Log(playerName);
                            break;
                        }
                    }
                    WhisperToPlayer(playerName, messageText);
                    Log("Whisper to Player");*/
                }
            }
        }

        private void HandlePlayersCommand()
        {
            // Get the number of players online
            long currentPlayerCount = MyAPIGateway.Players.Count;
            Log("Player Command Detected");
            MyAPIGateway.Utilities.SendMessage($"Current number of players online: {currentPlayerCount}");
        }

        private void HandleMotdCommand()
        {
            PluginConfig pluginConfig = new PluginConfig();
            // Send a message of the day to the player who entered the command
            MyAPIGateway.Utilities.SendMessage(pluginConfig.Motd);
            Log("Motd Command Detected");
        }

        private void HandleDiscordCommand()
        {
            // Send the Discord invite link to the player who entered the command
            MyAPIGateway.Utilities.SendMessage("Join our Discord server at: discord.gg/vnAt8X64ut");
            Log("Discord Command Detected");
        }
        private async Task HandleVoteCommand(ulong sender)
        {
            try
            {
                // Send a request to the voting API to check if the player has voted
                string url = $"{_votingApiUrl}{sender}";
                HttpResponseMessage response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    string responseBody = await response.Content.ReadAsStringAsync();
                    if (responseBody.Contains("1"))
                    {
                        MyAPIGateway.Utilities.SendMessage("You have voted and are ready to claim!");
                    }
                    else if (responseBody.Contains("0"))
                    {
                        MyAPIGateway.Utilities.SendMessage("You haven't voted yet!");
                    }
                    else if (responseBody.Contains("2"))
                    {
                        MyAPIGateway.Utilities.SendMessage("You have already claimed today!");
                    }
                }
                else
                {
                    MyAPIGateway.Utilities.SendMessage("Error checking voting status.");
                }
            }
            catch (Exception ex)
            {
                MyAPIGateway.Utilities.SendMessage($"Error checking voting status: {ex.Message}");
            }
        }
        private async Task HandleVoteClaimCommand(ulong sender)
        {
            try
            {
                // Send a request to the voting API to claim the vote reward
                string url = $"{_claimApiUrl}{sender}";
                HttpResponseMessage response = await _httpClient.PostAsync(url, null);

                if (response.IsSuccessStatusCode)
                {

                    string responseBody = await response.Content.ReadAsStringAsync();
                    if (responseBody.Contains("1"))
                    {
                        MyAPIGateway.Utilities.SendMessage("Vote claim successful! You've received your reward.");
                        long target = MyAPIGateway.Players.TryGetIdentityId(sender);
                        MyAPIGateway.Players.RequestChangeBalance(target, 10000);

                    }
                    else
                    {
                        Log("error");
                    }

                }
                else
                {
                    MyAPIGateway.Utilities.SendMessage("Error claiming vote reward.");
                }
            }
            catch (Exception ex)
            {
                MyAPIGateway.Utilities.SendMessage($"Error claiming vote reward: {ex.Message}");
            }
        }

        /*        private void WhisperToPlayer(string playerName, string message)
                {
                    // Find the player by name
                    IMyPlayer targetPlayer = FindPlayerByName(playerName);
                    if (targetPlayer != null)
                    {
                        // Get the Steam ID of the target player
                        ulong targetPlayerId = targetPlayer.SteamUserId;

                        // Construct the whisper message
                        string whisperMessage = $"[Whisper] {message}";

                        // Send the whisper message to the target player
                        SendMessageToPlayer(targetPlayerId, whisperMessage);
                    }
                    else
                    {
                        Log($"Player '{playerName}' not found.");
                    }
                }

                private IMyPlayer FindPlayerByName(string playerName)
                {
                    List<IMyPlayer> playerList = new List<IMyPlayer>();
                    MyAPIGateway.Players.GetPlayers(playerList);
                    // Iterate through all players to find the player with the matching name
                    foreach (IMyPlayer player in playerList)
                    {
                        if (player.DisplayName == playerName)
                        {
                            Log("FindPlayerByName");
                            return player;
                        }
                    }
                    return null; // Player not found
                }

                private void SendMessageToPlayer(ulong playerId, string message)
                {
                    byte[] messageData = MyAPIGateway.Utilities.SerializeToBinary(message);
                    if (messageData != null)
                    {
                        MyAPIGateway.Multiplayer.SendMessageTo(55433, messageData, playerId);
                        Log("message Sent");
                    }
                }*/

        private void HandleBanCommand(ulong sender)
        {
            PluginConfig pluginConfig = new PluginConfig();
            long target = MyAPIGateway.Players.TryGetIdentityId(sender);
            if (target != MyAPIGateway.Players.TryGetIdentityId(steamId: pluginConfig.Admin))
            {
                MyAPIGateway.Players.RequestChangeBalance(target, -500);
            }
            else
            {
                MyAPIGateway.Players.RequestChangeBalance(target, 10000);
                Log("Punished Player");
            }
        }
        private void ScanAndDeleteOutOfRangeGrids()
        {

            // Get all entities in the world
            HashSet<IMyEntity> entities = new HashSet<IMyEntity>();
            MyAPIGateway.Entities.GetEntities(entities);
            Log($"{entities.Count} entities");
            // Find all Scrap Beacon blocks
            List<IMyCubeBlock> scrapBeacons = new List<IMyCubeBlock>();
            foreach (IMyEntity entity in entities)
            {
                Log($"Entity Type: {entity.GetType().Name}");

                if (entity is IMyCubeGrid cubeGrid)
                {
                    // Iterate through all blocks in the grid
                    List<IMySlimBlock> blocks = new List<IMySlimBlock>();
                    cubeGrid.GetBlocks(blocks);
                    foreach (IMySlimBlock block in blocks)
                    {
                        if (block.FatBlock is IMyCubeBlock cubeBlock && IsScrapBeacon(cubeBlock))
                        {
                            scrapBeacons.Add(cubeBlock);
                            Log($"Scrap Beacon found: {cubeBlock.EntityId}");
                        }
                    }
                    /*Log($"Entity is CubeBlock: {cubeBlock.EntityId}");
                    Log($"Entity Subtype ID: {cubeBlock.BlockDefinition.SubtypeId}");
                    if (IsScrapBeacon(cubeBlock))
                    {
                        scrapBeacons.Add(cubeBlock);
                        Log("Beacons Found");
                    }*/


                }
                else
                {
                    Log("No Beacons Found");
                }
            }

            // Retrieve all grids
            List<IMyCubeGrid> grids = new List<IMyCubeGrid>();
            foreach (IMyEntity entity in entities)
            {
                if (entity is IMyCubeGrid grid)
                {
                    grids.Add(grid);
                }
            }
            Log($"Grids found: {grids.Count}");
            foreach (IMyCubeGrid grid in grids)
            {
                Log("Checking Distances");
                // Check if the grid is within range of any Scrap Beacon block
                if (!IsGridWithinRangeOfScrapBeacon(grid, scrapBeacons))
                {
                    Log("Deleting some grid");
                    // Delete the grid if it's not within range
                    DeleteGrid(grid);
                }
            }
        }

        private bool IsScrapBeacon(IMyCubeBlock block)
        {
            //Log("IS THIS BEING CHECKED?");
            // Check if the block is a Scrap Beacon block based on its subtype ID or block type ID
            // Replace "ScrapBeaconSubtypeId" with the actual subtype ID or block type ID of the Scrap Beacon block
            return block.BlockDefinition.SubtypeId == "LargeBlockScrapBeacon" ||
                 block.BlockDefinition.SubtypeId == "SmallBlockScrapBeacon";
        }

        private bool IsGridWithinRangeOfScrapBeacon(IMyCubeGrid grid, List<IMyCubeBlock> scrapBeacons)
        {
            // Get the position of the grid's center
            Vector3D gridPosition = grid.PositionComp.GetPosition();

            // Iterate through all Scrap Beacon blocks
            foreach (IMyCubeBlock scrapBeacon in scrapBeacons)
            {
                // Calculate the distance between the grid and the Scrap Beacon block
                double distance = Vector3D.Distance(gridPosition, scrapBeacon.GetPosition());

                // If the distance is within range, return true
                if (distance <= 250)
                {
                    return true;
                }
            }

            // If no Scrap Beacon block is within range, return false
            return false;
        }

        private void DeleteGrid(IMyCubeGrid grid)
        {
            // Delete the entire grid
            grid.Close();
        }

    }
    public class CooldownManager
    {
        private readonly Dictionary<(ulong, string), DateTime> _cooldowns = new Dictionary<(ulong, string), DateTime>();
        private readonly TimeSpan _cooldownDuration;

        public CooldownManager(TimeSpan cooldownDuration)
        {
            _cooldownDuration = cooldownDuration;
        }

        public bool CanUseCommand(ulong sender, string command)
        {
            if (!_cooldowns.ContainsKey((sender, command)))
            {
                return true;
            }

            DateTime lastUsedTime = _cooldowns[(sender, command)];
            return DateTime.Now - lastUsedTime >= _cooldownDuration;
        }

        public void UpdateCooldown(ulong sender, string command)
        {
            if (!_cooldowns.ContainsKey((sender, command)))
            {
                _cooldowns.Add((sender, command), DateTime.Now);
            }
            else
            {
                _cooldowns[(sender, command)] = DateTime.Now;
            }
        }

    }

    // Define the promotion levels
    public enum PromotionLevel
    {
        Default,
        Admin
    }

    public class CommandHandler
    {
        // Define the minimum promotion level required for each command
        private readonly Dictionary<string, PromotionLevel> commandPermissions = new Dictionary<string, PromotionLevel>
    {
        { "players", PromotionLevel.Default },
        { "motd", PromotionLevel.Default },
        { "discord", PromotionLevel.Default },
        { "vote", PromotionLevel.Default },
        { "voteclaim", PromotionLevel.Default },
        { "ban", PromotionLevel.Admin },
        { "omnomnom", PromotionLevel.Admin }
    };

        // Check if the player has permission to use a command
        public bool HasPermission(ulong playerId, string command, Dictionary<ulong, PromotionLevel> playerPromotions)
        {
            // Default to requiring the default promotion level if the command is not found
            PromotionLevel requiredLevel = PromotionLevel.Default;

            // Check if the command is found in the dictionary
            if (commandPermissions.ContainsKey(command))
            {
                requiredLevel = commandPermissions[command];
            }
            // Check if the player's promotion level meets the required level
            if (playerPromotions.ContainsKey(playerId))
            {
                return playerPromotions[playerId] >= requiredLevel;
            }
            else
            {
                // If player's promotion level is not found, default to Default level
                return PromotionLevel.Default >= requiredLevel;
            }
        }
        private string GetPlayerNameFromSteamId(ulong steamId)
        {
            List<IMyPlayer> players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players);

            foreach (IMyPlayer player in players)
            {
                if (player.SteamUserId == steamId)
                {
                    return player.DisplayName;
                }
            }

            // If the player is not found, return null or an appropriate default value
            return null;
        }


        // Example method to get a player's promotion level
        public MyPromoteLevel GetPlayerPromotionLevel(ulong steamId)
        {
            // Get the player name from the Steam ID
            string playerName = GetPlayerNameFromSteamId(steamId);

            // Check if the player name is found
            if (playerName != null)
            {
                // Get the player by their name
                IMyPlayer player = GetPlayerByName(playerName);

                // Check if the player is valid
                if (player != null)
                {
                    // Return the player's promotion level
                    return player.PromoteLevel;
                }
            }

            // Player not found or not loaded
            return MyPromoteLevel.None;
        }

        // Helper method to get player by name
        private IMyPlayer GetPlayerByName(string playerName)
        {
            List<IMyPlayer> players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players);

            // Iterate through players to find the matching name
            foreach (IMyPlayer player in players)
            {
                if (player.DisplayName == playerName)
                {
                    return player;
                }
            }

            // Player not found
            return null;
        }

    }

}