#if !TORCH

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using static System.Net.WebRequestMethods;

namespace Shared.Config
{
    public class PluginConfig : IPluginConfig
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private void SetValue<T>(ref T field, T value, [CallerMemberName] string propName = "")
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return;

            field = value;

            OnPropertyChanged(propName);
        }

        private void OnPropertyChanged([CallerMemberName] string propName = "")
        {
            PropertyChangedEventHandler propertyChanged = PropertyChanged;
            if (propertyChanged == null)
                return;

            propertyChanged(this, new PropertyChangedEventArgs(propName));
        }

        private bool enabled = true;
        private bool detectCodeChanges = true;
        private string motd = "TESTING THIS SHIT GOD DAMN";
        private TimeSpan cooldown = TimeSpan.FromSeconds(15);
        private ulong admin = 76561198032754201;
        private int reward = 30000;
        private int powercost = 10000000;
        private string votekey = "Insert your Voting api key from www.space-engineers.com here";
        private double cleanup = 4;
        private int port = 27019;
        // TODO: Implement your config fields here
        // The default values here will apply to Client and Dedicated.
        // The default values for Torch are defined in TorchPlugin.

        public bool Enabled
        {
            get => enabled;
            set => SetValue(ref enabled, value);
        }

        public bool DetectCodeChanges
        {
            get => detectCodeChanges;
            set => SetValue(ref detectCodeChanges, value);
        }

        public string Motd
        {
            get => motd;
            set => SetValue(ref motd, value);
        }
        public TimeSpan Cooldown
        {
            get => cooldown;
            set => SetValue(ref cooldown, value);
        }
        public ulong Admin
        {
            get => admin;
            set => SetValue(ref admin, value);
        }
        public int Reward
        {
            get => reward;
            set => SetValue(ref reward, value);
        }

        public int PowerCost
        {
            get => powercost;
            set => SetValue(ref powercost, value);
        }

        public string VoteKey
        {
            get => votekey;
            set => SetValue(ref votekey, value);
        }

        public double Cleanup
        {
            get => cleanup;
            set => SetValue(ref cleanup, value);
        }

        public int Port
        {
            get => port;
            set => SetValue(ref port, value);
        }
        // TODO: Encapsulate your config fields as properties here
    }
}

#endif