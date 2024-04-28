using System;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using VRageMath;
using VRage.ModAPI;
using Shared.Config;
using DedicatedPlugin;
using System.IO;
using VRage.FileSystem;
using Sandbox.Game.World;
using VRage.Scripting;
using Shared.Logging;
using VRage.Utils;
using System.Linq;

// Define the promotion levels
public enum PromotionLevel
{
    Default,
    Admin
}

namespace NachoPlugin
{
    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]

    public class NachoPlugin : MySessionComponentBase
    {
        // Define a timer to trigger the vote check every 10 minutes
        private System.Timers.Timer voteCheckTimer;

        // Dictionary to store vote totals for each player
        Dictionary<ulong, int> voteTotals = new Dictionary<ulong, int>();

        // Property to access voteTotals
        public Dictionary<ulong, int> VoteTotals
        {
            get { return voteTotals; }
        }
        // Dictionary to store the last checked time for each player
        private Dictionary<ulong, DateTime> lastCheckedTimes = new Dictionary<ulong, DateTime>();

        private readonly HttpClient _httpClient;
        private readonly string _votingApiUrl;
        private readonly string _claimApiUrl;
        private readonly string _votecheckApiUrl;
        private readonly CooldownManager _cooldownManager;

        public NachoPlugin()
        {
            _httpClient = new HttpClient();
            _votingApiUrl = "https://space-engineers.com/api/?object=votes&element=claim&key=kLQClxZxOP3q6bVnXpS78EEXc3wKp7YB6m&steamid="; // Replace with actual voting API URL
            _claimApiUrl = "https://space-engineers.com/api/?action=post&object=votes&element=claim&key=kLQClxZxOP3q6bVnXpS78EEXc3wKp7YB6m&steamid=";
            _votecheckApiUrl = "https://space-engineers.com/api/?object=votes&element=claim&key=kLQClxZxOP3q6bVnXpS78EEXc3wKp7YB6m&username=";
            _cooldownManager = new CooldownManager(TimeSpan.FromSeconds(15));
            // Initialize the timer with a 10-minute interval
            voteCheckTimer = new System.Timers.Timer
            {
                Interval = TimeSpan.FromMinutes(10).TotalMilliseconds,
                AutoReset = true // Set to true to automatically restart the timer after each interval
            };
            voteCheckTimer.Elapsed += async (sender, e) =>
            {
                // Call the vote check method when the timer elapses
                await CheckAndUpdateVoteTotals();
            };
            // Start the timer
            voteCheckTimer.Start();
        }
        

        // Method to check players' vote totals and update a local file
        private async Task CheckAndUpdateVoteTotals()
        {
            try
            {
                // Loop through each player
                List<IMyPlayer> players = new List<IMyPlayer>();
                MyAPIGateway.Players.GetPlayers(players);
                foreach (IMyPlayer player in players)
                {
                    ulong playerId = player.SteamUserId;

                    // Check if the player has been checked today
                    if (!lastCheckedTimes.ContainsKey(playerId) || (DateTime.Now - lastCheckedTimes[playerId]).TotalDays >= 1)
                    {
                        // Update the last checked time for the player
                        lastCheckedTimes[playerId] = DateTime.Now;
                        // Send a request to the voting API to get the vote total for the player
                        string url = $"{_votecheckApiUrl}{playerId}";
                        HttpResponseMessage response = await _httpClient.GetAsync(url);

                        if (response.IsSuccessStatusCode)
                        {
                            string responseBody = await response.Content.ReadAsStringAsync();


                            // Assuming the response body contains the vote total
                            // Modify accordingly if the actual response is different
                            if (responseBody == "1" || responseBody == "2")
                            {
                                voteTotals[playerId] += 1;
                            }
                        }
                        else
                        {
                            // Handle error checking vote total
                            // For now, let's assume if there's an error, the player hasn't voted
                            voteTotals[playerId] = 0;
                        }
                    }

                    // Save the vote totals to a local file
                    SaveVoteTotalsToFile(voteTotals);
                }
            }
            catch (Exception ex)
            {
                // Handle exceptions
                // For now, let's just log the exception
                Console.WriteLine($"Error checking and updating vote totals: {ex.Message}");
            }
        }

        private void SaveVoteTotalsToFile(Dictionary<ulong, int> voteTotals)
        {
            try
            {
                // Convert the vote totals dictionary to lines for writing to the file
                List<string> lines = new List<string>();
                foreach (var kvp in voteTotals)
                {
                    lines.Add($"{kvp.Key},{kvp.Value}");
                }

                // Write the lines to the file
                var voteFile = Path.Combine(MyFileSystem.UserDataPath, "VoteTotals.txt");
                File.WriteAllLines(voteFile, lines);
            }
            catch (Exception ex)
            {
                // Handle exceptions
                Console.WriteLine($"Error saving vote totals to file: {ex.Message}");
            }
        }

        private long GetPlayerIdentityIdByName(string playerName)
        {
            List<IMyPlayer> players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players);

            // Iterate through players to find the matching name
            foreach (IMyPlayer player in players)
            {
                if (player.DisplayName == playerName)
                {
                    // Get the identity ID of the player and return it
                    return MyAPIGateway.Players.TryGetIdentityId(player.SteamUserId);
                }
            }

            // Return 0 if the player with the given name is not found
            return 0;
        }

        public override void LoadData()
        {
            base.LoadData();
            // Load vote totals from file when the plugin is loaded
            LoadVoteTotalsFromFile();
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
            // Schedule daily vote total check
            _ = CheckAndUpdateVoteTotals();
            Log("Event listener working?");
        }
        /*private async void ScheduleDailyVoteTotalCheck()
        {
            while (true)
            {
                // Wait until next day
                DateTime nextDay = DateTime.Today.AddDays(1);
                TimeSpan waitTime = nextDay - DateTime.Now;
                await Task.Delay(waitTime);

                // Check and update vote totals
                await CheckAndUpdateVoteTotals();
            }
        }*/
        private void Log(string message)
        {
            Console.WriteLine(message); // Log to console

            try
            {
                // Specify the full path to the log file "NachoLog.txt"
                var logFile = Path.Combine(MyFileSystem.UserDataPath, "NachoLog.txt");

                // Append the message to the log file or create the file if it doesn't exist
                using (StreamWriter writer = File.AppendText(logFile))
                {
                    writer.WriteLine($"{DateTime.Now} - {message}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error writing to log file: {ex.Message}");
            }
        }
        
        // Method to get player's username from Steam ID
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

            // Player not found or not loaded
            return null;
        }

        // Method to get player's username from Steam ID
        private bool GetPlayerBalance(string identity, out long balance)
        {
            List<IMyPlayer> players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players);

            foreach (IMyPlayer player in players)
            {

                if (player.DisplayName == identity)
                {
                    return player.TryGetBalanceInfo(out balance);
                }
            }

            balance = 0;

            // Player not found or not loaded
            return false;
        }

        // Method to load vote totals from "VoteTotals.txt" file
        private void LoadVoteTotalsFromFile()
        {
            try
            {
                // Read the contents of the "VoteTotals.txt" file
                string[] lines = File.ReadAllLines("VoteTotals.txt");

                // Parse the contents of the file and update the vote totals dictionary
                foreach (string line in lines)
                {
                    string[] parts = line.Split(',');
                    if (parts.Length == 2 && ulong.TryParse(parts[0], out ulong playerId) && int.TryParse(parts[1], out int voteCount))
                    {
                        voteTotals[playerId] = voteCount;
                    }
                }
            }
            catch (Exception ex)
            {
                // Handle exceptions
                Console.WriteLine($"Error loading vote totals from file: {ex.Message}");
            }
        }

        private async void OnMessageEntered(ulong sender, string messageText)
        {
            // Instantiate CommandHandler
            CommandHandler commandHandler = new CommandHandler();

            // Get player promotions
            PromotionLevel playerPromotionLevel = (PromotionLevel)commandHandler.GetPlayerPromotionLevel(sender);

            // Split the message text by spaces to extract the command
            string[] messageParts = messageText.Split(' ');

            // Check if the message starts with "!" and has at least one part after splitting
            if (messageText.StartsWith("!"))
            {
                // Extract the command from the first part after splitting
                string command = messageParts[0].Trim().Substring(1).ToLower();


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
                            case "vote check":
                                HandleVoteCheck(sender);
                                break;
                            case "test":
                                if (messageParts.Length >= 3)
                                {
                                    string setting = messageParts[1].Trim();
                                    string param = string.Join(" ", messageParts.Skip(2)).Trim();
                                    TestArea(setting, param);
                                }
                                else
                                {
                                    Log("Insufficient parameters for changing command");
                                }

                                break;
                            case "pay":
                                // Check if there are enough parameters for the pay command
                                if (messageParts.Length >= 3)
                                {
                                    string recipient = messageParts[1].Trim();
                                    if (int.TryParse(messageParts[2].Trim(), out int amount))
                                    {
                                        HandlePayCommand(sender, recipient, amount);
                                    }
                                    else
                                    {
                                        Log($"Invalid amount specified for pay command: {messageParts[2].Trim()}");
                                    }
                                }
                                else
                                {
                                    Log("Insufficient parameters for pay command");
                                }
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
                    Log($"{sender} is on cooldown");
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
            Log("Motd Command Detected");

            // Send a message of the day
            //this code here is how we do things
            string motd = Plugin.Instance.Config.Motd;
            MyAPIGateway.Utilities.SendMessage($"{motd}");

        }

        private void HandleDiscordCommand()
        {
            // Send the Discord invite link to the player who entered the command
            MyAPIGateway.Utilities.SendMessage("Join our Discord server at: discord.gg/vnAt8X64ut");
            Log("Discord Command Detected");
        }
        private bool _isVoteInProgress = false;
        private async Task HandleVoteCommand(ulong sender)
        {
            try
            {
                // Check if a vote request is already in progress
                if (_isVoteInProgress)
                {
                    MyAPIGateway.Utilities.SendMessage("A vote request is already in progress. Please wait a moment and try again.");
                    return;
                }

                // Set the flag to indicate that a vote request is in progress
                _isVoteInProgress = true;

                // Send a request to the voting API to check if the player has voted
                string url = $"{_votingApiUrl}{sender}";
                HttpResponseMessage response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    string responseBody = await response.Content.ReadAsStringAsync();
                    if (responseBody.Contains("1"))
                    {
                        MyAPIGateway.Utilities.SendMessage("You have voted and are ready to claim!");
                        Log($"{sender} is ready to claim");
                    }
                    else if (responseBody.Contains("0"))
                    {
                        MyAPIGateway.Utilities.SendMessage("You haven't voted yet! Check out www.space-engineers.com");
                        Log($"{sender} is ready to vote");
                    }
                    else if (responseBody.Contains("2"))
                    {
                        MyAPIGateway.Utilities.SendMessage("You have already claimed today!");
                        Log($"{sender} has claimed today");
                    }
                }
                else
                {
                    MyAPIGateway.Utilities.SendMessage("Error checking voting status.");
                    Log($"{sender} has encountered an error with checking his daily vote");
                }
            }
            catch (Exception ex)
            {
                MyAPIGateway.Utilities.SendMessage($"Error checking voting status: {ex.Message}");
            }
            finally
            {
                // Reset the flag once the vote request is complete
                _isVoteInProgress = false;
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
                        Log($"{sender} claimed reward!");
                        long target = MyAPIGateway.Players.TryGetIdentityId(sender);
                        MyAPIGateway.Players.RequestChangeBalance(target, Plugin.Instance.Config.Reward);

                    }
                    else
                    {
                        MyAPIGateway.Utilities.SendMessage("You haven't voted yet!");
                        Log("User didn't vote");
                    }

                }
                else
                {
                    MyAPIGateway.Utilities.SendMessage("Error claiming vote reward.");
                    Log("Error claiming");
                }
            }
            catch (Exception ex)
            {
                MyAPIGateway.Utilities.SendMessage($"Error claiming vote reward: {ex.Message}");
                Log("error! this one you gotta tell nacho about");
            }
        }
        private void HandlePayCommand(ulong sender, string recipient, int amount)
        {
            // Get the identity ID of the sender
            long senderIdentityId = MyAPIGateway.Players.TryGetIdentityId(sender);
            string senderBank = GetPlayerNameFromSteamId(sender);

            // Check if the sender's identity ID is valid
            if (senderIdentityId == 0)
            {
                Log($"Invalid sender: {sender}");
                return;
            }

            if (!GetPlayerBalance(senderBank, out long senderBalance))
            {
                Log($"Failed to get balance for sender: {GetPlayerNameFromSteamId(sender)}");
                return;
            }

            // Check if the sender has sufficient balance
            if (senderBalance < amount)
            {
                Log($"Insufficient balance for sender: {GetPlayerNameFromSteamId(sender)}");
                return;
            }


            // Get the identity ID of the recipient
            long recipientIdentityId = GetPlayerIdentityIdByName(recipient);


            // Check if the recipient's identity ID is valid
            if (recipientIdentityId == 0)
            {
                Log($"Invalid recipient: {recipient}");
                return;
            }

            MyAPIGateway.Players.RequestChangeBalance(senderIdentityId, -amount);
            MyAPIGateway.Players.RequestChangeBalance(recipientIdentityId, amount);


            // Log the transaction
            Log($"{senderBank} transferred {amount} to {recipient}");
        }
        
        private void HandleVoteCheck(ulong sender)
        {
            // Get the username associated with the sender's Steam ID
            string playerName = GetPlayerNameFromSteamId(sender);

            if (playerName != null)
            {
                // Get the vote total for the sender
                int voteTotal = 0;
                if (VoteTotals.ContainsKey(sender))
                {
                    voteTotal = VoteTotals[sender];
                }

                // Print the username and vote total
                Log($"Player: {playerName}, Vote Total: {voteTotal}");
            }
            else
            {
                // Unable to retrieve username
                Log("Unable to retrieve username for sender.");
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
            //PluginConfig pluginConfig = new PluginConfig();
            long target = MyAPIGateway.Players.TryGetIdentityId(sender);
            if (target != MyAPIGateway.Players.TryGetIdentityId(steamId: Plugin.Instance.Config.Admin))
            {
                MyAPIGateway.Players.RequestChangeBalance(target, -500);
                MyAPIGateway.Utilities.SendMessage("oh ho ho no.");
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
                //Log($"Entity Type: {entity.GetType().Name}");

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
                            //Log($"Scrap Beacon found: {cubeBlock.EntityId}");
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
            MyAPIGateway.Utilities.SendMessage($"Grids found: {grids.Count}");
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

        private void TestArea(string configsetting, string param)
        {
            string test1 = Plugin.Instance.Config.Motd;

            Log($"{test1}");
            //this is what i need
            Plugin.Instance.Config.Motd = param;
            Log($"{test1}");
            test1 = param;

            Log($"{test1}");

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
        { "omnomnom", PromotionLevel.Admin },
        { "pay", PromotionLevel.Default }
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