using Dalamud.Interface;
using Dalamud.Logging;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;
using Newtonsoft.Json.Linq;
using OBSPlugin.Objects;
using OBSWebsocketDotNet;
using OBSWebsocketDotNet.Types;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Linq;

namespace OBSPlugin
{
    public class PluginUI
    {
        private readonly Plugin Plugin;
        public PluginUI(Plugin plugin)
        {
            Plugin = plugin;
        }
        public Configuration Config => Plugin.config;
        private int UIErrorCount = 0;
        Blur[] PartyMemberBlurList = new Blur[8];

        public bool IsVisible { get; set; }

        public void Draw()
        {
            if (Config.UIDetection)
                UpdateGameUI();

            if (!IsVisible)
                return;

            ImGui.SetNextWindowSize(new Vector2(530, 450), ImGuiCond.FirstUseEver);
            bool configOpen = IsVisible;
            if (ImGui.Begin("OBS Plugin Config", ref configOpen))
            {
                IsVisible = configOpen;
                if (ImGui.BeginTabBar("TabBar"))
                {
                    if (ImGui.BeginTabItem("Connection##Tab"))
                    {
                        if (ImGui.BeginChild("Connection##SettingsRegion"))
                        {
                            DrawConnectionSettings();
                            ImGui.EndChild();
                        }
                        ImGui.EndTabItem();
                    }
                    if (ImGui.BeginTabItem("Stream##Tab"))
                    {
                        if (ImGui.BeginChild("Blur##SettingsRegion"))
                        {
                            DrawStream();
                            ImGui.EndChild();
                        }
                        ImGui.EndTabItem();
                    }
                    if (ImGui.BeginTabItem("Blur##Tab"))
                    {
                        if (ImGui.BeginChild("Blur##SettingsRegion"))
                        {
                            DrawBlurSettings();
                            ImGui.EndChild();
                        }
                        ImGui.EndTabItem();
                    }
                    if (ImGui.BeginTabItem("About##Tab"))
                    {
                        if (ImGui.BeginChild("Blur##SettingsRegion"))
                        {
                            DrawAbout();
                            ImGui.EndChild();
                        }
                        ImGui.EndTabItem();
                    }
                    ImGui.EndTabBar();
                }
                ImGui.End();
            }

        }

        private unsafe (float, float, float, float) GetUIRect(float X, float Y, float W, float H)
        {
            var size = ImGui.GetIO().DisplaySize;
            var width = size.X;
            var height = size.Y;
            var top = Y / height * 100;
            var left = X / width * 100;
            var bottom = (height - Y - H) / height * 100;
            var right = (width - X - W) / width * 100;
            return (top, bottom, left, right);
        }

        private bool AddOrUpdateBlur(Blur blur)
        {
            if (!Plugin.obs.IsConnected) return false;
            string sourceName = Config.SourceName;
            try
            {
                FilterSettings filter = null;
                var settings = new JObject();
                bool created = false;
                try
                {
                    filter = Plugin.obs.GetSourceFilterInfo(sourceName, blur.Name);
                    settings = filter.Settings;
                } catch
                {
                    created = true;
                }
                settings["Filter.Blur.Mask"] = true;
                settings["Filter.Blur.Mask.Region.Top"] = blur.Top;
                settings["Filter.Blur.Mask.Region.Bottom"] = blur.Bottom;
                settings["Filter.Blur.Mask.Region.Left"] = blur.Left;
                settings["Filter.Blur.Mask.Region.Right"] = blur.Right;
                settings["Filter.Blur.Mask.Type"] = 0;
                settings["Filter.Blur.Size"] = blur.Size;
                if (created)
                {
                    Plugin.obs.AddFilterToSource(sourceName, blur.Name, "streamfx-filter-blur", settings);
                } else
                {
                    Plugin.obs.SetSourceFilterSettings(sourceName, blur.Name, settings);
                }
                if (!created && filter != null && blur.Enabled != filter.IsEnabled)
                {
                    Plugin.obs.SetSourceFilterVisibility(sourceName, blur.Name, blur.Enabled);
                }
            }
            catch (ErrorResponseException e)
            {
                if (e.ToString().Contains("specified source doesn't exist"))
                {
                    Config.UIDetection = false;
                    var errMsg = $"Cannot find source \"{Config.SourceName}\", please check.";
                    PluginLog.Error(errMsg);
                    Plugin.Chat.PrintError($"[OBSPlugin] {errMsg}");
                    Config.Save();
                }
                return false;
            }
            catch (Exception e)
            {
                PluginLog.Error("Failed updating blur: {0}", e);
                return false;
            }
            PluginLog.Debug("Updated blur: {0}", blur.Name);
            return true;
        }

        private bool RemoveBlur(Blur blur)
        {
            if (!Plugin.obs.IsConnected) return false;
            bool removed = false;
            try
            {
                removed = Plugin.obs.RemoveFilterFromSource(Config.SourceName, blur.Name);
                PluginLog.Debug("Deleted blur: {0}", blur.Name);
            }
            catch (Exception e)
            {
                PluginLog.Error("Failed deleting blur: {0}", e);
                return false;
            }
            return removed;
        }

        internal unsafe void UpdateGameUI()
        {
            if (!Config.Enabled) return;
            if (!Plugin.Connected) return;
            if (Plugin.ClientState.LocalPlayer == null) return;
            try
            {
                UpdateChatLog();
            }
            catch (Exception e)
            {
                PluginLog.Error("Error Updating ChatLog UI: {0}", e);
                Config.ChatLogBlur = false;
                UIErrorCount++;
                Config.Save();
            }
            try
            {
                UpdatePartyList();
            }
            catch (Exception e)
            {
                PluginLog.Error("Error Updating PartyList UI: {0}", e);
                Config.PartyListBlur = false;
                UIErrorCount++;
                Config.Save();
            }
            try
            {
                UpdateTarget();
            }
            catch (Exception e)
            {
                PluginLog.Error("Error Updating Target UI: {0}", e);
                Config.TargetBlur = false;
                Config.TargetTargetBlur = false;
                UIErrorCount++;
                Config.Save();
            }
            try
            {
                UpdateFocusTarget();
            }
            catch (Exception e)
            {
                PluginLog.Error("Error Updating FocusTarget UI: {0}", e);
                Config.FocusTargetBlur = false;
                UIErrorCount++;
                Config.Save();
            }
            try
            {
                UpdateNamePlate();
            }
            catch (Exception e)
            {
                PluginLog.Error("Error Updating NamePlate UI: {0}", e);
                Config.NamePlateBlur = false;
                UIErrorCount++;
                Config.Save();
            }
            if (UIErrorCount > 1000)
            {
                var errMsg = "More than 1000 UI errors encountered, UI detection is turned off. " +
                    "Please open /xllog for more details.";
                PluginLog.Error(errMsg);
                Plugin.Chat.PrintError($"[OBSPlugin] {errMsg}");
                Config.UIDetection = false;
                Config.Save();
            }
        }


        private unsafe Vector2 GetNodePosition(AtkResNode* node)
        {
            var pos = new Vector2(node->X, node->Y);
            var par = node->ParentNode;
            while (par != null)
            {
                pos *= new Vector2(par->ScaleX, par->ScaleY);
                pos += new Vector2(par->X, par->Y);
                par = par->ParentNode;
            }

            return pos;
        }

        private unsafe Vector2 GetFloatingNodePosition(AtkResNode* node)
        {
            if (node == null) return new Vector2(0, 0);
            var scaledPosX = node->X - (node->Width * ((node->ScaleX - 1) / 2));
            var scaledPosY = node->Y - (node->Height * ((node->ScaleY - 1)));
            var pos = new Vector2(scaledPosX, scaledPosY);
            var par = node->ParentNode;
            if (par != null)
            {
                pos *= new Vector2(par->ScaleX, par->ScaleY);
                pos += this.GetFloatingNodePosition(node->ParentNode);
            }

            return pos;
        }

        private unsafe Vector2 GetNodeScale(AtkResNode* node)
        {
            if (node == null) return new Vector2(1, 1);
            var scale = new Vector2(node->ScaleX, node->ScaleY);
            while (node->ParentNode != null)
            {
                node = node->ParentNode;
                scale *= new Vector2(node->ScaleX, node->ScaleY);
            }

            return scale;
        }

        private unsafe bool GetNodeVisible(AtkResNode* node)
        {
            if (node == null) return false;
            while (node != null)
            {
                if ((node->Flags & (short)NodeFlags.Visible) != (short)NodeFlags.Visible) return false;
                node = node->ParentNode;
            }

            return true;
        }

        private unsafe Blur GetBlurFromNode(AtkResNode* node, string name, bool floating=false)
        {
            var position = floating ? GetFloatingNodePosition(node) : GetNodePosition(node);
            var scale = GetNodeScale(node);
            var nodeVisible = GetNodeVisible(node);
            var size = new Vector2(node->Width, node->Height) * scale;
            if (Config.DrawBlurRect && nodeVisible
                && Config.BlurList.Find(blur => blur.Name == name) != null
                && Config.BlurList.Find(blur => blur.Name == name).Enabled)
                ImGui.GetForegroundDrawList(ImGui.GetMainViewport()).AddRect(position, position + size, 0xFFFF0000);
            var (top, bottom, left, right) = GetUIRect(position.X,
                position.Y,
                size.X,
                size.Y);
            Blur blur = new(name, top, bottom, left, right, Config.BlurSize);
            blur.Enabled = nodeVisible;
            return blur;
        }
        private unsafe bool UpdateConfigBlur(Blur blur)
        {
            var _blur = Config.BlurList.Find(confBlur => confBlur.Name == blur.Name);
            if (_blur == null
                || _blur.Enabled != blur.Enabled
                || _blur.Top != blur.Top
                || _blur.Bottom != blur.Bottom
                || _blur.Left != blur.Left
                || _blur.Right != blur.Right
                || _blur.Size != blur.Size)
            {
                if (AddOrUpdateBlur(blur))
                {
                    if (_blur == null)
                    {
                        Config.BlurList.Add(blur);
                    }
                    else
                    {
                        _blur.Enabled = blur.Enabled;
                        _blur.Top = blur.Top;
                        _blur.Bottom = blur.Bottom;
                        _blur.Left = blur.Left;
                        _blur.Right = blur.Right;
                        _blur.Size = blur.Size;
                    }
                    return true;
                }
            }
            return false;
        }

        private unsafe void UpdateChatLog()
        {
            if (!Config.ChatLogBlur) return;
            var chatLogAddress = Plugin.GameGui.GetAddonByName("ChatLog", 1);
            if (chatLogAddress == IntPtr.Zero) return;
            bool created = false;
            var chatLog = (AtkUnitBase*)chatLogAddress;
            if (chatLog->UldManager.NodeListCount <= 0) return;
            var chatLogNode = chatLog->UldManager.NodeList[0];
            Blur blur = GetBlurFromNode(chatLogNode, "ChatLog");
            if (UpdateConfigBlur(blur))
            {
                Config.Save();
            }
        }

        private unsafe void UpdatePartyList()
        {
            if (!Config.PartyListBlur) return;
            HashSet<string> existingBlur = new();
            uint partyMemberCount = 0;
            var partyListAddress = Plugin.GameGui.GetAddonByName("_PartyList", 1);
            if (partyListAddress == IntPtr.Zero) return;
            var partyList = (AtkUnitBase*)partyListAddress;
            for (var i = 0; i < partyList->UldManager.NodeListCount; i++)
            {
                var childNode = partyList->UldManager.NodeList[i];
                var IsVisible = GetNodeVisible(childNode);
                if (childNode != null && (int)childNode->Type == 1006 && IsVisible)
                {
                    for (var j = 0; j < childNode->GetAsAtkComponentNode()->Component->UldManager.NodeListCount; j++)
                    {
                        var childChildNode = childNode->GetAsAtkComponentNode()->Component->UldManager.NodeList[j];
                        var childChildIsVisible = GetNodeVisible(childChildNode);
                        if (childChildNode != null && childChildNode->Type == NodeType.Text && childChildIsVisible)
                        {
                            Blur blur = GetBlurFromNode(childChildNode, $"PartyList_{partyMemberCount}");
                            PartyMemberBlurList[partyMemberCount] = blur;
                            existingBlur.Add($"PartyList_{partyMemberCount}");
                            partyMemberCount++;
                            break;
                        }
                    }
                }
            }
            Config.BlurList.RemoveAll(blur => {
                bool toDel = blur.Name.StartsWith("PartyList") && !existingBlur.Contains(blur.Name);
                if (toDel) RemoveBlur(blur);
                return toDel;
            });
            bool toSave = false;
            for (uint i = 0; i < partyMemberCount; i++)
            {
                var blur = PartyMemberBlurList[i];
                toSave |= UpdateConfigBlur(blur);
            }
            if (toSave) Config.Save();
        }

        private unsafe void UpdateNamePlate()
        {
            if (!Config.NamePlateBlur) return;
            Dictionary<string, Blur> namePlateBlurMap = new();
            HashSet<string> existingBlur = new();
            var namePlateAddress = Plugin.GameGui.GetAddonByName("NamePlate", 1);
            if (namePlateAddress == IntPtr.Zero) return;
            var namePlate = (AtkUnitBase*)namePlateAddress;
            for (var i = 0; i < namePlate->UldManager.NodeListCount; i++)
            {
                var childNode = namePlate->UldManager.NodeList[i];
                var IsVisible = GetNodeVisible(childNode);
                if (childNode != null && (int)childNode->Type == 1001 && IsVisible)
                {
                    var collisionNode = childNode->GetAsAtkComponentNode()->Component->UldManager.NodeList[0];
                    var collisionNodeIsVisible = GetNodeVisible(collisionNode);
                    if (collisionNode != null && collisionNode->Type == NodeType.Collision && collisionNodeIsVisible)
                    {
                        string blurName = $"NamePlate_{(ulong)collisionNode:X}";
                        namePlateBlurMap[blurName] = GetBlurFromNode(collisionNode, blurName, true);
                    }
                }
            }
            var namePlateBlurList = namePlateBlurMap.OrderBy(
                    pair => Math.Pow((pair.Value.Top - pair.Value.Bottom) / 2, 2) +
                            Math.Pow((pair.Value.Left - pair.Value.Right) / 2, 2)
                ).Take(Config.MaxNamePlateCount);
            foreach (KeyValuePair<string, Blur> pair in namePlateBlurList)
            {
                UpdateConfigBlur(pair.Value);
                existingBlur.Add(pair.Value.Name);
            }
            Config.BlurList.RemoveAll(blur => {
                bool toDel = blur.Name.StartsWith("NamePlate") && !existingBlur.Contains(blur.Name);
                if (toDel) RemoveBlur(blur);
                return toDel;
            });

            Config.Save();
        }


        private unsafe void UpdateTarget()
        {
            if (!Config.TargetBlur && !Config.TargetTargetBlur) return;
            Blur targetBlur = null;
            Blur targetTargetBlur = null;
            uint partyMemberCount = 0;
            var targetInfoAddress = Plugin.GameGui.GetAddonByName("_TargetInfo", 1);
            if (targetInfoAddress == IntPtr.Zero) return;
            var targetInfo = (AtkUnitBase*)targetInfoAddress;
            int textIndex = 0;
            for (var i = 0; i < targetInfo->UldManager.NodeListCount; i++)
            {
                var childNode = targetInfo->UldManager.NodeList[i];
                var IsVisible = GetNodeVisible(childNode);
                if (childNode != null && childNode->Type == NodeType.Text && IsVisible)
                {
                    if (textIndex == 1)
                    {
                        targetBlur = GetBlurFromNode(childNode, "Target");
                    } else if (textIndex == 2)
                    {
                        targetTargetBlur = GetBlurFromNode(childNode, "TargetTarget");
                    }
                    textIndex++;
                }
            }
            bool toSave = false;
            if (Config.TargetBlur)
            {
                if (targetBlur == null)
                {
                    Config.BlurList.RemoveAll(blur => {
                        bool toDel = blur.Name == "Target";
                        if (toDel) RemoveBlur(blur);
                        return toDel;
                    });
                }
                else
                {
                    toSave |= UpdateConfigBlur(targetBlur);
                }
            }

            if (Config.TargetTargetBlur)
            {
                if (targetTargetBlur == null)
                {
                    Config.BlurList.RemoveAll(blur => {
                        bool toDel = blur.Name == "TargetTarget";
                        if (toDel) RemoveBlur(blur);
                        return toDel;
                    });
                }
                else
                {
                    toSave |= UpdateConfigBlur(targetTargetBlur);
                }
            }
                
            if (toSave) Config.Save();
        }

        private unsafe void UpdateFocusTarget()
        {
            if (!Config.FocusTargetBlur) return;
            Blur focusTargetBlur = null;
            var focusTargetAddress = Plugin.GameGui.GetAddonByName("_FocusTargetInfo", 1);
            if (focusTargetAddress == IntPtr.Zero) return;
            var focusTargetInfo = (AtkUnitBase*)focusTargetAddress;
            for (var i = 0; i < focusTargetInfo->UldManager.NodeListCount; i++)
            {
                var childNode = focusTargetInfo->UldManager.NodeList[i];
                var IsVisible = GetNodeVisible(childNode);
                if (childNode != null && childNode->Type == NodeType.Text && IsVisible)
                {
                    focusTargetBlur = GetBlurFromNode(childNode, "FocusTarget");
                    break;
                }
            }
            if (focusTargetBlur == null)
            {
                Config.BlurList.RemoveAll(blur => {
                    bool toDel = blur.Name == "FocusTarget";
                    if (toDel) RemoveBlur(blur);
                    return toDel;
                });
            }
            else
            {
                UpdateConfigBlur(focusTargetBlur);
            }

            Config.Save();
        }

        private void DrawConnectionSettings()
        {
            if (ImGui.Checkbox("Enabled", ref Config.Enabled))
            {
                Config.Save();
            }
            ImGui.SameLine(ImGui.GetColumnWidth() - 80);
            ImGui.TextColored(Plugin.Connected ? new(0, 1, 0, 1) : new(1, 0, 0, 1),
                Plugin.obs.IsConnected ? "Connected" : "Disconnected");
            if (ImGui.InputText("Server Address", ref Config.Address, 128))
            {
                Config.Save();
            }
            if (ImGui.InputText("Password", ref Config.Password, 128, ImGuiInputTextFlags.Password))
            {
                Config.Save();
            }
            string connectionButtonText = Plugin.obs.IsConnected ? "Disconnect" : "Connect";
            if (ImGui.Button(connectionButtonText))
            {
                if (Plugin.obs.IsConnected)
                {
                    Plugin.obs.Disconnect();
                } else
                {
                    Plugin.TryConnect(Config.Address, Config.Password);
                }
            }
            if (Plugin.ConnectionFailed)
            {
                ImGui.SameLine();
                ImGui.Text("Authentication failed, check the address and password!");
            }
        }

        private void DrawBlurSettings()
        {
            if (ImGui.Checkbox("UI Detection", ref Config.UIDetection))
            {
                UIErrorCount = 0;
                Config.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Detect game UI elements together with game-rendering.\n" +
                    "May cause performance issues.\n" +
                    "If disabled, you need to manually call \"/obs update\" to update the blurs in obs.");
            if (ImGui.Checkbox("Draw Blur Rect", ref Config.DrawBlurRect))
            {
                Config.Save();
            }
            if (ImGui.InputText("Source Name", ref Config.SourceName, 128))
            {
                Config.Save();
            }
            if (ImGui.DragInt("Blur Size", ref Config.BlurSize, 1, 1, 128))
            {
                foreach (var blur in Config.BlurList)
                {
                    blur.Size = Config.BlurSize;
                    AddOrUpdateBlur(blur);
                }
                Config.Save();
            }
            ImGui.Separator();
            if (ImGui.Checkbox("ChatLog", ref Config.ChatLogBlur))
            {
                if (!Config.ChatLogBlur)
                {
                    var chatLogBlur = Config.BlurList.Find(blur => blur.Name == "ChatLog");
                    if (chatLogBlur != null)
                    {
                        chatLogBlur.Enabled = false;
                        AddOrUpdateBlur(chatLogBlur);
                        PluginLog.Debug("Turn off {0}", chatLogBlur.Name);
                    }
                }
                Config.Save();
            }
            if (ImGui.Checkbox("PartyList", ref Config.PartyListBlur))
            {
                if (!Config.PartyListBlur)
                {
                    var partyListBlurs = Config.BlurList.FindAll(blur => blur.Name.StartsWith("PartyList"));
                    for (int i = 0; i < Config.BlurList.Count; i++)
                    {
                        if (Config.BlurList[i].Name.StartsWith("PartyList"))
                        {
                            Config.BlurList[i].Enabled = false;
                            AddOrUpdateBlur(Config.BlurList[i]);
                            PluginLog.Debug("Turn off {0}", Config.BlurList[i].Name);
                        }
                    }
                }
                Config.Save();
            }
            if (ImGui.Checkbox("Target", ref Config.TargetBlur))
            {
                if (!Config.TargetBlur)
                {
                    var targetBlur = Config.BlurList.Find(blur => blur.Name == "Target");
                    if (targetBlur != null)
                    {
                        targetBlur.Enabled = false;
                        AddOrUpdateBlur(targetBlur);
                        PluginLog.Debug("Turn off {0}", targetBlur);
                    }
                }
                Config.Save();
            }

            if (ImGui.Checkbox("TargetTarget", ref Config.TargetTargetBlur))
            {
                if (!Config.TargetTargetBlur)
                {
                    var targetTargetBlur = Config.BlurList.Find(blur => blur.Name == "TargetTarget");
                    if (targetTargetBlur != null)
                    {
                        targetTargetBlur.Enabled = false;
                        AddOrUpdateBlur(targetTargetBlur);
                        PluginLog.Debug("Turn off {0}", targetTargetBlur);
                    }
                }
                Config.Save();
            }

            if (ImGui.Checkbox("FocusTarget", ref Config.FocusTargetBlur))
            {
                if (!Config.FocusTargetBlur)
                {
                    var focusTargetBlur = Config.BlurList.Find(blur => blur.Name == "FocusTarget");
                    if (focusTargetBlur != null)
                    {
                        focusTargetBlur.Enabled = false;
                        AddOrUpdateBlur(focusTargetBlur);
                        PluginLog.Debug("Turn off {0}", focusTargetBlur);
                    }
                }
                Config.Save();
            }

            if (ImGui.Checkbox("NamePlate", ref Config.NamePlateBlur))
            {
                if (!Config.NamePlateBlur)
                {
                    var namePlateBlurs = Config.BlurList.FindAll(blur => blur.Name.StartsWith("NamePlate"));
                    for (int i = 0; i < Config.BlurList.Count; i++)
                    {
                        if (Config.BlurList[i].Name.StartsWith("NamePlate"))
                        {
                            Config.BlurList[i].Enabled = false;
                            AddOrUpdateBlur(Config.BlurList[i]);
                            PluginLog.Debug("Turn off {0}", Config.BlurList[i].Name);
                        }
                    }
                }
                Config.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Blurring NamePlate is not recommended.\n" +
                    "This may cause severe performance issues.\n" +
                    "It's better to turn off name plate in game or limit the number of blurs to <= 8.");
            ImGui.SameLine();
            if (ImGui.DragInt("Max", ref Config.MaxNamePlateCount, 1, 1, 50))
            {
                Config.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Max number of nameplates to be blurred, sorted by the distance to the center.");

        }

        private void DrawAbout()
        {
            ImGui.Text("This plugin is still WIP, lot of functions are still in development.");

            ImGui.Text("You need to install two plugins in your OBS for this plugin to work:");

            ImGui.BulletText("");
            ImGui.SameLine();
            if (ImGui.Button("StreamFX"))
            {
                try
                {
                    Process.Start(new ProcessStartInfo()
                    {
                        FileName = "https://github.com/Xaymar/obs-StreamFX/releases/latest",
                        UseShellExecute = true,
                    });
                }
                catch (Exception ex)
                {
                    PluginLog.Error(ex, "Could not open StreamFX url");
                }
            }
            ImGui.SameLine();
            ImGui.Text("Just download and install.");

            ImGui.BulletText("");
            ImGui.SameLine();
            if (ImGui.Button("OBS-websocket"))
            {
                try
                {
                    Process.Start(new ProcessStartInfo()
                    {
                        FileName = "https://github.com/Palakis/obs-websocket/releases/latest",
                        UseShellExecute = true,
                    });
                }
                catch (Exception ex)
                {
                    PluginLog.Error(ex, "Could not open OBS-websocket url");
                }
            }
            ImGui.SameLine();
            ImGui.Text("You need to set a password and provide in the #Connection tab.");

            ImGui.NewLine();
            ImGui.Text("If you encountered any bugs please submit issues in");
            ImGui.SameLine();
            if (ImGui.Button("Github"))
            {
                try
                {
                    Process.Start(new ProcessStartInfo()
                    {
                        FileName = "https://github.com/Bluefissure/Dalamud-OBS",
                        UseShellExecute = true,
                    });
                }
                catch (Exception ex)
                {
                    PluginLog.Error(ex, "Could not open OBS-websocket url");
                }
            }
        }

        private void DrawStream()
        {
            string obsButtonText;

            switch (Plugin.obsStatus)
            {
                case OutputState.Starting:
                    obsButtonText = "Stream starting...";
                    break;

                case OutputState.Started:
                    obsButtonText = "Stop streaming";
                    break;

                case OutputState.Stopping:
                    obsButtonText = "Stream stopping...";
                    break;

                case OutputState.Stopped:
                    obsButtonText = "Start streaming";
                    break;

                default:
                    obsButtonText = "State unknown";
                    break;
            }

            if (ImGui.Button(obsButtonText))
            {
                try
                {
                    Plugin.obs.ToggleStreaming();
                }
                catch (Exception e)
                {
                    PluginLog.Error("Error on toggle streaming: {0}", e);
                    Plugin.Chat.PrintError("[OBSPlugin] Error on toggle streaming, check log for details.");
                }
            }

            if (Plugin.streamStatus == null) return;

            ImGui.Text($"Stream time : {Plugin.streamStatus.TotalStreamTime} sec");
            ImGui.Text($"Kbits/sec : {Plugin.streamStatus.KbitsPerSec} kbit/s");
            ImGui.Text($"Bytes/sec : {Plugin.streamStatus.BytesPerSec} bytes/s");
            ImGui.Text($"Framerate : {Plugin.streamStatus.BytesPerSec} %");
            ImGui.Text($"Strain : {Plugin.streamStatus.Strain} FPS");
            ImGui.Text($"Dropped frames : {Plugin.streamStatus.DroppedFrames}");
            ImGui.Text($"Total frames : {Plugin.streamStatus.TotalFrames}");
        }
    }
}
