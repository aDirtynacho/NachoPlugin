using Sandbox.ModAPI;
using VRage.Game.Components;
using VRageMath;
using System.Text;
using NachoPluginSystem;
using VRage.Utils;
using System;
using System.Collections.Generic;
using VRage.Game.ModAPI;
using System.Windows.Forms;

namespace nachothreat
{
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public class ThreatScoreServer : MySessionComponentBase
    {
        private MESApi _spawnerAPI;
        private NachoPlugin nacho;

        public override void LoadData()
        {
            _spawnerAPI = new MESApi();
            nacho = new NachoPlugin();
            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(12345, HandleClientRequest);
        }

        protected override void UnloadData()
        {
            MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(12345, HandleClientRequest);
            _spawnerAPI.UnregisterListener();
        }

        private void HandleClientRequest(ushort handler, byte[] data, ulong sender, bool fromServer)
        {
            try
            {
                string message = Encoding.UTF8.GetString(data);
                NachoPlugin.Log(message);
                string[] parts = message.Split('|');
                if (parts.Length != 3)
                {
                    NachoPlugin.Log("Invalid message format received");
                    return;
                }
                long playerId = long.Parse(parts[0]);
                ulong steamId = ulong.Parse(parts[1]);
                string[] positionParts = parts[2].Split(',');
                Vector3D playerPosition = new Vector3D(double.Parse(positionParts[0]), double.Parse(positionParts[1]), double.Parse(positionParts[2]));
                NachoPlugin.Log($"{playerPosition}");

                var player = nacho.GetPlayerBySteamId(steamId);
                NachoPlugin.Log($"{player}");
                if (_spawnerAPI == null)
                {
                    NachoPlugin.Log("Spawner API is not initialized");
                    return;
                }
                if (player != null)
                {
                    _spawnerAPI.ChatCommand("/MES.GESAP", MatrixD.CreateTranslation(playerPosition), playerId, steamId);
                    System.Threading.Thread.Sleep(1000);
                    string result = Clipboard.GetText();
                    if (string.IsNullOrWhiteSpace(result))
                    {
                        NachoPlugin.Log("Clipboard is empty or not updated");
                        return;
                    }
                    // Extract the threat score from the result
                    double threatScore = ExtractThreatScore(result);

                    string response = threatScore.ToString();
                    byte[] responseBytes = Encoding.UTF8.GetBytes(response);
                    MyAPIGateway.Multiplayer.SendMessageTo(12346, responseBytes, steamId);
                }
            }
            catch (Exception ex)
            {
                NachoPlugin.Log($"{ex}");
            }
        }

        private double ExtractThreatScore(string result)
        {
            string[] lines = result.Split('\n');
            foreach (var line in lines)
            {
                if (line.StartsWith(" - Threat Score:"))
                {
                    string[] parts = line.Split(':');
                    if (parts.Length > 1 && double.TryParse(parts[1].Trim(), out double threatScore))
                    {
                        return threatScore;
                    }
                }
            }
            return 0.0;
        }
    }
}
