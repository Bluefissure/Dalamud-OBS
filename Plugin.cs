using System;
using ImGuiNET;
using Dalamud.Game;
using Dalamud.Game.ClientState;
using Dalamud.Game.Command;
using Dalamud.Game.Gui;
using Dalamud.IoC;
using Dalamud.Logging;
using Dalamud.Plugin;
using Newtonsoft.Json.Linq;
using OBSPlugin.Attributes;
using OBSWebsocketDotNet;
using OBSPlugin.Objects;
using OBSWebsocketDotNet.Types;

namespace OBSPlugin
{
    public class Plugin : IDalamudPlugin
    {
        [PluginService]
        [RequiredVersion("1.0")]
        internal DalamudPluginInterface PluginInterface { get; init; }

        [PluginService]
        [RequiredVersion("1.0")]
        internal CommandManager Commands { get; init; }

        [PluginService]
        [RequiredVersion("1.0")]
        internal ChatGui Chat { get; init; }

        [PluginService]
        [RequiredVersion("1.0")]
        internal ClientState ClientState { get; init; }
        [PluginService]
        [RequiredVersion("1.0")]
        internal Framework Framework { get; init; }

        [PluginService]
        [RequiredVersion("1.0")]
        internal GameGui GameGui { get; init; }

        internal readonly PluginCommandManager<Plugin> commandManager;
        internal Configuration config { get; private set; }
        internal readonly PluginUI ui;

        internal OBSWebsocket obs;
        internal bool ConnectionFailed = false;
        internal StreamStatus streamStatus;
        internal OutputState obsStatus = OutputState.Stopped;

        public string Name => "OBS Plugin";

        public Plugin()
        {
            obs = new OBSWebsocket();
            obs.Connected += onConnect;
            obs.StreamStatus += onStreamData;
            obs.StreamingStateChanged += onStreamingStateChange;

            this.config = (Configuration)PluginInterface.GetPluginConfig() ?? new Configuration();
            this.config.Initialize(PluginInterface);

            this.ui = new PluginUI(this);
            PluginInterface.UiBuilder.DisableCutsceneUiHide = true;
            PluginInterface.UiBuilder.DisableAutomaticUiHide = true;
            PluginInterface.UiBuilder.DisableGposeUiHide = true;
            PluginInterface.UiBuilder.DisableUserUiHide = true;
            PluginInterface.UiBuilder.Draw += this.ui.Draw;
            PluginInterface.UiBuilder.OpenConfigUi += this.OpenConfigUi;

            this.commandManager = new PluginCommandManager<Plugin>(this, Commands);

            if (config.Password.Length > 0)
            {
                TryConnect(config.Address, config.Password);
            }
        }

        private void OpenConfigUi()
        {
            this.ui.IsVisible = true;
        }

        public bool TryConnect(string url, string password)
        {
            try
            {
                obs.Connect(url, password);
                PluginLog.Information("Connection established {0}", url);
                ConnectionFailed = false;
                return true;
            }
            catch (AuthFailureException)
            {
                obs.Disconnect();
                ConnectionFailed = true;
            }
            catch (Exception e)
            {
                PluginLog.Error("Connection error {0}", e);
            }
            return false;
        }
        private void onConnect(object sender, EventArgs e)
        {
            var streamStatus = obs.GetStreamingStatus();
            if (streamStatus.IsStreaming)
                onStreamingStateChange(obs, OutputState.Started);
            else
                onStreamingStateChange(obs, OutputState.Stopped);
        }

        private void onStreamData(OBSWebsocket sender, StreamStatus data)
        {
            streamStatus = data;
        }

        private void onStreamingStateChange(OBSWebsocket sender, OutputState newState)
        {
            obsStatus = newState;
        }

        [Command("/obs")]
        [HelpMessage("Open OBSPlugin config panel.")]
        public unsafe void ObsCommand(string command, string args)
        {
            // You may want to assign these references to private variables for convenience.
            // Keep in mind that the local player does not exist until after logging in.
            if (args == "" || args == "config")
            {
                this.ui.IsVisible = !this.ui.IsVisible;
            }
            else if (args == "on")
            {
                this.config.Enabled = true;
                this.config.Save();
            }
            else if (args == "off")
            {
                this.config.Enabled = false;
                this.config.Save();
            }
            else if (args == "toggle")
            {
                this.config.Enabled = !this.config.Enabled;
                this.config.Save();
            }
            else if (args == "update")
            {
                this.ui.UpdateGameUI();
            }
        }

        #region IDisposable Support
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing) return;

            this.commandManager.Dispose();

            PluginInterface.SavePluginConfig(this.config);

            PluginInterface.UiBuilder.Draw -= this.ui.Draw;

            if (obs != null && obs.IsConnected)
            {
                obs.Disconnect();
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        #endregion
    }
}
