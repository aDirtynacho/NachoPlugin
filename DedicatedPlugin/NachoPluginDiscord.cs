using System;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using Sandbox.ModAPI;
using System.Runtime.CompilerServices;

namespace NachoPluginSystem
{
    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate, 1500)]

    public class NachoPluginDiscord : MySessionComponentBase
    {
        private bool _configurationInitialized = false;
        public NachoPluginDiscord()
        {
            
            try
            {

            }
            catch(Exception ex)
            {
                Log1($"{ex.Message}{ex.InnerException}");
            }
        }

        public override void LoadData()
        {
            base.LoadData();
            // Your initialization logic here
            Log1("NachoPluginDiscord has been loaded!");
        }

        protected override void UnloadData()
        {
            base.UnloadData();
            // Your cleanup logic here
            Log1("NachoPluginDiscord has been unloaded!");
        }

        public override void BeforeStart()
        {
            base.BeforeStart();
            // Your setup logic here
            Log1("NachoPluginDiscord has started!");
        }
        public void Log1(string message)
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

        public void InitializeConfiguration()
        {
            if (!_configurationInitialized)
            {
                try
                {
                    _configurationInitialized = true;
                    Log1("Configuration Loaded Successfully");
                }
                catch (Exception ex)
                {
                    Log1($"Hmm error?{ex.Message}{ex.InnerException}");
                }
            }
            else
            {
                Log1("Loading Defaults");
            }



        }
    }
}