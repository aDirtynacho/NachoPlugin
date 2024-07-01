using System;
using VRage.Game.Components;
using Sandbox.ModAPI;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using Newtonsoft.Json;

namespace NachoPluginSystem
{
    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate, 1500)]

    public class NachoPluginDiscord : MySessionComponentBase
    {
        private readonly string webhookUrl = "https://discord.com/api/webhooks/1245831400695922708/zlIF2yYQlQ09Ij1pfVQeirkY6hvp5JHUOHbnluJvalwcD2ywRzJItE2ctV5Pq8LKQjmt";
        private static readonly HttpClient httpClient = new HttpClient();
        private HttpListener httpListener;
        public NachoPlugin nachoplugin;
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

            Console.WriteLine("NachoPluginDiscord has been loaded");
            Log1("NachoPluginDiscord has been loaded!");
        }

        protected override void UnloadData()
        {
            base.UnloadData();
            // Your cleanup logic here
            StopHttpListener();
            Log1("NachoPluginDiscord has been unloaded!");
        }

        public override void BeforeStart()
        {
            base.BeforeStart();
            // Your setup logic here
            nachoplugin = new NachoPlugin();
            MyAPIGateway.Session.OnSessionReady += InitializeConfiguration;
            MyAPIGateway.Utilities.MessageRecieved += OnMessageEntered;
            

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
                    StartHttpListener();
                    if (httpListener == null)
                    {
                        Console.WriteLine("SHITS FUCKED MAN, IT DIDN'T LAUNCH HTTP");
                    }
                    else
                    {
                        _configurationInitialized = true;
                        Log1("Nacho Discord Plugin Configuration Loaded Successfully");
                    }
                }
                catch (Exception ex)
                {
                    Log1($"Hmm error?{ex.Message}{ex.InnerException}");
                }
            }
            else
            {
                Log1("Loading Defaults");
                _configurationInitialized = true;
            }



        }
        private void OnMessageEntered(ulong sender, string message)
        {
            
            var player = nachoplugin.GetPlayerNameFromSteamId(sender);
            var message1 = message.StartsWith("$") ? message.Substring(1) : message; // Remove the $ if it exists
            var chatMessage = $"{player}: {message1}";
            if (!message.StartsWith("DISCORD") && message.StartsWith("$"))
            {
                Log1("ATTEMPTING TO SEND TO NEXT METHOD (PostToWebhook)");
                Task.Run(() => PostToWebhook(chatMessage));
            }
            Log1(chatMessage);
            
        }

        private async Task PostToWebhook(string message)
        {
            Log1("POSTTOWEBHOOK CALLED");
            var payload = new { content = message };
            var jsonPayload = Newtonsoft.Json.JsonConvert.SerializeObject(payload);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            Log1($"{message} {content}");

            var response = await httpClient.PostAsync(webhookUrl, content);
            response.EnsureSuccessStatusCode(); // Optional: handle the response status
            Log1($"{response}");
        }
        private void StartHttpListener()
        {
            Log1("STARTHTTPLISTENER CALLED");
            int port = Plugin.Instance.Config.Port;
            httpListener = new HttpListener();
            httpListener.Prefixes.Add($"http://*:{port}/");
            httpListener.Start();
            Task.Run(() => Listen());
        }

        private void StopHttpListener()
        {
            httpListener?.Stop();
        }
        private async Task Listen()
        {
            while (httpListener.IsListening)
            {
                var context = await httpListener.GetContextAsync();
                var request = context.Request;

                if (request.HttpMethod == "POST")
                {
                    using (var reader = new System.IO.StreamReader(request.InputStream, request.ContentEncoding))
                    {
                        string json = await reader.ReadToEndAsync();
                        var messageData = JsonConvert.DeserializeObject<WebhookMessage>(json);

                        if (messageData != null && !messageData.Content.StartsWith("DISCORD"))
                        {
                            string content = messageData.Content;
                            MyAPIGateway.Utilities.SendMessage($"DISCORD : {content}");
                        }
                    }
                }

                context.Response.StatusCode = 200;
                context.Response.Close();
            }
        }

        private class WebhookMessage
        {
            public string Content { get; set; }
        }
    }
}
