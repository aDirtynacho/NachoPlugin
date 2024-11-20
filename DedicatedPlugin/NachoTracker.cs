using Sandbox.Engine.Multiplayer;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VRage.FileSystem;
using VRage.Game.Components;

namespace NachoPluginSystem
{
    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate, 2000)]
    public class NachoTracker : MySessionComponentBase
    {
        public NachoPlugin nachoplugin1;
        Dictionary<ulong, string> playerNames = new Dictionary<ulong, string>();
        private bool _configurationInitialized = false;
        public MyMultiplayerBase clients;
        public NachoTracker() 
        {
            try
            {
                clients = (MyMultiplayerBase)VRage.Replication.MyMultiplayerMinimalBase.Instance;
            }
            catch (InvalidCastException ex)
            {
                Log2($"{ex.Message}{ex.InnerException}");
            }
        }
        public void Log2(string message)
        {
            if (NachoPlugin.IsInitialized)
            {
                NachoPlugin.Log(message); // Call the static Log method
            }
            else
            {
                Console.WriteLine("NachoPlugin is not initialized. Cannot log message.");
            }
        }

        public override void LoadData()
        {
            base.LoadData();
            // Your initialization logic here
            
            Console.WriteLine("NachoTracker has been loaded");
            Log2("NachoTracker has been loaded!");
        }
        protected override void UnloadData()
        {
            base.UnloadData();
            // Your cleanup logic here
            if (clients != null)
            {
                clients.ClientJoined -= PlayerJoinedHandler;
            }
            Console.WriteLine("NachoTracker has been unloaded");
            Log2("NachoTracker has been unloaded!");
        }

        public override void BeforeStart()
        {
            base.BeforeStart();
            // Your setup logic here
            nachoplugin1 = new NachoPlugin();
            MyAPIGateway.Session.OnSessionReady += InitializeConfiguration;

            if (clients != null)
            {
                Console.WriteLine("Loading Player Join Handler");
                clients.ClientJoined += PlayerJoinedHandler;
                
            }

            Log2("NachoTracker has started!");
        }

        private void PlayerJoinedHandler(ulong steamId, string playerName)
        {
            PlayerJoined(steamId, playerName);
        }
        public void InitializeConfiguration()
        {
            if (!_configurationInitialized)
            {
                try
                {
                        _configurationInitialized = true;
                        Log2("Nacho Discord Plugin Configuration Loaded Successfully");
                }
                catch (Exception ex)
                {
                    Log2($"Hmm error?{ex.Message}{ex.InnerException}");
                }
            }
            else
            {
                Log2("Loading Defaults");
                _configurationInitialized = true;
            }
        }

        public void PlayerJoined(ulong playerId, string playerName)
        {
            try
            {
                // Ensure the path to the player names file
                var playerFile = Path.Combine(MyFileSystem.UserDataPath, "PlayerSteamIDandNames.txt");
                Dictionary<ulong, (string, string)> existingNames = new Dictionary<ulong, (string, string)>();

                // Read existing lines if the file exists, otherwise create an empty file
                if (File.Exists(playerFile))
                {
                    var existingLines = File.ReadAllLines(playerFile);
                    // Parse existing lines into the dictionary
                    foreach (var line in existingLines)
                    {
                        var parts = line.Split(',');
                        if (parts.Length == 3 && ulong.TryParse(parts[0], out ulong key))
                        {
                            existingNames[key] = (parts[1], parts[2]);
                        }
                    }
                }
                else
                {
                    // Create an empty file
                    File.Create(playerFile).Close();
                }

                // Ensure the player name starts from the second character
                string cleanedPlayerName = playerName.Length > 1 ? playerName.Substring(1) : playerName;

                // Add or update the entry with the current system time
                string currentTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                existingNames[playerId] = (cleanedPlayerName, currentTime);

                // Prepare updated lines for writing to file
                List<string> updatedLines = existingNames.Select(kvp => $"{kvp.Key},{kvp.Value.Item1},{kvp.Value.Item2}").ToList();
                // Write the updated lines to the file
                File.WriteAllLines(playerFile, updatedLines);
            }
            catch (Exception ex)
            {
                // Log errors
                Console.WriteLine($"Error saving player names to file: {ex.Message}");
            }
        }

    }
}
