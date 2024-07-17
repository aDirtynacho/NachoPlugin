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
using System.IO;
using VRage.FileSystem;
using System.Linq;
using Sandbox.Game.Entities;
using VRage.Game;
using VRage.ObjectBuilders;
using VRage;
using Sandbox.Definitions;
using System.Text;
using System.Security.Cryptography.X509Certificates;
using System.Configuration;
using System.Runtime.InteropServices;
using VRage.Voxels;
using VRage.Game.ObjectBuilders.Components;
using VRage.Game.ObjectBuilders.Definitions;
using Sandbox.Game.Entities.Blocks;

// Define the promotion levels
public enum PromotionLevel
{
    Default,
    Admin
}

namespace NachoPluginSystem
{
    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate, 500)]

    public class NachoPlugin : MySessionComponentBase
    {
        // Define a timer to trigger the vote check every 10 minutes
        private System.Timers.Timer voteCheckTimer;
        private System.Timers.Timer cleanupTimer;
        public static bool IsInitialized { get; private set; } = false;
        // Dictionary to store vote totals for each player
        Dictionary<ulong, int> voteTotals = new Dictionary<ulong, int>();

        // Property to access voteTotals
        public Dictionary<ulong, int> VoteTotals
        {
            get { return voteTotals; }
        }
        // Dictionary to store the last checked time for each player
        public Dictionary<ulong, DateTime> lastCheckedTimes = new Dictionary<ulong, DateTime>();
        public HttpClient _httpClient;
        public string _votingApiUrl;
        public string _claimApiUrl;
        public string _votecheckApiUrl;
        public CooldownManager _cooldownManager;
        public TimeSpan _initialCooldownDuration = TimeSpan.FromSeconds(15);
        public string stringPath = Path.Combine(MyFileSystem.UserDataPath, "RandomStrings.txt");
        public static Queue<string> RandomStringQueue = new Queue<string>();
        public static List<string> RandomStrings = new List<string>();
        public static readonly Random RandomGenerator = new Random();
        //this line below this is the OnTimerElapsed beginning, right now with it commented and the Timer.Elapsed += OnTimerElapsed; its disabled, uncomment to re-enable
        //Additionally, i've learned this is just to construct it, i can call a true configuration of it later in InitializeConfiguration() since its tied to the event OnSessionReady
        //And that will allow "Plugin.Instance.Config.*" to return properly
        public static readonly Timer Timer = new Timer(TimeSpan.FromMinutes(15).TotalMilliseconds);
        public const ushort MESSAGE_ID = 22345;
        public static Timer reboot;
        // Flags to track initialization
        //If you set these to true it will skip those, recommend only disabling, i.e setting to "true", on _configurationInitialized, at this time at 6/4/2024, i haven't tested the others.
        public bool _fileOperationsInitialized = false;
        public bool _queuePopulated = false;
        public bool _voteTotalsLoaded = false;
        public bool _httpClientInitialized = false;
        public bool _cooldownManagerInitialized = false;
        public bool _configurationInitialized = false;
        public int viewdistance = 0;
        public NachoTracker nachoTracker = new NachoTracker();
        public ServerShopHandler shopHandler = new ServerShopHandler();
        private static readonly MyDefinitionId Electricity = new MyDefinitionId(typeof(MyObjectBuilder_GasProperties), "Electricity");
        public NachoPlugin()
        {
            try
            {
                InitializeFileOperations();
                PopulateQueue();
                LoadVoteTotals();
                InitializeHttpClient();
                InitializeCooldownManager();

            }
            catch (Exception ex)
            {
                // Handle exceptions
                
                Console.WriteLine($"Error initializing NachoPlugin: {ex.Source},{ex.Message},{ex.TargetSite},{ex.InnerException}");
            }
        }

        public void InitializeConfiguration()
        {
            if (!_configurationInitialized)
            {
                
                try
                {
                    viewdistance = nachoTracker.clients.ViewDistance;
                    cleanupTimer.Interval = TimeSpan.FromHours(Plugin.Instance.Config.Cleanup).TotalMilliseconds;
                    Log(cleanupTimer.Interval.ToString());
                    UpdateCooldownDuration(Plugin.Instance.Config.Cooldown);
                    _configurationInitialized = true;
                    Log("Configuration Loaded Successfully");
                }
                catch (Exception ex)
                {
                    Log($"Hmm error?{ex.Message}{ex.InnerException}");
                }
            }
            else
            {
                Log("Loading Defaults");
            }



        }
        private void InitializeFileOperations()
        {
            if (!_fileOperationsInitialized)
            {
                EnsureFileExists(stringPath);
                LoadRandomStrings(stringPath);
                ShuffleRandomStrings();
                _fileOperationsInitialized = true;
            }
        }


        private void PopulateQueue()
        {
            if (!_queuePopulated)
            {
                foreach (var str in RandomStrings)
                {
                    RandomStringQueue.Enqueue(str);
                }
                _queuePopulated = true;
            }
        }

        private void LoadVoteTotals()
        {
            if (!_voteTotalsLoaded)
            {
                LoadVoteTotalsFromFile();
                _voteTotalsLoaded = true;
            }
        }

        private void InitializeHttpClient()
        {
            if (!_httpClientInitialized)
            {
                _httpClient = new HttpClient();
                _votingApiUrl = "https://space-engineers.com/api/?object=votes&element=claim&key=kLQClxZxOP3q6bVnXpS78EEXc3wKp7YB6m&steamid="; // Replace with actual voting API URL
                _claimApiUrl = "https://space-engineers.com/api/?action=post&object=votes&element=claim&key=kLQClxZxOP3q6bVnXpS78EEXc3wKp7YB6m&steamid=";
                _votecheckApiUrl = "https://space-engineers.com/api/?object=votes&element=claim&key=kLQClxZxOP3q6bVnXpS78EEXc3wKp7YB6m&steamid=";
                _httpClientInitialized = true;
            }
        }

        private void InitializeCooldownManager()
        {
            if (!_cooldownManagerInitialized)
            {
                _cooldownManager = new CooldownManager(_initialCooldownDuration);
                _cooldownManagerInitialized = true;
            }
        }


        // Method to update the cooldown duration
        public bool UpdateCooldownDuration(TimeSpan newDuration)
        {
            try
            {
                _cooldownManager.CooldownDuration = newDuration;
                return true;
            }
            catch (Exception ex)
            {
                Log($"tits, didn't work{ex.Message}");
                return false;
            }
            // Update the cooldown duration for the CooldownManager

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

        public IMyPlayer GetPlayerByName(string playerName)
        {
            List<IMyPlayer> players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players);

            // Iterate through players to find the matching name and return the player object
            foreach (IMyPlayer player in players)
            {
                if (player.DisplayName == playerName)
                {
                    return player;
                }
            }

            // Return null if the player with the given name is not found
            return null;
        }

        public float GetPlayerInventoryVolume(IMyPlayer player)
        {
            if (player != null)
            {
                var character = player.Character;
                if (character != null)
                {
                    var inventory = character.GetInventory() as IMyInventory;
                    if (inventory != null)
                    {
                        return (float)inventory.CurrentVolume;
                    }
                }
            }
            return 0f;
        }

        public float GetPlayerMaxInventoryVolume(IMyPlayer player)
        {
            if (player != null)
            {
                var character = player.Character;
                if (character != null)
                {
                    var inventory = character.GetInventory() as IMyInventory;
                    if (inventory != null)
                    {
                        return (float)inventory.MaxVolume;
                    }
                }
            }
            return 0f;
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

        public override void LoadData()
        {
            base.LoadData();
            // Load vote totals from file when the plugin is loaded
            LoadVoteTotalsFromFile();
            IsInitialized = true;
            Log("NachoPlugin has been loaded!");
        }

        protected override void UnloadData()
        {
            base.UnloadData();
            Timer?.Stop();
            Timer?.Dispose();
            voteCheckTimer?.Stop();
            voteCheckTimer?.Dispose();
            cleanupTimer?.Stop();
            cleanupTimer?.Dispose();
            IsInitialized = false;
            Log("NachoPlugin has been unloaded!");
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
            Timer.Elapsed += OnTimerElapsed;
            Timer.AutoReset = true;
            Timer.Start();
            cleanupTimer = new System.Timers.Timer
            {
                Interval = TimeSpan.FromHours(4).TotalMilliseconds,
                AutoReset = true
            };           
            cleanupTimer.Elapsed += CleanupTimer_Elapsed;
            
            cleanupTimer.Start();
            SetOneOffTimer();
            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(MESSAGE_ID, HandleMessage);
            MyAPIGateway.Session.OnSessionReady += InitializeConfiguration;

        }

        private static void SetOneOffTimer()
        {
            DateTime now = DateTime.Now;
            DateTime nextTrigger;

            // Determine the next trigger time
            if (now.Hour < 1 || (now.Hour == 1 && now.Minute < 19))
            {
                // Set next trigger to 1:19 AM today
                nextTrigger = new DateTime(now.Year, now.Month, now.Day, 1, 19, 0);
            }
            else if (now.Hour < 13 || (now.Hour == 13 && now.Minute < 19))
            {
                // Set next trigger to 1:19 PM today
                nextTrigger = new DateTime(now.Year, now.Month, now.Day, 13, 19, 0);
            }
            else
            {
                // Set next trigger to 1:19 AM the next day
                nextTrigger = new DateTime(now.Year, now.Month, now.Day, 1, 19, 0).AddDays(1);
            }

            double intervalToNextTrigger = (nextTrigger - now).TotalMilliseconds;

            reboot = new Timer(intervalToNextTrigger);
            reboot.Elapsed += RebootTimer;
            reboot.AutoReset = false; // One-off timer
            reboot.Start();
        }
        private static void RebootTimer(object sender, ElapsedEventArgs e)
        {
            RebootWarning();
            reboot.Dispose();
        }
        public static async void RebootWarning()
        {
            for (int i = 5; i > 0; i--)
            {
                MyAPIGateway.Utilities.SendMessage($"Server will restart in {i} minute{(i > 1 ? "s" : "")}!");
                await Task.Delay(TimeSpan.FromMinutes(1));
            }
        }

        private async void CleanupTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            for (int i = 5; i > 0; i--)
            {
                MyAPIGateway.Utilities.SendMessage($"Server cleanup in {i} minute{(i > 1 ? "s" : "")}!");
                await Task.Delay(TimeSpan.FromMinutes(1));
            }
            ScanAndDeleteOutOfRangeGrids();
            
            shopHandler.ReloadShopItems();
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

        // Method to calculate the PCU for a player
        public long CalculatePlayerPCU(ulong steamId)
        {
            long totalPCU = 0;
            HashSet<IMyEntity> entities = new HashSet<IMyEntity>();

            // Retrieve all entities and filter out the player's grids
            MyAPIGateway.Entities.GetEntities(entities, entity => entity is IMyCubeGrid);

            foreach (var entity in entities)
            {
                if (entity is IMyCubeGrid grid && grid.BigOwners.Contains(GetPlayerIdentityIdByName(GetPlayerNameFromSteamId(steamId))))
                {
                    totalPCU += GetGridPCU(grid);
                }
            }

            return totalPCU;
        }

        // Method to calculate the PCU of a grid
        public long GetGridPCU(IMyCubeGrid grid)
        {
            long gridPCU = 0;
            List<IMySlimBlock> blocks = new List<IMySlimBlock>();
            grid.GetBlocks(blocks);
            
            foreach (var block in blocks)
            {
                if (block.BlockDefinition is MyCubeBlockDefinition blockDefinition)
                {
                    gridPCU += blockDefinition.PCU;
                }
                else
                {
                    Log("It didn't work");
                }
            }

            return gridPCU;
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


        public void GivePlayerItem(ulong targetSteamId, string itemType, string itemSubtype, string amountString)
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
            IMyPlayer senderPlayer = GetPlayerBySteamId(targetSteamId);
            if (senderPlayer == null)
            {
                Log($"Player not found: {targetSteamId}");
                return;
            }

            // Get the character or the entity controlled by the player
            if (!(senderPlayer.Controller.ControlledEntity is IMyEntity controlledEntity))
            {
                Log($"Player is not controlling any entity: {targetSteamId}");
                return;
            }

            // Get the inventory from the controlled entity
            IMyInventory inventory = null;
            if (controlledEntity is IMyCharacter character)
            {
                inventory = character.GetInventory();
            }
            else if (controlledEntity is IMyCockpit cockpit)
            {
                inventory = cockpit.GetInventory();
            }
            else if (controlledEntity is IMyRemoteControl remoteControl)
            {
                inventory = remoteControl.GetInventory();
            }

            if (inventory == null)
            {
                Log($"No inventory found for controlled entity of player: {targetSteamId}");
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
            if (!(itemBuilder is MyObjectBuilder_PhysicalObject physicalItem))
            {
                Log($"Could not create physical item: {itemSubtype}");
                return;
            }

            // Add the item to the player's inventory
            inventory.AddItems(amount, physicalItem);

            Log($"Given {amount} of {itemSubtype} to player: {targetSteamId}");
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
                            case "turnonpb":
                                HandleTurnOnPBCommand();
                                break;
                            case "motd":
                                HandleMotdCommand(sender);
                                break;
                            case "discord":
                                HandleDiscordCommand(sender);
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
                                    if (long.TryParse(messageParts[2].Trim(), out long amount))
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
                                    MyAPIGateway.Multiplayer.SendMessageTo(MESSAGE_ID,WhisperMessage("Usage: !pay *username* *amount*"), sender);
                                }
                                break;
                            case "help":
                            case "commands":
                                // Get the available commands for the sender
                                string availableCommands = GetAvailableCommands(sender);
                                // Log the available commands
                                Log(availableCommands);
                                MyAPIGateway.Multiplayer.SendMessageTo(MESSAGE_ID, WhisperMessage(availableCommands), sender);
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
                                UpdateCooldownDuration(Plugin.Instance.Config.Cooldown);
                                break;
                            case "updatevote":
                                await CheckAndUpdateVoteTotals();
                                break;
                            case "grids":
                                HandleGridsCommand();
                                break;
                            case "power":
                                if (messageParts.Length < 2)
                                {
                                    MyAPIGateway.Multiplayer.SendMessageTo(MESSAGE_ID, WhisperMessage("This command will power up the first battery in your Small Grid your currently seated in (the first battery in the block list), type '!power confirm' to use"),sender);
                                }
                                else
                                {
                                    HandlePowerCommand(sender);
                                }
                                
                                break;
                            case "give_item":
                                if (messageParts.Length > 3)
                                {
                                    GivePlayerItem(GetSteamIdFromPlayerName(messageParts[1]), "MyObjectBuilder_Component", messageParts[2], messageParts[3]);
                                }
                                else
                                {
                                    Log($"Not enough Commands for GiveItem:{sender}");
                                }
                                break;
                            case "scrapit":
                                if (messageParts.Length < 2)
                                {
                                    MyAPIGateway.Multiplayer.SendMessageTo(MESSAGE_ID, WhisperMessage("This command will check the last time the server has seen the player login"), sender);
                                }
                                else
                                {
                                    CheckPlayerLogin(messageParts[1], sender);
                                }
                                break;
                            case "updateshop":
                                shopHandler.ReloadShopItems();
                                break;
                            case "gridsnear":
                                HandleGridsNearMeCommand(sender);
                                break;
                            default:
                                Log($"Unknown command: {command}");
                                var message = WhisperMessage($"Unknown Command: {command}");
                                MyAPIGateway.Multiplayer.SendMessageTo(MESSAGE_ID, message, sender);
                                break;
                        }
                    }

                    _cooldownManager.UpdateCooldown(sender, command);
                }
                else
                {
                    var message = WhisperMessage("You are on cooldown for this command");
                    MyAPIGateway.Multiplayer.SendMessageTo(MESSAGE_ID, message, sender);
                    Log($"{sender} is on cooldown");
                }
            }
        }
        public static byte[] WhisperMessage(string message)
        {
            return Encoding.UTF8.GetBytes(message);
        }

        private void HandleMessage(ushort messageId, byte[] data, ulong senderSteamId, bool reliable)
        {
            string message = Encoding.UTF8.GetString(data);
            MyAPIGateway.Utilities.ShowMessage("Nacho Bot", $"Message from {senderSteamId}: {message}");
            Log($"{senderSteamId} sent {message}");
            OnMessageEntered(senderSteamId, message);
        }

        public void CheckPlayerLogin(string playerName, ulong sender)
        {
            try
            {
                // Ensure the path to the player names file
                var playerFile = Path.Combine(MyFileSystem.UserDataPath, "PlayerSteamIDandNames.txt");
                if (File.Exists(playerFile))
                {
                    var existingLines = File.ReadAllLines(playerFile);
                    // Parse existing lines and search for the player name
                    foreach (var line in existingLines)
                    {
                        var parts = line.Split(',');
                        if (parts.Length == 3 && parts[1].Equals(playerName, StringComparison.OrdinalIgnoreCase))
                        {
                            string lastSeen = parts[2];
                            MyAPIGateway.Multiplayer.SendMessageTo(MESSAGE_ID, WhisperMessage($"Player {playerName} was last seen at {lastSeen}"), sender);
                            Console.WriteLine($"Player {playerName} was last seen at {lastSeen}");
                            return;
                        }
                    }
                    var message1 = WhisperMessage($"Player {playerName} not found.");
                    MyAPIGateway.Multiplayer.SendMessageTo(MESSAGE_ID, message1, sender);
                    Console.WriteLine($"Player {playerName} was not found");
                }
                else
                {
                    Console.WriteLine("Player data file does not exist.");
                }
            }
            catch (Exception ex)
            {
                // Log errors
                Console.WriteLine($"Error checking player login: {ex.Message}");
                MyAPIGateway.Utilities.SendMessage("error");
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
                var message1 = WhisperMessage("Not enough money honey");
                Log($"Insufficient balance for sender: {senderName}");
                MyAPIGateway.Multiplayer.SendMessageTo(MESSAGE_ID, message1, senderSteamId); 
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
                var message2 = WhisperMessage("You must be in a small grid ship");
                Log($"Player is not controlling any entity: {senderName}");
                MyAPIGateway.Multiplayer.SendMessageTo(MESSAGE_ID, message2, senderSteamId);
                return;
            }

            IMyCubeGrid grid = null;

            if (controlledEntity is IMyCharacter character)
            {
                if (character.Parent is IMyShipController shipController)
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
                var message2 = WhisperMessage("You must be in a small grid ship");
                MyAPIGateway.Multiplayer.SendMessageTo(MESSAGE_ID, message2, senderSteamId);
                Log($"Player is not controlling a ship grid: {senderName}");
                return;
            }

            // Check if the grid is a small grid
            if (grid.GridSizeEnum != MyCubeSize.Small)
            {
                var message3 = WhisperMessage("Small Grids Only");
                Log($"Player is not controlling a small grid: {senderName}");
                MyAPIGateway.Multiplayer.SendMessageTo(MESSAGE_ID, message3, senderSteamId);
                return;
            }

            List<IMyBatteryBlock> batteries = new List<IMyBatteryBlock>();
            MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid).GetBlocksOfType(batteries);

            if (batteries.Count == 0)
            {
                var message1 = WhisperMessage("No Valid Batteries Found");
                Log($"No batteries found on the grid: {senderName}");
                MyAPIGateway.Multiplayer.SendMessageTo(MESSAGE_ID, message1, senderSteamId);
                return;
            }

            // Deduct the balance
            MyAPIGateway.Players.RequestChangeBalance(senderIdentityId, -PowerCommandCost);

            // Set the first battery to max power
            MyBatteryBlock battery = (MyBatteryBlock)batteries[0];
            battery.CurrentStoredPower = battery.MaxStoredPower;

            Log($"Battery set to max power for player: {senderName}. 10 million credits deducted.");
            var message = WhisperMessage("Juiced Up!");
            MyAPIGateway.Multiplayer.SendMessageTo(MESSAGE_ID, message, senderSteamId);        }
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

            foreach (IMyCubeGrid grid in entities.Cast<IMyCubeGrid>())
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
        public void HandleTurnOnPBCommand()
        {
            HashSet<IMyEntity> entities = new HashSet<IMyEntity>();
            MyAPIGateway.Entities.GetEntities(entities, e => e is IMyCubeGrid);

            int turnedOffCount = 0;

            foreach (IMyCubeGrid grid in entities.Cast<IMyCubeGrid>())
            {
                IMyGridTerminalSystem terminalSystem = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid);
                if (terminalSystem != null)
                {
                    List<IMyProgrammableBlock> programmableBlocks = new List<IMyProgrammableBlock>();
                    terminalSystem.GetBlocksOfType(programmableBlocks);

                    foreach (IMyProgrammableBlock programmableBlock in programmableBlocks)
                    {
                        programmableBlock.Enabled = true;
                        turnedOffCount++;
                    }
                }
            }

            MyAPIGateway.Utilities.SendMessage($"Turned on {turnedOffCount} programmable blocks.");
            Log($"Turned on {turnedOffCount} programmable blocks.");
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
                    string formattedCash = $"{cash:N0} SC";
                    Log(player.DisplayName + cash.ToString());
                    MyAPIGateway.Utilities.SendMessage($"{player.DisplayName} : {formattedCash}");
                }
            }

        }

        public void HandleFlexCommand(ulong sender)
        {
            string senderID = GetPlayerNameFromSteamId(sender);
            _ = GetPlayerBalance(senderID, out long senderCash);

            string formattedSenderCash = $"{senderCash:N0} SC";

            // Calculate the PCU for the sender
            long senderPCU = CalculatePlayerPCU(sender);
            string formattedSenderPCU = $"{senderPCU:N0} PCU";

            // Send the message to the player
            MyAPIGateway.Utilities.SendMessage($"{senderID}: {formattedSenderCash}, PCU: {formattedSenderPCU}");

            // Log the information
            Log($"{senderID}: {formattedSenderCash}, PCU: {formattedSenderPCU}");
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

        public void HandleMotdCommand(ulong sender)
        {
            Log("Motd Command Detected");

            // Send a message of the day
            //this code here is how we do things, THIS CALLS THE VAR STORED IN THE FILE, CANNOT CALL THIS WAY UNLESS SERVER IS RUNNING, OTHERWISE THIS CODE-LINK IS NULL
            //so just honestly don't use it in initializers or in the "Before, After, or Load Data" methods.
            string motd = Plugin.Instance.Config.Motd;
            var message = WhisperMessage(motd);
            MyAPIGateway.Multiplayer.SendMessageTo(MESSAGE_ID, message, sender);

        }

        public void HandleDiscordCommand(ulong sender)
        {
            // Send the Discord invite link to the player who entered the command
            var message = WhisperMessage("Come join us on discord at discord.gg/vnAt8X64ut. Use $ to chat with players in discord!");
            MyAPIGateway.Multiplayer.SendMessageTo(MESSAGE_ID, message, sender);
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
                    var message = WhisperMessage("Vote request already in progress try again in a moment");
                    MyAPIGateway.Multiplayer.SendMessageTo(MESSAGE_ID, message, sender);
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
                        var message = WhisperMessage("You have voted and are ready to claim!");
                        MyAPIGateway.Multiplayer.SendMessageTo(MESSAGE_ID, message, sender);
                        Log($"{sender} is ready to claim");
                    }
                    else if (responseBody.Contains("0"))
                    {
                        var message = WhisperMessage("You haven't voted yet! Go to www.space-engineers.com and vote today!");
                        MyAPIGateway.Multiplayer.SendMessageTo(MESSAGE_ID, message, sender);
                        Log($"{sender} is ready to vote");
                    }
                    else if (responseBody.Contains("2"))
                    {
                        var message = WhisperMessage("You have voted and claimed your reward today already, make sure to vote again to help the server!");
                        MyAPIGateway.Multiplayer.SendMessageTo(MESSAGE_ID, message, sender);
                        Log($"{sender} has claimed today");
                    }
                }
                else
                {
                    var message = WhisperMessage("There has been an error, please try again in a moment");
                    MyAPIGateway.Multiplayer.SendMessageTo(MESSAGE_ID, message, sender);
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
                        var message = WhisperMessage("Vote reward claimed! Enjoy 5 Power cells!");
                        MyAPIGateway.Multiplayer.SendMessageTo(MESSAGE_ID, message, sender);
                        Log($"{sender} claimed reward!");
                        long target = MyAPIGateway.Players.TryGetIdentityId(sender);
                        MyAPIGateway.Players.RequestChangeBalance(target, Plugin.Instance.Config.Reward);
                        GivePlayerItem(sender, "MyObjectBuilder_Component", "PowerCell", "5");

                    }
                    else
                    {
                        var message = WhisperMessage("You haven't voted yet!");
                        MyAPIGateway.Multiplayer.SendMessageTo(MESSAGE_ID, message, sender);
                        Log("User didn't vote");
                    }

                }
                else
                {
                    var message = WhisperMessage("There was an error claiming reward...");
                    MyAPIGateway.Multiplayer.SendMessageTo(MESSAGE_ID, message, sender);
                    Log("Error claiming");
                }
            }
            catch (Exception ex)
            {
                MyAPIGateway.Utilities.SendMessage($"Error claiming vote reward: {ex.Message} this one you gotta tell nacho about");
                Log("error! this one you gotta tell nacho about");
            }
        }
        public void HandlePayCommand(ulong sender, string recipient, long amount)
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
                var message2 = WhisperMessage("Whoa there, Bank Account ain't got that much...");
                MyAPIGateway.Multiplayer.SendMessageTo(MESSAGE_ID, message2, sender);
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

            MyAPIGateway.Multiplayer.SendMessageTo(MESSAGE_ID, WhisperMessage($"{senderBank} has sent you {amount}"), GetSteamIdFromPlayerName(recipient));
            MyAPIGateway.Multiplayer.SendMessageTo(MESSAGE_ID, WhisperMessage($"You have sent {recipient} {amount}"), sender);
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
                var message = WhisperMessage($"Player: {playerName}, Vote Total: {voteTotal}");
                MyAPIGateway.Multiplayer.SendMessageTo(MESSAGE_ID, message, sender);
            }
            else
            {
                // Unable to retrieve username
                Log("Unable to retrieve username for sender.");
                var message = WhisperMessage("Either you voted and the system hasn't seen you yet, or you haven't voted!");
                MyAPIGateway.Multiplayer.SendMessageTo(MESSAGE_ID, message, sender);
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

                        string detectedBlock;
                        if (block.FatBlock is IMyCubeBlock cubeBlock && IsScrapBeacon(cubeBlock, out detectedBlock) && IsBeaconFunctionalAndPowered(cubeBlock, detectedBlock))
                        {
                            
                            scrapBeacons.Add(cubeBlock);
                            //Log($"Scrap Beacon found: {cubeBlock.EntityId}");
                        }
                    }


                }
                else
                {
                    //Log("No Beacons Found");
                }
            }

            // Log the count of each entity type
            foreach (var pair in entityTypeCounts)
            {
                Log($"Found {pair.Value} entities of type {pair.Key}");
            }
            int staticGridCount = 0;
            int dynamicGridCount = 0;

            // Retrieve all grids
            List<IMyCubeGrid> grids = new List<IMyCubeGrid>();
            foreach (IMyEntity entity in entities)
            {
                if (entity is IMyCubeGrid grid)
                {
                    grids.Add(grid);
                    if (grid.IsStatic)
                    {
                        staticGridCount++;
                    }
                    else
                    {
                        dynamicGridCount++;
                    }
                }
            }
            Log($"Grids found: {grids.Count}");
            Log($"Static Grids (Non Sim Speed Killing){staticGridCount}");
            Log($"Dynamic Grids (Sim Speed Killers){dynamicGridCount}");
            MyAPIGateway.Utilities.SendMessage($"Grids found: {grids.Count}");
            MyAPIGateway.Utilities.SendMessage($"Static Grids (Non Sim Speed Killing){staticGridCount}");
            MyAPIGateway.Utilities.SendMessage($"Dynamic Grids (Sim Speed Killers){dynamicGridCount}");
            foreach (IMyCubeGrid grid in grids)
            {
                //Log("Checking Distances");
                // Check if the grid is within range of any Scrap Beacon block
                if (!IsGridWithinRangeOfScrapBeacon(grid, scrapBeacons))
                {
                    //Log("Deleting some grid");
                    // Delete the grid if it's not within range
                    DeleteGrid(grid);
                }
            }
        }
        private bool IsBeaconFunctionalAndPowered(IMyCubeBlock cubeBlock, string detectedBlock)
        {
            if (detectedBlock == "StoreBlock")
            {
                return true;
            }
            if (cubeBlock is IMyFunctionalBlock functionalBlock)
            {
                // Checks if the beacon is functional (not damaged) and is on a powered grid
                return functionalBlock.IsFunctional && functionalBlock.ResourceSink.IsPoweredByType(Electricity) ;
            }
            return false;
        }

        public bool IsScrapBeacon(IMyCubeBlock block, out string detectedBlock)
        {
            // Initialize the out parameter
            detectedBlock = null;

            // Check if the block is a Scrap Beacon block based on its subtype ID
            if (block.BlockDefinition.SubtypeId == "LargeBlockScrapBeacon")
            {
                detectedBlock = "LargeBlockScrapBeacon";
                return true;
            }
            else if (block.BlockDefinition.SubtypeId == "SmallBlockScrapBeacon")
            {
                detectedBlock = "SmallBlockScrapBeacon";
                return true;
            }
            else if (block.BlockDefinition.SubtypeId == "StoreBlock")
            {
                detectedBlock = "StoreBlock";
                return true;
            }

            return false;
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
            List<IMyCubeGrid> grids1 = new List<IMyCubeGrid>();
            MyAPIGateway.Entities.GetEntities(entities, e => e is IMyCubeGrid);
            int staticGridCount = 0;
            int dynamicGridCount = 0;
            int gridCount = entities.Count;
            foreach (IMyEntity entity in entities)
            {
                if (entity is IMyCubeGrid grid)
                {
                    grids1.Add(grid);
                    if (grid.IsStatic)
                    {
                        staticGridCount++;
                    }
                    else
                    {
                        dynamicGridCount++;
                    }
                }
            }
            MyAPIGateway.Utilities.SendMessage($"Total number of grids: {gridCount}");
            MyAPIGateway.Utilities.SendMessage($"Static Grids (Non Sim Speed Killing){staticGridCount}");
            MyAPIGateway.Utilities.SendMessage($"Dynamic Grids (Sim Speed Killers){dynamicGridCount}");
            Log($"Total number of grids: {gridCount}");
            
            
        }

        public void HandleGridsNearMeCommand(ulong sender)
        {
            HashSet<IMyEntity> entities = new HashSet<IMyEntity>();
            List<IMyCubeGrid> grids1 = new List<IMyCubeGrid>();
            MyAPIGateway.Entities.GetEntities(entities, e => e is IMyCubeGrid);

            IMyPlayer player = GetPlayerBySteamId(sender);
            if (player == null)
            {
                MyAPIGateway.Utilities.SendMessage("No player found.");
                return;
            }

            Vector3D playerPosition = player.GetPosition();
            double radius = 250.0;
            int staticGridCount = 0;
            int dynamicGridCount = 0;
            int gridCount = 0;

            foreach (IMyEntity entity in entities)
            {
                if (entity is IMyCubeGrid grid)
                {
                    double distance = Vector3D.Distance(playerPosition, grid.GetPosition());
                    if (distance <= radius)
                    {
                        grids1.Add(grid);
                        gridCount++;
                        if (grid.IsStatic)
                        {
                            staticGridCount++;
                        }
                        else
                        {
                            dynamicGridCount++;
                        }
                    }
                }
            }

            MyAPIGateway.Utilities.SendMessage($"Total number of grids within 250m: {gridCount}");
            MyAPIGateway.Utilities.SendMessage($"Static Grids (Non Sim Speed Killing): {staticGridCount}");
            MyAPIGateway.Utilities.SendMessage($"Dynamic Grids (Sim Speed Killers): {dynamicGridCount}");
            Log($"Total number of grids within 250m: {gridCount}");
        }

        public void TestArea(string configsetting, string param)
        {
            var paramdistance = int.Parse(param);
            Log($"{viewdistance}");
            if ( param == null || paramdistance != viewdistance)
            {
                MyAPIGateway.Utilities.SendMessage($"{viewdistance}");
                viewdistance = paramdistance;
                MyAPIGateway.Utilities.SendMessage($"{viewdistance}");
                
            }
            
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
        { "turnonpb", PromotionLevel.Admin },
        { "grids", PromotionLevel.Default },
        { "flex", PromotionLevel.Default },
        { "power", PromotionLevel.Default },
        { "give_item", PromotionLevel.Admin }

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