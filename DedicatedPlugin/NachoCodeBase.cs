using System;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Timers;
using VRageMath;
using VRage.ModAPI;
using Shared.Config;
using System.IO;
using VRage.FileSystem;
using Sandbox.Game.World;
using VRage.Scripting;
using Shared.Logging;
using VRage.Utils;
using System.Linq;
using Epic.OnlineServices.Sanctions;
using Sandbox.Game.Entities;
using VRage.Game;
using Epic.OnlineServices;
using VRage.ObjectBuilders;
using VRage;

// Define the promotion levels
public enum PromotionLevel
{
    Default,
    Admin
}

namespace DedicatedPlugin
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
        public Dictionary<ulong, DateTime> lastCheckedTimes = new Dictionary<ulong, DateTime>();

        public readonly HttpClient _httpClient;
        public readonly string _votingApiUrl;
        public readonly string _claimApiUrl;
        public readonly string _votecheckApiUrl;
        public readonly CooldownManager _cooldownManager;
        public readonly TimeSpan _initialCooldownDuration = TimeSpan.FromSeconds(15);
        public string stringPath = Path.Combine(MyFileSystem.UserDataPath, "RandomStrings.txt");
        public static Queue<string> RandomStringQueue = new Queue<string>();
        public static List<string> RandomStrings = new List<string>();
        public static readonly Random RandomGenerator = new Random();
        //this line below this is the OnTimerElapsed beginning, right now with it commented and the Timer.Elapsed += OnTimerElapsed; its disabled, uncomment to re-enable
        //public static readonly Timer Timer = new Timer(TimeSpan.FromMinutes(15).TotalMilliseconds);
        


        public NachoPlugin()
        {
            try
            {
                // Ensure the file exists and load random strings
                EnsureFileExists(stringPath);
                LoadRandomStrings(stringPath);
                ShuffleRandomStrings();

                // Populate the queue with shuffled strings
                foreach (var str in RandomStrings)
                {
                    RandomStringQueue.Enqueue(str);
                }

                // Load existing vote totals from the file
                LoadVoteTotalsFromFile();

                //Timer.Elapsed += OnTimerElapsed;
                //Timer.AutoReset = true;
                //Timer.Start();

                _httpClient = new HttpClient();
                _votingApiUrl = "https://space-engineers.com/api/?object=votes&element=claim&key=kLQClxZxOP3q6bVnXpS78EEXc3wKp7YB6m&steamid="; // Replace with actual voting API URL
                _claimApiUrl = "https://space-engineers.com/api/?action=post&object=votes&element=claim&key=kLQClxZxOP3q6bVnXpS78EEXc3wKp7YB6m&steamid=";
                _votecheckApiUrl = "https://space-engineers.com/api/?object=votes&element=claim&key=kLQClxZxOP3q6bVnXpS78EEXc3wKp7YB6m&steamid=";

                _cooldownManager = new CooldownManager(_initialCooldownDuration);
                
            }
            catch (Exception ex)
            {
                // Handle exceptions
                Console.WriteLine($"Error initializing NachoPlugin: {ex.Message}");
            }
        }
        
        // Method to update the cooldown duration
        public void UpdateCooldownDuration(TimeSpan newDuration)
        {
            // Update the cooldown duration for the CooldownManager
            _cooldownManager.CooldownDuration = newDuration;
        }

        // Initialize dictionary to track if a player has received a response of 2 today
        Dictionary<ulong, bool> response2ReceivedToday = new Dictionary<ulong, bool>();
        private DateTime lastResetDate = DateTime.Today;

        // Method to check players' vote totals and update a local file
        // Dictionary to track the number of checks since the last '2' response or new addition
        Dictionary<ulong, int> checkCountSinceLastTwo = new Dictionary<ulong, int>();

        public async Task CheckAndUpdateVoteTotals()
        {
            Log("Checking votes");
            try
            {
                // Reset response2ReceivedToday if it's a new day
                if (DateTime.Today > lastResetDate)
                {
                    response2ReceivedToday.Clear();
                    lastResetDate = DateTime.Today;
                }

                List<IMyPlayer> players = new List<IMyPlayer>();
                MyAPIGateway.Players.GetPlayers(players);
                bool hasUpdates = false; // Track if any updates happen to voteTotals

                foreach (IMyPlayer player in players)
                {
                    ulong playerId = player.SteamUserId;
                    // Initialize check count if the player is new
                    if (!checkCountSinceLastTwo.ContainsKey(playerId))
                        checkCountSinceLastTwo[playerId] = 0;

                    // Increment check count for every player on each cycle
                    checkCountSinceLastTwo[playerId]++;

                    // Only process the check if enough attempts have passed or if it's the first time today
                    if (!lastCheckedTimes.ContainsKey(playerId) || (DateTime.Now - lastCheckedTimes[playerId]).TotalDays >= 1 || checkCountSinceLastTwo[playerId] >= 15)
                    {
                        lastCheckedTimes[playerId] = DateTime.Now;
                        string url = $"{_votecheckApiUrl}{playerId}";
                        HttpResponseMessage response = await _httpClient.GetAsync(url);
                        Log("Sending Request for player " + playerId);

                        if (response.IsSuccessStatusCode)
                        {
                            string responseBody = await response.Content.ReadAsStringAsync();
                            Log($"Response for {playerId}: {responseBody}");

                            if (responseBody == "2")
                            {
                                if (!response2ReceivedToday.ContainsKey(playerId) || !response2ReceivedToday[playerId])
                                {
                                    voteTotals[playerId] = voteTotals.ContainsKey(playerId) ? voteTotals[playerId] + 1 : 1;
                                    response2ReceivedToday[playerId] = true;
                                    checkCountSinceLastTwo[playerId] = 0; // Reset the check count on success
                                    hasUpdates = true;
                                    Log($"Vote total updated for {playerId}: {voteTotals[playerId]}");
                                }
                            }
                            if (responseBody == "1")
                            {
                                Log($"{playerId} needs to claim their vote to recieve points");
                            }
                            else
                            {
                                Log($"Player {playerId} did not receive a '2' response or already confirmed today.");
                            }
                        }
                        else
                        {
                            Log($"Request failed for {playerId} with status: {response.StatusCode}");
                        }
                    }
                }

                // Save only if there were updates
                if (hasUpdates)
                {
                    Log("Saving updated vote totals.");
                    SaveVoteTotalsToFile(voteTotals);
                }
                else
                {
                    Log("No changes to vote totals, no save needed.");
                }
            }
            catch (Exception ex)
            {
                Log($"Error checking and updating vote totals: {ex.Message}");
            }
        }


        public void SaveVoteTotalsToFile(Dictionary<ulong, int> voteTotals)
        {
            try
            {
                // Ensure the path to the vote totals file
                var voteFile = Path.Combine(MyFileSystem.UserDataPath, "VoteTotals.txt");
                // Read existing lines if the file exists, otherwise start with an empty array
                var existingLines = File.Exists(voteFile) ? File.ReadAllLines(voteFile) : new string[0];
                Dictionary<ulong, int> existingTotals = new Dictionary<ulong, int>();

                // Parse existing lines into the dictionary
                foreach (var line in existingLines)
                {
                    var parts = line.Split(',');
                    if (parts.Length == 2 && ulong.TryParse(parts[0], out ulong key) && int.TryParse(parts[1], out int value))
                    {
                        existingTotals[key] = value;
                    }
                }

                // Update existing totals or add new ones
                foreach (var kvp in voteTotals)
                {
                    if (existingTotals.ContainsKey(kvp.Key))
                        existingTotals[kvp.Key] = kvp.Value;
                    else
                        existingTotals.Add(kvp.Key, kvp.Value);
                }

                // Prepare updated lines for writing to file
                List<string> updatedLines = existingTotals.Select(kvp => $"{kvp.Key},{kvp.Value}").ToList();
                // Write the updated lines to the file
                File.WriteAllLines(voteFile, updatedLines);
            }
            catch (Exception ex)
            {
                // Log errors
                Console.WriteLine($"Error saving vote totals to file: {ex.Message}");
            }
        }

        public long GetPlayerIdentityIdByName(string playerName)
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
            //Timer.Stop();
            //Timer.Dispose();
            voteCheckTimer?.Stop();
            voteCheckTimer?.Dispose();
            Log("NachoPlugin has been unloaded!");
        }
        public static void EnsureFileExists(string filePath)
        {
            if (!File.Exists(filePath))
            {
                // Create the file if it doesn't exist
                File.WriteAllLines(filePath, new[] { "Hello", "World", "How are you?", "Good morning!" });
            }
        }

        public static void LoadRandomStrings(string filePath)
        {
            try
            {
                // Read the random strings from the file
                RandomStrings = new List<string>(File.ReadAllLines(filePath));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading random strings from file: {ex.Message}");
            }
        }
        public override void BeforeStart()
        {
            base.BeforeStart();
            MyAPIGateway.Utilities.MessageRecieved += OnMessageEntered;
            Log("Event listener working?");
            voteCheckTimer = new System.Timers.Timer
            {
                Interval = TimeSpan.FromSeconds(600).TotalMilliseconds,
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

        public bool isProcessing = false;
        public void OnTimerElapsed(object sender, ElapsedEventArgs e)
        {
            // Check if the queue is empty and reshuffle the strings
            if (RandomStringQueue.Count == 0)
            {
                ShuffleRandomStrings();
                // Populate the queue with shuffled strings again
                foreach (var str in RandomStrings)
                {
                    RandomStringQueue.Enqueue(str);
                }
            }

            // Dequeue a string from the queue and log it
            string randomString = RandomStringQueue.Dequeue();
            MyAPIGateway.Utilities.SendMessage(randomString);
            
            Log(randomString);
        }

        public static void ShuffleRandomStrings()
        {
            // Shuffle the list of random strings
            RandomStrings = RandomStrings.OrderBy(x => RandomGenerator.Next()).ToList();
        }

        public static void Log(string message)
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
        public string GetPlayerNameFromSteamId(ulong steamId)
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

        public ulong GetSteamIdFromPlayerName(string target)
        {
            List<IMyPlayer> players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players);

            foreach (IMyPlayer player in players)
            {
                if (player.DisplayName == target)
                {
                    return player.SteamUserId;
                }
            }

            // Player not found or not loaded
            return 0;
        }

        // Method to get player's username from Steam ID
        public bool GetPlayerBalance(string identity, out long balance)
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
        public void LoadVoteTotalsFromFile()
        {
            try
            {
                // Read the contents of the "VoteTotals.txt" file
                var voteFile = Path.Combine(MyFileSystem.UserDataPath, "VoteTotals.txt");
                string[] lines = File.ReadAllLines(voteFile);

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

        public IMyPlayer GetPlayerBySteamId(ulong steamId)
        {
            List<IMyPlayer> players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players);

            foreach (IMyPlayer player in players)
            {
                if (player.SteamUserId == steamId)
                {
                    return player;
                }
            }

            // Player not found or not loaded
            return null;
        }


        public void GivePlayerItem(ulong senderSteamId, string itemType, string itemSubtype, string amountString)
        {

            // Convert the amount from string to long
            if (!long.TryParse(amountString, out long amountLong))
            {
                Log($"Invalid amount format: {amountString}");
                return;
            }

            // Convert long to MyFixedPoint
            MyFixedPoint amount = (MyFixedPoint)(double)amountLong;


            // Get the player by Steam ID using the provided method
            IMyPlayer senderPlayer = GetPlayerBySteamId(senderSteamId);
            if (senderPlayer == null)
            {
                Log($"Player not found: {senderSteamId}");
                return;
            }

            // Get the character entity controlled by the player
            var controlledEntity = senderPlayer.Controller.ControlledEntity as IMyCharacter;
            if (controlledEntity == null)
            {
                Log($"Player is not controlling a character: {senderSteamId}");
                return;
            }

            // Get the player's inventory
            var inventory = controlledEntity.GetInventory() as IMyInventory;
            if (inventory == null)
            {
                Log($"No inventory found for player: {senderSteamId}");
                return;
            }

            // Create the item to be added
            MyObjectBuilder_Base itemBuilder = MyObjectBuilderSerializer.CreateNewObject(new MyObjectBuilderType(typeof(MyObjectBuilder_Component)), itemSubtype);
            if (itemBuilder == null)
            {
                Log($"Invalid item type or subtype: {itemType}, {itemSubtype}");
                return;
            }

            // Create a physical item object
            var physicalItem = itemBuilder as MyObjectBuilder_PhysicalObject;
            if (physicalItem == null)
            {
                Log($"Could not create physical item: {itemSubtype}");
                return;
            }

            // Add the item to the player's inventory
            inventory.AddItems(amount, physicalItem);

            Log($"Given {amount} of {itemSubtype} to player: {senderSteamId}");
        }

        public async void OnMessageEntered(ulong sender, string messageText)
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
                            case "turnoffpb":
                                HandleTurnOffPBCommand();
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
                            case "dur":
                                HandleDurCommand(sender);
                                break;
                            case "ban":
                                if (messageParts.Length > 2)
                                {
                                    string target = messageParts[1].Trim();
                                    HandleBanCommand(sender, target);
                                }
                                else
                                {
                                    Log("Usage: !ban *username*");
                                    MyAPIGateway.Utilities.SendMessage("Usage: !ban *username*");
                                }
                                break;
                            case "omnomnom":
                                ScanAndDeleteOutOfRangeGrids();
                                break;
                            case "votecheck":
                                HandleVoteCheck(sender);
                                break;
                            case "test":
                                if (messageParts.Length > 3)
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
                                    MyAPIGateway.Utilities.SendMessage("Usage: !pay *username* *amount*");
                                }
                                break;
                            case "help":
                            case "commands":
                                // Get the available commands for the sender
                                string availableCommands = GetAvailableCommands(sender);
                                // Log the available commands
                                Log(availableCommands);
                                MyAPIGateway.Utilities.SendMessage(availableCommands);
                                break;
                            case "flex":
                                HandleFlexCommand(sender);
                                break;
                            case "1%":
                                HandleHiScoreCommand();
                                break;
                            case "random":
                                HandleRandomCommand();
                                break;
                            case "rerandom":
                                LoadRandomStrings(stringPath);
                                ShuffleRandomStrings();
                                RandomStringQueue.Clear();
                                foreach (var str in RandomStrings)
                                {
                                    RandomStringQueue.Enqueue(str);
                                }
                                break;
                            case "cooldown":
                                UpdateCooldownDuration(TimeSpan.FromSeconds(Plugin.Instance.Config.Cooldown));
                                break;
                            case "updatevote":
                                await CheckAndUpdateVoteTotals();
                                break;
                            case "grids":
                                HandleGridsCommand();
                                break;
                            case "power":
                                HandlePowerCommand(sender);
                                break;
                            case "giveitem":
                                if (messageParts.Length > 3)
                                {
                                    GivePlayerItem(GetSteamIdFromPlayerName(messageParts[1]), "MyObjectBuilder_Component", messageParts[2], messageParts[3]);
                                }
                                else
                                {
                                    Log($"Not enough Commands for GiveItem:{sender}");
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
        public void HandlePowerCommand(ulong senderSteamId)
        {
            int PowerCommandCost = Plugin.Instance.Config.PowerCost;
            // Get the identity ID of the sender
            long senderIdentityId = MyAPIGateway.Players.TryGetIdentityId(senderSteamId);
            string senderName = GetPlayerNameFromSteamId(senderSteamId);

            // Check if the sender's identity ID is valid
            if (senderIdentityId == 0)
            {
                Log($"Invalid sender: {senderSteamId}");
                return;
            }

            if (senderName == null)
            {
                Log($"Player not found: {senderSteamId}");
                return;
            }

            if (!GetPlayerBalance(senderName, out long senderBalance))
            {
                Log($"Failed to get balance for sender: {senderName}");
                return;
            }

            // Check if the sender has sufficient balance
            if (senderBalance < PowerCommandCost)
            {
                Log($"Insufficient balance for sender: {senderName}");
                Console.WriteLine("Not enough money honey");
                return;
            }

            IMyPlayer senderPlayer = GetPlayerBySteamId(senderSteamId);
            if (senderPlayer == null)
            {
                Log($"Player not found: {senderName}");
                return;
            }

            var controlledEntity = senderPlayer.Controller.ControlledEntity;

            // Check if the player is controlling a character or ship
            if (controlledEntity == null)
            {
                Log($"Player is not controlling any entity: {senderName}");
                return;
            }

            IMyCubeGrid grid = null;

            if (controlledEntity is IMyCharacter character)
            {
                IMyShipController shipController = character.Parent as IMyShipController;
                if (shipController != null)
                {
                    grid = shipController.CubeGrid;
                }
            }
            else if (controlledEntity is IMyShipController shipController)
            {
                grid = shipController.CubeGrid;
            }

            if (grid == null)
            {
                Log($"Player is not controlling a ship grid: {senderName}");
                return;
            }

            // Check if the grid is a small grid
            if (grid.GridSizeEnum != MyCubeSize.Small)
            {
                Log($"Player is not controlling a small grid: {senderName}");
                MyAPIGateway.Utilities.SendMessage("Small grids only");
                return;
            }

            List<IMyBatteryBlock> batteries = new List<IMyBatteryBlock>();
            MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid).GetBlocksOfType(batteries);

            if (batteries.Count == 0)
            {
                Log($"No batteries found on the grid: {senderName}");
                MyAPIGateway.Utilities.SendMessage("No valid batteries found");
                return;
            }

            // Deduct the balance
            MyAPIGateway.Players.RequestChangeBalance(senderIdentityId, -PowerCommandCost);

            // Set the first battery to max power
            MyBatteryBlock battery = (MyBatteryBlock)batteries[0];
            battery.CurrentStoredPower = battery.MaxStoredPower;

            Log($"Battery set to max power for player: {senderName}. 10 million credits deducted.");
            MyAPIGateway.Utilities.SendMessage("Juiced up");
        }
        public void HandleRandomCommand()
        {
            // Call the method to manually trigger the timer event and advance to the next random string
            OnTimerElapsed(null, null);
        }
        // Method to handle turning off all programmable blocks
        public void HandleTurnOffPBCommand()
        {
            HashSet<IMyEntity> entities = new HashSet<IMyEntity>();
            MyAPIGateway.Entities.GetEntities(entities, e => e is IMyCubeGrid);

            int turnedOffCount = 0;

            foreach (IMyCubeGrid grid in entities)
            {
                IMyGridTerminalSystem terminalSystem = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid);
                if (terminalSystem != null)
                {
                    List<IMyProgrammableBlock> programmableBlocks = new List<IMyProgrammableBlock>();
                    terminalSystem.GetBlocksOfType(programmableBlocks);

                    foreach (IMyProgrammableBlock programmableBlock in programmableBlocks)
                    {
                        programmableBlock.Enabled = false;
                        turnedOffCount++;
                    }
                }
            }

            MyAPIGateway.Utilities.SendMessage($"Turned off {turnedOffCount} programmable blocks.");
            Log($"Turned off {turnedOffCount} programmable blocks.");
        }
        public void HandleHiScoreCommand()
        {
            List<IMyPlayer> players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players);
            foreach (IMyPlayer player in players)
            {
                if (player != null)
                {
                    player.TryGetBalanceInfo(out long cash);
                    Log(player.DisplayName + cash.ToString());
                    MyAPIGateway.Utilities.SendMessage($"{player.DisplayName} : {cash}");
                }
            }

        }

        public void HandleFlexCommand(ulong sender)
        {
            string senderID = GetPlayerNameFromSteamId(sender);
            _ = GetPlayerBalance(senderID, out long senderCash);
            Log(senderID + senderCash);
            MyAPIGateway.Utilities.SendMessage($"{senderID}: {senderCash}");
        }

        // Method to get the available commands for the sender
        public string GetAvailableCommands(ulong sender)
        {
            CommandHandler commandHandler = new CommandHandler();
            // Get the promotion level of the sender
            PromotionLevel senderPerm = (PromotionLevel)commandHandler.GetPlayerPromotionLevel(sender);

            // List to store available commands
            List<string> availableCommands = new List<string>();

            // Iterate through commands and check permissions
            foreach (var command in CommandHandler.commandPermissions)
            {
                if (senderPerm >= command.Value)
                {
                    availableCommands.Add(command.Key);
                }
            }

            // Construct message containing available commands
            string message = "Available commands:\n";
            foreach (var cmd in availableCommands)
            {
                message += $"!{cmd} ";
            }

            return message;
        }


        public void HandlePlayersCommand()
        {
            // Get the number of players online
            long currentPlayerCount = MyAPIGateway.Players.Count;
            Log("Player Command Detected");
            MyAPIGateway.Utilities.SendMessage($"Current number of players online: {currentPlayerCount}");
        }

        public void HandleMotdCommand()
        {
            Log("Motd Command Detected");

            // Send a message of the day
            //this code here is how we do things, THIS CALLS THE VAR STORED IN THE FILE, CANNOT CALL THIS WAY UNLESS SERVER IS RUNNING, OTHERWISE THIS CODE-LINK IS NULL
            //so just honestly don't use it in initializers or in the "Before, After, or Load Data" methods.
            string motd = Plugin.Instance.Config.Motd;
            MyAPIGateway.Utilities.SendMessage($"{motd}");

        }

        public void HandleDiscordCommand()
        {
            // Send the Discord invite link to the player who entered the command
            MyAPIGateway.Utilities.SendMessage("Join our Discord server at: discord.gg/vnAt8X64ut");
            Log("Discord Command Detected");
        }
        public bool _isVoteInProgress = false;
        public async Task HandleVoteCommand(ulong sender)
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
        public async Task HandleVoteClaimCommand(ulong sender)
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
                        GivePlayerItem(sender, "MyObjectBuilder_Component", "PowerCell", "10");

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
        public void HandlePayCommand(ulong sender, string recipient, int amount)
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

        public void HandleVoteCheck(ulong sender)
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

        public void HandleBanCommand(ulong sender, string target)
        {
            ulong bannedPlayer = GetSteamIdFromPlayerName(target);

            if (bannedPlayer != 0)
            {
                MyAPIGateway.Utilities.ConfigDedicated.Banned.Add(bannedPlayer);
                Log($"{target} has been banned from the server");
                MyAPIGateway.Utilities.SendMessage($"{target} has been banned! ");
            }
            else
            {
                Log("Target was not found");
                MyAPIGateway.Utilities.SendMessage($"{target} was not found ");
            }
        }

        public void HandleDurCommand(ulong sender)
        {
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
        public void ScanAndDeleteOutOfRangeGrids()
        {

            // Get all entities in the world
            HashSet<IMyEntity> entities = new HashSet<IMyEntity>();
            MyAPIGateway.Entities.GetEntities(entities);
            Log($"{entities.Count} entities");

            // Dictionary to count entity types
            Dictionary<string, int> entityTypeCounts = new Dictionary<string, int>();

            // Find all Scrap Beacon blocks
            List<IMyCubeBlock> scrapBeacons = new List<IMyCubeBlock>();
            foreach (IMyEntity entity in entities)
            {
                // Increment count for each entity type
                string entityType = entity.GetType().Name;
                if (!entityTypeCounts.ContainsKey(entityType))
                    entityTypeCounts[entityType] = 0;
                entityTypeCounts[entityType]++;

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


                }
                else
                {
                    Log("No Beacons Found");
                }
            }

            // Log the count of each entity type
            foreach (var pair in entityTypeCounts)
            {
                Log($"Found {pair.Value} entities of type {pair.Key}");
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

        public bool IsScrapBeacon(IMyCubeBlock block)
        {
            //Log("IS THIS BEING CHECKED?");
            // Check if the block is a Scrap Beacon block based on its subtype ID or block type ID
            // Replace "ScrapBeaconSubtypeId" with the actual subtype ID or block type ID of the Scrap Beacon block
            return block.BlockDefinition.SubtypeId == "LargeBlockScrapBeacon" ||
                 block.BlockDefinition.SubtypeId == "SmallBlockScrapBeacon";
        }

        public bool IsGridWithinRangeOfScrapBeacon(IMyCubeGrid grid, List<IMyCubeBlock> scrapBeacons)
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

        public void DeleteGrid(IMyCubeGrid grid)
        {
            // Delete the entire grid
            grid.Close();
        }
        // New Method to Handle !grids Command
        public void HandleGridsCommand()
        {
            HashSet<IMyEntity> entities = new HashSet<IMyEntity>();
            MyAPIGateway.Entities.GetEntities(entities, e => e is IMyCubeGrid);

            int gridCount = entities.Count;
            MyAPIGateway.Utilities.SendMessage($"Total number of grids: {gridCount}");
            Log($"Total number of grids: {gridCount}");
        }

        public void TestArea(string configsetting, string param)
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

        public readonly Dictionary<(ulong, string), DateTime> _cooldowns = new Dictionary<(ulong, string), DateTime>();
        public TimeSpan _cooldownDuration;


        public CooldownManager(TimeSpan cooldownDuration)
        {
            _cooldownDuration = cooldownDuration;
        }

        // Property to get or set the cooldown duration
        public TimeSpan CooldownDuration
        {
            get { return _cooldownDuration; }
            set { _cooldownDuration = value; }
        }


        public bool CanUseCommand(ulong sender, string command)
        {
            if (!_cooldowns.ContainsKey((sender, command)))
            {
                return true;
            }

            DateTime lastUsedTime = _cooldowns[(sender, command)];
            return DateTime.Now - lastUsedTime >= CooldownDuration;
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
        public readonly static Dictionary<string, PromotionLevel> commandPermissions = new Dictionary<string, PromotionLevel>
    {
        { "players", PromotionLevel.Default },
        { "motd", PromotionLevel.Default },
        { "discord", PromotionLevel.Default },
        { "vote", PromotionLevel.Default },
        { "voteclaim", PromotionLevel.Default },
        { "votecheck", PromotionLevel.Default },
        { "ban", PromotionLevel.Admin },
        { "omnomnom", PromotionLevel.Admin },
        { "pay", PromotionLevel.Default },
        { "test", PromotionLevel.Admin },
        { "dur", PromotionLevel.Default },
        { "turnoffpb", PromotionLevel.Admin },
        { "grids", PromotionLevel.Default },
        { "flex", PromotionLevel.Default },
        { "power", PromotionLevel.Admin },
        { "giveitem", PromotionLevel.Admin }

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
        public string GetPlayerNameFromSteamId(ulong steamId)
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
        public IMyPlayer GetPlayerByName(string playerName)
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