using Dalamud.Data;
using Dalamud.Game;
using Dalamud.Game.ClientState;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Command;
using Dalamud.Game.Gui;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Common.Lua;
using OBSPlugin.Attributes;
using OBSPlugin.Objects;
using OBSWebsocketDotNet;
using OBSWebsocketDotNet.Communication;
using OBSWebsocketDotNet.Types;
using OBSWebsocketDotNet.Types.Events;
using System;
using System.Threading;
using System.Threading.Tasks;
using static FFXIVClientStructs.FFXIV.Client.System.String.Utf8String.Delegates;

namespace OBSPlugin
{
    public class Plugin : IDalamudPlugin
    {
        [PluginService]
        internal static IDalamudPluginInterface PluginInterface { get; private set; }

        [PluginService]
        internal ICommandManager Commands { get; init; }

        [PluginService]
        internal IChatGui Chat { get; init; }

        [PluginService]
        internal IClientState ClientState { get; init; }
        [PluginService]
        internal IFramework Framework { get; init; }

        [PluginService]
        internal IGameGui GameGui { get; init; }

        [PluginService]
        internal ISigScanner SigScanner { get; init; }

        [PluginService]
        internal ICondition Condition { get; init; }

        [PluginService]
        internal IDataManager Data { get; init; }

        [PluginService]
        internal IGameInteropProvider GameInteropProvider { get; init; }
        [PluginService]
        internal static IPluginLog PluginLog { get; private set; }

        internal string minimumPluginVersion = "5.3.0";

        private CancellationTokenSource keepAliveTokenSource;
        private readonly int keepAliveInterval = 500;

        internal readonly PluginCommandManager<Plugin> commandManager;
        internal Configuration config { get; private set; }
        internal readonly PluginUI ui;

        internal OBSWebsocket obs;
        internal bool Connected = false;
        internal bool ConnectionFailed = false;
        internal ObsVersion versionInfo;
        internal OutputStatus streamStats;
        internal OutputState obsStreamStatus = OutputState.OBS_WEBSOCKET_OUTPUT_STOPPED;
        internal OutputState obsRecordStatus = OutputState.OBS_WEBSOCKET_OUTPUT_STOPPED;
        internal OutputState obsReplayBufferStatus = OutputState.OBS_WEBSOCKET_OUTPUT_STOPPED;
        internal readonly StopWatchHook stopWatchHook;
        internal CombatState state;
        internal float lastCountdownValue;

        private bool _connectLock;
        private CancellationTokenSource _cts = new();
        private bool _stoppingRecord = false;

        public string Name => "OBS Plugin";

        public Plugin()
        {
            obs = new OBSWebsocket();
            obs.Connected += onConnect;
            obs.Disconnected += onDisconnect;
            //obs.StreamStatus += onStreamData;
            obs.StreamStateChanged += onStreamingStateChange;
            obs.RecordStateChanged += onRecordingStateChange;
            obs.ReplayBufferStateChanged += onReplayBufferStateChange;

            this.config = (Configuration)PluginInterface.GetPluginConfig() ?? new Configuration();

            this.ui = new PluginUI(this);
            PluginInterface.UiBuilder.DisableCutsceneUiHide = true;
            PluginInterface.UiBuilder.DisableAutomaticUiHide = true;
            PluginInterface.UiBuilder.DisableGposeUiHide = true;
            PluginInterface.UiBuilder.DisableUserUiHide = true;
            PluginInterface.UiBuilder.Draw += this.ui.Draw;
            PluginInterface.UiBuilder.OpenConfigUi += this.OpenConfigUi;
            PluginInterface.UiBuilder.OpenMainUi += this.OpenMainUi;


            state = new CombatState();
            state.InCombatChanged += new EventHandler((Object sender, EventArgs e) =>
            {
                if (!Connected)
                {
                    TryConnect(config.Address, config.Password);
                    if (!Connected) return;
                }
                if (this.state.InCombat && config.StartRecordOnCombat)
                {
                    try
                    {
                        if (config.CancelStopRecordOnResume && _stoppingRecord)
                        {
                            _cts.Cancel();
                        }
                        else
                        {
                            PluginLog.Information("Auto start recording");
                            this.ui.SetRecordingDir();
                            this.obs.StartRecord();
                        }
                    }
                    catch (ErrorResponseException err)
                    {
                        PluginLog.Warning("Start Recording Error: {0}", err);
                    }
                }
                else if (!this.state.InCombat)
                {
                    if (config.StopRecordOnCombat)
                    {
                        StopRecordingAsync();
                        //new Task(async () =>
                        //{
                        //    try
                        //    {
                        //        PluginLog.Information($"Stop recording in {config.StopRecordOnCombatDelay} seconds");
                        //        _stoppingRecord = true;
                        //        var delay = config.StopRecordOnCombatDelay;
                        //        var isViewingCutScene = false;
                        //        do
                        //        {
                        //            _cts.Token.ThrowIfCancellationRequested();
                        //            await Task.Delay(1000);
                        //            delay -= 1;
                        //            isViewingCutScene = await Framework.RunOnFrameworkThread(() => this.ClientState.LocalPlayer?.OnlineStatus.RowId == 15);
                        //            PluginLog.Information($"isViewingCutScene: {isViewingCutScene}");
                        //        } while (delay > 0 || (config.DontStopInCutscene && isViewingCutScene));
                        //        PluginLog.Information("Auto stop recording");
                        //        // this.ui.SetRecordingDir();
                        //        this.obs.StopRecord();
                        //    }
                        //    catch (ErrorResponseException err)
                        //    {
                        //        PluginLog.Warning("Stop Recording Error: {0}", err);
                        //    }
                        //    finally
                        //    {
                        //        _stoppingRecord = false;
                        //        _cts.Dispose();
                        //        _cts = new();
                        //    }
                        //}, _cts.Token).Start();
                    }
                    if (config.SaveReplayBufferOnCombat)
                    {
                        new Task(() =>
                        {
                            try
                            {
                                var delay = config.SaveReplayBufferOnCombatDelay;
                                do
                                {
                                    _cts.Token.ThrowIfCancellationRequested();
                                    Thread.Sleep(1000);
                                    delay -= 1;
                                } while (delay > 0);
                                PluginLog.Information("Auto save replay buffer");
                                this.obs.SaveReplayBuffer();
                            }
                            catch (ErrorResponseException err)
                            {
                                PluginLog.Warning("Stop Recording Error: {0}", err);
                            }
                        }).Start();
                    }
                }
            });
            state.CountingDownChanged += new EventHandler((Object sender, EventArgs e) =>
            {
                if (!Connected)
                {
                    TryConnect(config.Address, config.Password);
                    return;
                }
                if (this.state.CountDownValue > lastCountdownValue && config.StartRecordOnCountDown)
                {
                    try
                    {
                        PluginLog.Information("Auto start recroding");
                        this.ui.SetRecordingDir();
                        this.obs.StartRecord();
                    }
                    catch (ErrorResponseException err)
                    {
                        PluginLog.Warning("Start Recording Error: {0}", err);
                    }
                }
                lastCountdownValue = this.state.CountDownValue;
            });
            this.stopWatchHook = new StopWatchHook(state, SigScanner, Condition, GameInteropProvider);

            PluginLog.Information("stopWatchHook");
            this.commandManager = new PluginCommandManager<Plugin>(this, Commands);

            if (config.Password.Length > 0)
            {
                TryConnect(config.Address, config.Password);
            }

            ClientState.TerritoryChanged += onTerritoryChanged;
        }
        public async Task StopRecordingAsync()
        {
            try
            {
                PluginLog.Information($"Stop recording in {config.StopRecordOnCombatDelay} seconds");
                _stoppingRecord = true;
                var delay = config.StopRecordOnCombatDelay;
                var isViewingCutScene = false;

                do
                {
                    _cts.Token.ThrowIfCancellationRequested();
                    await Task.Delay(1000);
                    delay -= 1;
                    isViewingCutScene = await Framework.RunOnFrameworkThread(() => this.ClientState.LocalPlayer?.OnlineStatus.RowId == 15);
                    PluginLog.Information($"isViewingCutScene: {isViewingCutScene}");
                } while (delay > 0 || (config.DontStopInCutscene && isViewingCutScene));

                PluginLog.Information("Auto stop recording");
                this.obs.StopRecord();
            }
            catch (ErrorResponseException err)
            {
                PluginLog.Warning("Stop Recording Error: {0}", err);
            }
            finally
            {
                _stoppingRecord = false;
                _cts.Dispose();
                _cts = new CancellationTokenSource();
            }
        }

        private void OpenConfigUi()
        {
            this.ui.IsVisible = true;
        }

        private void OpenMainUi()
        {
            this.ui.IsVisible = true;
        }

        public async void TryConnect(string url, string password)
        {
            if (_connectLock)
            {
                return;
            }
            try
            {
                _connectLock = true;
                await Task.Run(() => obs.Connect(url, password));
                ConnectionFailed = false;
            }
            catch (AuthFailureException)
            {
                _ = Task.Run(() => obs.Disconnect());
                ConnectionFailed = true;
            }
            catch (Exception e)
            {
                PluginLog.Error("Connection error {0}", e);
            }
            finally
            {
                _connectLock = false;
            }
        }
        
        private void onConnect(object sender, EventArgs e)
        {
            Connected = true;
            PluginLog.Information("OBS connected: {0}", config.Address);
            versionInfo = obs.GetVersion();
            var pluginVersion = versionInfo.PluginVersion;
            var pVersion = new Version(pluginVersion);
            if (pVersion < new Version(minimumPluginVersion))
            {
                string errMsg = $"Invalid obs-websocket-plugin version, needs {minimumPluginVersion}, having {pluginVersion}";
                PluginLog.Error(errMsg);
                Chat.PrintError($"[OBSPlugin] {errMsg}");
                this.obs.Disconnect();
                return;
            }
            var streamStatus = obs.GetStreamStatus();
            if (streamStatus.IsActive)
                onStreamingStateChange(obs, new StreamStateChangedEventArgs(new OutputStateChanged() { IsActive = true, StateStr = nameof(OutputState.OBS_WEBSOCKET_OUTPUT_STARTED) }));
            else
                onStreamingStateChange(obs, new StreamStateChangedEventArgs(new OutputStateChanged() { IsActive = false, StateStr = nameof(OutputState.OBS_WEBSOCKET_OUTPUT_STOPPED) }));
            var recordStatus = obs.GetRecordStatus();
            if (recordStatus.IsRecording)
                onRecordingStateChange(obs, new RecordStateChangedEventArgs(new RecordStateChanged() { IsActive = true, StateStr = nameof(OutputState.OBS_WEBSOCKET_OUTPUT_STARTED) }));
            else
                onRecordingStateChange(obs, new RecordStateChangedEventArgs(new RecordStateChanged() { IsActive = false, StateStr = nameof(OutputState.OBS_WEBSOCKET_OUTPUT_STOPPED) }));
            var replayBufferActive = obs.GetReplayBufferStatus();
            if (replayBufferActive)
                onReplayBufferStateChange(obs, new ReplayBufferStateChangedEventArgs(new OutputStateChanged() { IsActive = true, StateStr = nameof(OutputState.OBS_WEBSOCKET_OUTPUT_STARTED) }));
            else
                onReplayBufferStateChange(obs, new ReplayBufferStateChangedEventArgs(new OutputStateChanged() { IsActive = false, StateStr = nameof(OutputState.OBS_WEBSOCKET_OUTPUT_STOPPED) }));
            if (config.RecordDir.Equals(String.Empty))
            {
                var recordDir = obs.GetRecordDirectory();
                config.RecordDir = recordDir;
                // config.FilenameFormat = obs.GetFilenameFormatting();
                config.Save();
            }

            keepAliveTokenSource = new CancellationTokenSource();
            CancellationToken keepAliveToken = keepAliveTokenSource.Token;
            Task statPollKeepAlive = Task.Factory.StartNew(() =>
            {
                while (true)
                {
                    Thread.Sleep(keepAliveInterval);
                    try
                    {
                        if (!obs.IsConnected)
                        {
                            continue;
                        }
                        if (keepAliveToken.IsCancellationRequested)
                        {
                            break;
                        }
                        UpdateStreamStats(obs.GetStreamStatus());
                    }
                    catch (Exception ex)
                    {
                        PluginLog.Error("Error getting obs streaming status", ex);
                    }
                }
            }, keepAliveToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        private void UpdateStreamStats(OutputStatus data)
        {
            streamStats = data;
        }

        private void onDisconnect(object sender, ObsDisconnectionInfo e)
        {
            PluginLog.Information("OBS disconnected: {0}", config.Address);
            Connected = false;
        }
       
        /*
        private void onStreamData(OBSWebsocket sender, StreamStatus data)
        {
            streamStatus = data;
        }
        */

        private void onStreamingStateChange(object sender, StreamStateChangedEventArgs newState)
        {
            obsStreamStatus = newState.OutputState.State;
        }

        private void onRecordingStateChange(object sender, RecordStateChangedEventArgs newState)
        {
            obsRecordStatus = newState.OutputState.State;
        }

        private void onReplayBufferStateChange(object sender, ReplayBufferStateChangedEventArgs newState)
        {
            obsReplayBufferStatus = newState.OutputState.State;
        }

        [Command("/obs")]
        [HelpMessage("Open OBSPlugin config panel.")]
        public unsafe void ObsCommand(string command, string args)
        {
            if (string.IsNullOrEmpty(args))
            {
                this.ui.IsVisible = !this.ui.IsVisible;
                return;
            }

            string[] commandParts = args.Split(' ', 2);
            string mainCommand = commandParts[0];
            string commandArgs = commandParts.Length > 1 ? commandParts[1] : "";

            // Switching to a switch statement makes adding new main-commands easier and neater.
            switch (mainCommand)
            {
                case "config":
                    this.ui.IsVisible = !this.ui.IsVisible;
                    break;

                case "on":
                    this.config.Enabled = true;
                    this.config.Save();
                    break;

                case "off":
                    this.config.Enabled = false;
                    this.config.Save();
                    break;

                case "toggle":
                    this.config.Enabled = !this.config.Enabled;
                    this.config.Save();
                    break;

                case "update":
                    this.ui.UpdateGameUI();
                    break;

                case "replay":
                    if (!Connected) break;
                    HandleReplayCommand(commandArgs);
                    break;

                case "stream":
                    if (!Connected) break;
                    HandleStreamCommand(commandArgs);
                    break;

                case "record":
                    if (!Connected) break;
                    HandleRecordCommand(commandArgs);
                    break;

                case "audio":
                    if (!Connected) break;
                    HandleAudioCommand(commandArgs);
                    break;

                case "scene":
                    if (!Connected) break;
                    HandleSceneCommand(commandArgs);
                    break;

                default:
                    Chat.PrintError($"[OBSPlugin] {args} is not a valid command.");
                    break;
            }
        }

        // Handles replay buffer options.
        private void HandleReplayCommand(string args)
        {
            if (string.IsNullOrWhiteSpace(args))
            {
                Chat.PrintError("[OBSPlugin] Replay command requires a subcommand: 'start', 'save', or 'stop'.");
                return;
            }

            switch (args.ToLowerInvariant())
            {
                case "start":
                    if (obsReplayBufferStatus != OutputState.OBS_WEBSOCKET_OUTPUT_STARTED)
                    {
                        obs.StartReplayBuffer();
                        Chat.Print("[OBSPlugin] Started replay buffer.");
                    }
                    else
                    {
                        Chat.PrintError("[OBSPlugin] The replay buffer is already active.");
                    }
                    break;

                case "save":
                    if (obsReplayBufferStatus == OutputState.OBS_WEBSOCKET_OUTPUT_STARTED)
                    {
                        obs.SaveReplayBuffer();
                        Chat.Print("[OBSPlugin] Replay saved: " + obs.GetLastReplayBufferReplay());
                    }
                    else
                    {
                        Chat.PrintError("[OBSPlugin] The replay buffer is not active.");
                    }
                    break;

                case "stop":
                    if (obsReplayBufferStatus == OutputState.OBS_WEBSOCKET_OUTPUT_STARTED)
                    {
                        obs.StopReplayBuffer();
                        Chat.Print("[OBSPlugin] Stopped replay buffer.");
                    }
                    else
                    {
                        Chat.PrintError("[OBSPlugin] The replay buffer is not active.");
                    }
                    break;

                default:
                    Chat.PrintError($"[OBSPlugin] '{args}' is not a valid subcommand. Valid subcommands are 'start', 'save', or 'stop'.");
                    break;
            }
        }

        // Handles stream activity options.
        private void HandleStreamCommand(string args)
        {
            if (string.IsNullOrWhiteSpace(args))
            {
                Chat.PrintError("[OBSPlugin] Stream command requires a subcommand: 'start' or 'stop'.");
                return;
            }

            switch (args.ToLowerInvariant())
            {
                case "start":
                    if (!obs.GetStreamStatus().IsActive)
                    {
                        obs.StartStream();
                        Chat.Print("[OBSPlugin] Started stream.");
                    }
                    else
                    {
                        Chat.PrintError("[OBSPlugin] The stream is already active.");
                    }
                    break;

                case "stop":
                    if (obs.GetStreamStatus().IsActive)
                    {
                        obs.StopStream();
                        Chat.Print("[OBSPlugin] Stopped stream.");
                    }
                    else
                    {
                        Chat.PrintError("[OBSPlugin] The stream is not active.");
                    }
                    break;

                default:
                    Chat.PrintError($"[OBSPlugin] '{args}' is not a valid subcommand. Valid subcommands are 'start' or 'stop'.");
                    break;
            }
        }

        // Handles recording options.
        private void HandleRecordCommand(string args)
        {
            if (string.IsNullOrWhiteSpace(args))
            {
                Chat.PrintError("[OBSPlugin] Record command requires a subcommand: 'start', 'stop', 'pause', or 'resume'.");
                return;
            }

            switch (args.ToLowerInvariant())
            {
                case "start":
                    if (!obs.GetRecordStatus().IsRecording)
                    {
                        obs.StartRecord();
                        Chat.Print("[OBSPlugin] Started recording.");
                    }
                    else
                    {
                        Chat.PrintError("[OBSPlugin] Recording is already active.");
                    }
                    break;

                case "stop":
                    if (obs.GetRecordStatus().IsRecording)
                    {
                        obs.StopRecord();
                        Chat.Print("[OBSPlugin] Stopped recording.");
                    }
                    else
                    {
                        Chat.PrintError("[OBSPlugin] Recording is not active.");
                    }
                    break;

                case "pause":
                    if (!obs.GetRecordStatus().IsRecordingPaused && obs.GetRecordStatus().IsRecording)
                    {
                        obs.PauseRecord();
                        Chat.Print("[OBSPlugin] Paused recording.");
                    }
                    else if (obs.GetRecordStatus().IsRecordingPaused)
                    {
                        Chat.PrintError("[OBSPlugin] Recording is already paused.");
                    }
                    else
                    {
                        Chat.PrintError("[OBSPlugin] Cannot pause as recording is not active.");
                    }
                    break;

                case "resume":
                    if (obs.GetRecordStatus().IsRecordingPaused)
                    {
                        obs.ResumeRecord();
                        Chat.Print("[OBSPlugin] Resumed recording.");
                    }
                    else if (!obs.GetRecordStatus().IsRecording)
                    {
                        Chat.PrintError("[OBSPlugin] Cannot resume as recording is not active.");
                    }
                    else
                    {
                        Chat.PrintError("[OBSPlugin] Recording is not paused.");
                    }
                    break;

                default:
                    Chat.PrintError($"[OBSPlugin] '{args}' is not a valid subcommand. Valid subcommands are 'start', 'stop', 'pause', or 'resume'.");
                    break;
            }
        }

        // Allows for muting and unmuting of an audio source.
        private void HandleAudioCommand(string args)
        {
            if (string.IsNullOrWhiteSpace(args))
            {
                Chat.PrintError("[OBSPlugin] Command requires a subcommand followed by an audio device name: 'mute <device_name>' or 'unmute <device_name>'.");
                return;
            }

            int firstSpaceIndex = args.IndexOf(' ');

            string command = args;
            string systemName = null;

            if (firstSpaceIndex != -1)
            {
                command = args.Substring(0, firstSpaceIndex).ToLowerInvariant();
                systemName = args.Substring(firstSpaceIndex + 1).Trim();
            }

            if (command.Equals("mute") || command.Equals("unmute"))
            {
                if (string.IsNullOrWhiteSpace(systemName))
                {
                    Chat.PrintError("[OBSPlugin] Audio commands need an audio device name to function.");
                    return;
                }

                switch (command)
                {
                    case "mute":
                        obs.SetInputMute(systemName, true);
                        Chat.Print($"[OBSPlugin] Muted {systemName}.");
                        break;

                    case "unmute":
                        obs.SetInputMute(systemName, false);
                        Chat.Print($"[OBSPlugin] Unmuted {systemName}.");
                        break;
                }
            }
            else
            {
                Chat.PrintError("[OBSPlugin] Valid commands are 'mute <device_name>' and 'unmute <device_name>'.");
            }
        }

        // Allows us to manipulate/jump between scenes as we like.
        private void HandleSceneCommand(string args)
        {
            const string changeKeyword = "change";

            int firstSpaceIndex = args.IndexOf(' ');

            if (firstSpaceIndex == -1)
            {
                Chat.PrintError("[OBSPlugin] Valid subcommand is 'change <scene_name>'");
                return;
            }

            string command = args.Substring(0, firstSpaceIndex);
            string sceneName = args.Substring(firstSpaceIndex + 1).Trim();

            if (!command.Equals(changeKeyword))
            {
                Chat.PrintError("[OBSPlugin] Valid subcommand is 'change <scene_name>'");
                return;
            }

            if (string.IsNullOrWhiteSpace(sceneName))
            {
                Chat.PrintError("[OBSPlugin] Please provide a scene name to change to.");
                return;
            }

            obs.SetCurrentProgramScene(sceneName);
            Chat.Print($"[OBSPlugin] Scene changed to {sceneName}.");
        }

        internal void onTerritoryChanged(ushort tid)
        {
            if (!Connected || !config.Enabled) return;
            if (config.ResetReplayBufferDirByTerritory && obsReplayBufferStatus == OutputState.OBS_WEBSOCKET_OUTPUT_STARTED)
            {
                if (obsRecordStatus != OutputState.OBS_WEBSOCKET_OUTPUT_STARTED)
                {
                    new Task(() =>
                    {
                        ui.ResetReplayBufferRecordingDir();
                    }).Start();
                }
                else
                {
                    PluginLog.Debug("Recording is active, cannot reset replay buffer dir.");
                }
            }
        }

        #region IDisposable Support
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing) return;

            this.commandManager.Dispose();

            this.stopWatchHook.Dispose();

            PluginInterface.SavePluginConfig(this.config);

            PluginInterface.UiBuilder.Draw -= this.ui.Draw;
            PluginInterface.UiBuilder.OpenConfigUi -= this.OpenConfigUi;
            PluginInterface.UiBuilder.OpenMainUi -= this.OpenMainUi;

            ClientState.TerritoryChanged -= onTerritoryChanged;

            this.ui.Dispose();

            if (obs != null && this.Connected)
            {
                if (config.RecordDir.Length > 0)
                    obs.SetRecordDirectory(config.RecordDir);
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
