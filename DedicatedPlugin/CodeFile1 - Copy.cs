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
        private readonly HttpClient _httpClient;
        private readonly string _votingApiUrl;
        private readonly string _claimApiUrl;

        public NachoPlugin()
        {
            _httpClient = new HttpClient();
            _votingApiUrl = "https://space-engineers.com/api/?object=votes&element=claim&key=kLQClxZxOP3q6bVnXpS78EEXc3wKp7YB6m&steamid="; // Replace with actual voting API URL
            _claimApiUrl = "https://space-engineers.com/api/?action=post&object=votes&element=claim&key=kLQClxZxOP3q6bVnXpS78EEXc3wKp7YB6m&steamid=";
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
            if (messageText.StartsWith("!"))
            {
                string command = messageText.Substring(1).ToLower();

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
        }

        private void HandlePlayersCommand()
        {
            long currentPlayerCount = MyAPIGateway.Players.Count;
            MyAPIGateway.Utilities.SendMessage($"Current number of players online: {currentPlayerCount}");
        }

        private void HandleMotdCommand()
        {
            PluginConfig pluginConfig = new PluginConfig();
            MyAPIGateway.Utilities.SendMessage(pluginConfig.Motd);
        }

        private void HandleDiscordCommand()
        {
            MyAPIGateway.Utilities.SendMessage("Join our Discord server at: discord.gg/vnAt8X64ut");
        }

        private async Task HandleVoteCommand(ulong sender)
        {
            try
            {
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
            HashSet<IMyEntity> entities = new HashSet<IMyEntity>();
            MyAPIGateway.Entities.GetEntities(entities);

            List<IMyCubeBlock> scrapBeacons = new List<IMyCubeBlock>();
            foreach (IMyEntity entity in entities)
            {
                if (entity is IMyCubeGrid cubeGrid)
                {
                    List<IMySlimBlock> blocks = new List<IMySlimBlock>();
                    cubeGrid.GetBlocks(blocks);
                    foreach (IMySlimBlock block in blocks)
                    {
                        if (block.FatBlock is IMyCubeBlock cubeBlock && IsScrapBeacon(cubeBlock))
                        {
                            scrapBeacons.Add(cubeBlock);
                        }
                    }
                }
            }

            List<IMyCubeGrid> grids = new List<IMyCubeGrid>();
            foreach (IMyEntity entity in entities)
            {
                if (entity is IMyCubeGrid grid)
                {
                    grids.Add(grid);
                }
            }

            foreach (IMyCubeGrid grid in grids)
            {
                if (!IsGridWithinRangeOfScrapBeacon(grid, scrapBeacons))
                {
                    DeleteGrid(grid);
                }
            }
        }

        private bool IsScrapBeacon(IMyCubeBlock block)
        {
            return block.BlockDefinition.SubtypeId == "LargeBlockScrapBeacon" ||
                   block.BlockDefinition.SubtypeId == "SmallBlockScrapBeacon";
        }

        private bool IsGridWithinRangeOfScrapBeacon(IMyCubeGrid grid, List<IMyCubeBlock> scrapBeacons)
        {
            Vector3D gridPosition = grid.PositionComp.GetPosition();

            foreach (IMyCubeBlock scrapBeacon in scrapBeacons)
            {
                double distance = Vector3D.Distance(gridPosition, scrapBeacon.GetPosition());
                if (distance <= 250)
                {
                    return true;
                }
            }
            return false;
        }

        private void DeleteGrid(IMyCubeGrid grid)
        {
            grid.Close();
        }
    }
}
