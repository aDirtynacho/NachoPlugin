using System;
using System.ComponentModel;

namespace Shared.Config
{
    public interface IPluginConfig : INotifyPropertyChanged
    {
        // Enables the plugin
        bool Enabled { get; set; }

        // Enables checking for changes in patched game code (disable this on Proton/Linux)
        bool DetectCodeChanges { get; set; }

        // TODO: Add config properties here, then extend the implementing classes accordingly
        string Motd {  get; set; }

        TimeSpan Cooldown {  get; set; }

        ulong Admin {  get; set; }

        int Reward { get; set; }

        int PowerCost { get; set; }

        string VoteKey { get; set; }


    }
}