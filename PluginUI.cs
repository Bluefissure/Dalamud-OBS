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
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Lumina.Excel.GeneratedSheets;
using System.IO;

namespace OBSPlugin
{
    public class PluginUI
    {
        private readonly Plugin Plugin;
        private bool isThreadRunning = true;
        internal BlockingCollection<Blur> BlurItemsToAdd = new (10000);
        internal BlockingCollection<Blur> BlurItemsToRemove = new (10000);
        public Dictionary<string, Blur> BlurDict = new();
        public Configuration Config => Plugin.config;
        private int UIErrorCount = 0;
        Blur[] PartyMemberBlurList = new Blur[8];

        public bool IsVisible { get; set; }
        public PluginUI(Plugin plugin)
        {
            Plugin = plugin;
            InitAddConsuming();
            InitRemoveConsuming();
        }

        private void InitAddConsuming()
        {
            Task.Run(() =>
            {
                while (!BlurItemsToAdd.IsCompleted && isThreadRunning)
                {
                    Blur blur = null;
                    try
                    {
                        blur = BlurItemsToAdd.Take();
                    }
                    catch (InvalidOperationException) { }

                    if (blur != null)
                    {
                        if (BlurDict.TryGetValue(blur.Name, out Blur latestBlur))
                        {
                            if (blur.LastEdit.CompareTo(latestBlur.LastEdit) < 0)
                            {
                                continue;
                            }

                        }
                        OBSAddOrUpdateBlur(blur);
                    }
                }
                PluginLog.Information("No more OBS blurs to add.");
            });

        }
        private void InitRemoveConsuming()
        {
            Task.Run(() =>
            {
                while (!BlurItemsToRemove.IsCompleted && isThreadRunning)
                {
                    Blur blur = null;
                    try
                    {
                        blur = BlurItemsToRemove.Take();
                    }
                    catch (InvalidOperationException) { }

                    if (blur != null)
                    {
                        OBSRemoveBlur(blur);
                    }
                }
                PluginLog.Information("No more OBS blurs to add.");
            });
        }

        public void Draw()
        {
            try
            {
                Plugin.stopWatchHook?.Update();
            }
            catch(Exception e)
            {
                PluginLog.Error("Error at updating stopwatch: {0}", e);
            }

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
                        if (ImGui.BeginChild("Stream##SettingsRegion"))
                        {
                            DrawStream();
                            ImGui.EndChild();
                        }
                        ImGui.EndTabItem();
                    }
                    if (ImGui.BeginTabItem("Record##Tab"))
                    {
                        if (ImGui.BeginChild("Record##SettingsRegion"))
                        {
                            DrawRecord();
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

        private bool OBSAddOrUpdateBlur(Blur blur)
        {
            if (!Plugin.Connected) return false;

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
                settings["Filter.Blur.Type"] = "dual_filtering";
                settings["Filter.Blur.Size"] = blur.Size;
                if (created)
                {
                    Plugin.obs.AddFilterToSource(sourceName, blur.Name, "streamfx-filter-blur", settings);
                } else
                {
                    Plugin.obs.SetSourceFilterSettings(sourceName, blur.Name, settings);
                }
                Plugin.obs.SetSourceFilterVisibility(sourceName, blur.Name, blur.Enabled);
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

        private bool OBSRemoveBlur(Blur blur)
        {
            if (!Plugin.Connected) return false;
            bool removed;
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

        private bool OBSRemoveBlurs(string blurNamePrefix)
        {
            if (!Plugin.Connected) return false;
            try
            {
                var filters = Plugin.obs.GetSourceFilters(Config.SourceName);
                foreach (var filter in filters)
                {
                    if (filter.Name.StartsWith(blurNamePrefix))
                    {
                        Plugin.obs.RemoveFilterFromSource(Config.SourceName, filter.Name);
                    }
                }
                PluginLog.Debug("Deleted all blurs starting with {0}", blurNamePrefix);
            }
            catch (Exception e)
            {
                PluginLog.Error("Failed deleting blurs: {0}", e);
                return false;
            }
            return true;
        }

        internal unsafe void UpdateGameUI()
        {
            if (!Config.Enabled) return;
            if (!Plugin.Connected) return;
            if (Plugin.ClientState.LocalPlayer == null) return;
            try
            {
                UpdateChatLog("ChatLog");
                UpdateChatLog("ChatLogPanel_0");
                UpdateChatLog("ChatLogPanel_1");
                UpdateChatLog("ChatLogPanel_2");
                UpdateChatLog("ChatLogPanel_3");
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
            try
            {
                UpdateCharacter();
            }
            catch (Exception e)
            {
                PluginLog.Error("Error Updating Character UI: {0}", e);
                Config.CharacterBlur = false;
                UIErrorCount++;
                Config.Save();
            }
            try
            {
                UpdateFridendList();
            }
            catch (Exception e)
            {
                PluginLog.Error("Error Updating FriendList UI: {0}", e);
                Config.FriendListBlur = false;
                UIErrorCount++;
                Config.Save();
            }
            try
            {
                UpdateHotbar();
            }
            catch (Exception e)
            {
                PluginLog.Error("Error Updating Hotbar UI: {0}", e);
                Config.HotbarBlur = false;
                UIErrorCount++;
                Config.Save();
            }
            try
            {
                UpdateCastBar();
            }
            catch (Exception e)
            {
                PluginLog.Error("Error Updating CastBar UI: {0}", e);
                Config.CastBarBlur = false;
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
                && BlurDict.TryGetValue(name, out Blur existingBlur)
                && existingBlur.Enabled)
                ImGui.GetForegroundDrawList(ImGui.GetMainViewport()).AddRect(position, position + size, 0xFFFF0000);
            var (top, bottom, left, right) = GetUIRect(position.X,
                position.Y,
                size.X,
                size.Y);
            Blur blur = new(name, top, bottom, left, right, Config.BlurSize);
            blur.Enabled = nodeVisible;
            return blur;
        }

        private unsafe void UpdateBlur(Blur blur)
        {
            blur.LastEdit = DateTime.Now;
            if (BlurDict.TryGetValue(blur.Name, out Blur existingBlur))
            {
                if (!blur.Equals(existingBlur))
                {
                    BlurDict[blur.Name] = blur;
                    if (Config.BlurAsync)
                    {
                        BlurItemsToAdd.Add(blur);
                    } else
                    {
                        OBSAddOrUpdateBlur(blur);
                    }
                }
            } else
            {
                BlurDict[blur.Name] = blur;
                BlurItemsToAdd.Add((Blur)blur.Clone());
            }
        }

        private unsafe void UpdateChatLog(string ChatLogWindowName)
        {
            if (!Config.ChatLogBlur) return;
            var chatLogAddress = Plugin.GameGui.GetAddonByName(ChatLogWindowName, 1);
            if (chatLogAddress == IntPtr.Zero) return;
            var chatLog = (AtkUnitBase*)chatLogAddress;
            if (chatLog->UldManager.NodeListCount <= 0) return;
            var chatLogNode = chatLog->UldManager.NodeList[0];
            UpdateBlur(GetBlurFromNode(chatLogNode, ChatLogWindowName));
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
                        if (childChildNode != null && childChildNode->Type == NodeType.Text )
                        {
                            if (childChildNode->NodeID == 16 && childChildIsVisible)
                            {
                                PartyMemberBlurList[partyMemberCount] = GetBlurFromNode(childChildNode, $"PartyList_{partyMemberCount}");
                                existingBlur.Add($"PartyList_{partyMemberCount}");
                                partyMemberCount++;
                                break;
                            }
                        }
                    }
                }
            }
            var blursToRemove = BlurDict.Values.Where(blur => blur.Name.StartsWith("PartyList") && !existingBlur.Contains(blur.Name));
            if (blursToRemove.Any())
            {
                blursToRemove.ToList().ForEach(blur =>
                {
                    BlurItemsToRemove.Add(blur);
                    BlurDict.Remove(blur.Name);
                });
            }
            for (int i = 0; i < partyMemberCount; i++)
            {
                UpdateBlur(PartyMemberBlurList[i]);
            }
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
                UpdateBlur(pair.Value);
                existingBlur.Add(pair.Value.Name);
            }

            var blursToRemove = BlurDict.Values.Where(blur => blur.Name.StartsWith("NamePlate") && !existingBlur.Contains(blur.Name));
            if (blursToRemove.Any())
            {
                blursToRemove.ToList().ForEach(blur =>
                {
                    BlurItemsToRemove.Add(blur);
                    BlurDict.Remove(blur.Name);
                });
            }
        }


        private unsafe void UpdateTarget()
        {
            if (!Config.TargetBlur && !Config.TargetTargetBlur) return;
            Blur targetBlur = null;
            Blur targetTargetBlur = null;
            // uint partyMemberCount = 0;
            var targetInfoAddress = Plugin.GameGui.GetAddonByName("_TargetInfo", 1);
            if (targetInfoAddress == IntPtr.Zero) return;
            var targetInfo = (AtkUnitBase*)targetInfoAddress;
            if (!GetNodeVisible(targetInfo->UldManager.NodeList[0]))
            {
                targetInfoAddress = Plugin.GameGui.GetAddonByName("_TargetInfoMainTarget", 1);
                targetInfo = (AtkUnitBase*)targetInfoAddress;
            }
            int textIndex = 0;
            int totalText = 0;
            for (var i = 0; i < targetInfo->UldManager.NodeListCount; i++)
            {
                var childNode = targetInfo->UldManager.NodeList[i];
                if (childNode != null && childNode->Type == NodeType.Text)
                {
                    totalText++;
                }
            }
            for (var i = 0; i < targetInfo->UldManager.NodeListCount; i++)
            {
                var childNode = targetInfo->UldManager.NodeList[i];
                var IsVisible = GetNodeVisible(childNode);
                if (childNode != null && childNode->Type == NodeType.Text)
                {
                    if(IsVisible)
                    {
                        if (textIndex == 2)
                        {
                            targetBlur = GetBlurFromNode(childNode, "Target");
                        }
                        else if (textIndex == totalText - 1)
                        {
                            targetTargetBlur = GetBlurFromNode(childNode, "TargetTarget");
                        }
                    }
                    textIndex++;
                }
            }
            if (Config.TargetBlur)
            {
                if (targetBlur == null)
                {
                    if (BlurDict.TryGetValue("Target", out Blur blurToRemove))
                    {
                        BlurItemsToRemove.Add(blurToRemove);
                        BlurDict.Remove(blurToRemove.Name);
                    }
                }
                else
                {
                    UpdateBlur(targetBlur);
                }
            }

            if (Config.TargetTargetBlur)
            {
                if (targetTargetBlur == null)
                {
                    if (BlurDict.TryGetValue("TargetTarget", out Blur blurToRemove))
                    {
                        BlurItemsToRemove.Add(blurToRemove);
                        BlurDict.Remove(blurToRemove.Name);
                    }
                }
                else
                {
                    UpdateBlur(targetTargetBlur);
                }
            }
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
                if (BlurDict.TryGetValue("FocusTarget", out Blur blurToRemove))
                {
                    BlurItemsToRemove.Add(blurToRemove);
                    BlurDict.Remove(blurToRemove.Name);
                }
            }
            else
            {
                UpdateBlur(focusTargetBlur);
            }
        }

        private unsafe void UpdateCharacter()
        {
            if (!Config.CharacterBlur) return;
            var characterAddress = Plugin.GameGui.GetAddonByName("Character", 1);
            var characterProfileAddress = Plugin.GameGui.GetAddonByName("CharacterProfile", 1);
            // character
            if (characterAddress == IntPtr.Zero) return;
            var character = (AtkUnitBase*)characterAddress;
            if (character->UldManager.NodeListCount <= 0) return;
            var childNode = character->UldManager.NodeList[80];
            UpdateBlur(GetBlurFromNode(childNode, "Character"));
            // characterProfile, may separate but i think it is fun
            if (characterProfileAddress == IntPtr.Zero) return;
            var characterProfile = (AtkUnitBase*)characterProfileAddress;
            if (characterProfile->UldManager.NodeListCount <= 0) return;
            var childNodeProfile = characterProfile->UldManager.NodeList[30];
            UpdateBlur(GetBlurFromNode(childNodeProfile, "CharacterProfile"));
        }

        private unsafe void UpdateFridendList()
        {
            if (!Config.FriendListBlur) return;
            var friendListAddress = Plugin.GameGui.GetAddonByName("FriendList", 1);
            if (friendListAddress == IntPtr.Zero) return;
            var friendList = (AtkUnitBase*)friendListAddress;
            if (friendList->UldManager.NodeListCount <= 0) return;
            var childNode = friendList->UldManager.NodeList[8];
            UpdateBlur(GetBlurFromNode(childNode, "FriendList"));
        }

        private unsafe void UpdateHotbar()
        {
            if (!Config.HotbarBlur || !Config.BlurredHotbars.Any()) return;
            foreach(var i in Config.BlurredHotbars)
            {
                var suffix = (i - 1).ToString("00");
                var hotbarAddress = Plugin.GameGui.GetAddonByName($"_ActionBar{(suffix == "00" ? string.Empty : suffix)}", 1);
                if (hotbarAddress == IntPtr.Zero) return;
                var hotbar = (AtkUnitBase*)hotbarAddress;
                var childNode = hotbar->UldManager.NodeList[0];
                UpdateBlur(GetBlurFromNode(childNode, $"Hotbar{suffix}"));
            }
        }
        private unsafe void UpdateCastBar()
        {
            if (!Config.CastBarBlur) return;
            var castbarAddress = Plugin.GameGui.GetAddonByName("_CastBar", 1);
            if (castbarAddress == IntPtr.Zero) return;
            var castbar = (AtkUnitBase*)castbarAddress;
            var childNode = castbar->UldManager.NodeList[1];
            UpdateBlur(GetBlurFromNode(childNode, "CastBar"));
        }

        // TODO IDK how to create the list in UI, maybe i can write a command
        private unsafe void UpdateCustomAddon(String addonName)
        {
            var addonAddress = Plugin.GameGui.GetAddonByName(addonName, 1);
            if (addonAddress == IntPtr.Zero) return;
            var addon = (AtkUnitBase*)addonAddress;
            if (addon->UldManager.NodeListCount <= 0) return;
            var childNode = addon->UldManager.NodeList[0];
            UpdateBlur(GetBlurFromNode(childNode, addonName));
        }

        private void DrawConnectionSettings()
        {
            if (ImGui.Checkbox("Enabled", ref Config.Enabled))
            {
                Config.Save();
            }
            ImGui.SameLine(ImGui.GetColumnWidth() - 80);
            ImGui.TextColored(Plugin.Connected ? new(0, 1, 0, 1) : new(1, 0, 0, 1),
                Plugin.Connected ? "Connected" : "Disconnected");
            if (ImGui.InputText("Server Address", ref Config.Address, 128))
            {
                Config.Save();
            }
            if (ImGui.InputText("Password", ref Config.Password, 128, ImGuiInputTextFlags.Password))
            {
                Config.Save();
            }
            string connectionButtonText = Plugin.Connected ? "Disconnect" : "Connect";
            if (ImGui.Button(connectionButtonText))
            {
                if (Plugin.Connected)
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
            if (ImGui.Checkbox("Asynchronous Update", ref Config.BlurAsync))
            {
                Config.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Asynchronizly update the blur filters in obs.\n" +
                    "Asynchronizly update will cause slight delays in updating the filters but has a better performance.");
            if (ImGui.Checkbox("Draw Blur Rect", ref Config.DrawBlurRect))
            {
                Config.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Draw the blurred content with a blue boundary.");
            if (ImGui.InputText("Source Name", ref Config.SourceName, 128))
            {
                Config.Save();
            }
            if (ImGui.DragInt("Blur Size", ref Config.BlurSize, 1, 1, 16))
            {
                foreach (var blur in BlurDict.Values)
                {
                    blur.Size = Config.BlurSize;
                    BlurItemsToAdd.Add((Blur)blur.Clone());
                }
                Config.Save();
            }
            ImGui.Separator();
            if (ImGui.Checkbox("ChatLog", ref Config.ChatLogBlur))
            {
                if (!Config.ChatLogBlur)
                {
                    Blur chatLogBlur = null;
                    if (BlurDict.TryGetValue("ChatLog", out chatLogBlur))
                    {
                        chatLogBlur.Enabled = false;
                        PluginLog.Debug("Turn off {0}", chatLogBlur.Name);
                        BlurItemsToAdd.Add((Blur)chatLogBlur.Clone());
                    }
                    if (BlurDict.TryGetValue("ChatLogPanel_0", out chatLogBlur))
                    {
                        chatLogBlur.Enabled = false;
                        PluginLog.Debug("Turn off {0}", chatLogBlur.Name);
                        BlurItemsToAdd.Add((Blur)chatLogBlur.Clone());
                    }
                    if (BlurDict.TryGetValue("ChatLogPanel_1", out chatLogBlur))
                    {
                        chatLogBlur.Enabled = false;
                        PluginLog.Debug("Turn off {0}", chatLogBlur.Name);
                        BlurItemsToAdd.Add((Blur)chatLogBlur.Clone());
                    }
                    if (BlurDict.TryGetValue("ChatLogPanel_2", out chatLogBlur))
                    {
                        chatLogBlur.Enabled = false;
                        PluginLog.Debug("Turn off {0}", chatLogBlur.Name);
                        BlurItemsToAdd.Add((Blur)chatLogBlur.Clone());
                    }
                    if (BlurDict.TryGetValue("ChatLogPanel_3", out chatLogBlur))
                    {
                        chatLogBlur.Enabled = false;
                        PluginLog.Debug("Turn off {0}", chatLogBlur.Name);
                        BlurItemsToAdd.Add((Blur)chatLogBlur.Clone());
                    }
                }
                Config.Save();
            }
            if (ImGui.Checkbox("PartyList", ref Config.PartyListBlur))
            {
                if (!Config.PartyListBlur)
                {
                    var blursToTurnOff = BlurDict.Values.Where(blur => blur.Name.StartsWith("PartyList"));
                    foreach (Blur blur in blursToTurnOff)
                    {
                        blur.Enabled = false;
                        PluginLog.Debug("Turn off {0}", blur.Name);
                        BlurItemsToAdd.Add((Blur)blur.Clone());
                    }
                }
                Config.Save();
            }
            if (ImGui.Checkbox("Target", ref Config.TargetBlur))
            {
                if (!Config.TargetBlur)
                {
                    Blur targetBlur = null;
                    if (BlurDict.TryGetValue("Target", out targetBlur))
                    {
                        targetBlur.Enabled = false;
                        PluginLog.Debug("Turn off {0}", targetBlur.Name);
                        BlurItemsToAdd.Add((Blur)targetBlur.Clone());
                    }
                }
                Config.Save();
            }

            if (ImGui.Checkbox("TargetTarget", ref Config.TargetTargetBlur))
            {
                if (!Config.TargetTargetBlur)
                {
                    Blur targetTargetBlur = null;
                    if (BlurDict.TryGetValue("TargetTarget", out targetTargetBlur))
                    {
                        targetTargetBlur.Enabled = false;
                        PluginLog.Debug("Turn off {0}", targetTargetBlur.Name);
                        BlurItemsToAdd.Add((Blur)targetTargetBlur.Clone());
                    }
                }
                Config.Save();
            }

            if (ImGui.Checkbox("FocusTarget", ref Config.FocusTargetBlur))
            {
                if (!Config.FocusTargetBlur)
                {
                    Blur focusTargetBlur = null;
                    if (BlurDict.TryGetValue("FocusTarget", out focusTargetBlur))
                    {
                        focusTargetBlur.Enabled = false;
                        PluginLog.Debug("Turn off {0}", focusTargetBlur.Name);
                        BlurItemsToAdd.Add((Blur)focusTargetBlur.Clone());
                    }
                }
                Config.Save();
            }
            if (ImGui.Checkbox("Character", ref Config.CharacterBlur))
            {
                if (!Config.CharacterBlur)
                {
                    Blur characterBlur = null;
                    if (BlurDict.TryGetValue("Character", out characterBlur))
                    {
                        characterBlur.Enabled = false;
                        PluginLog.Debug("Turn off {0}", characterBlur.Name);
                        BlurItemsToAdd.Add((Blur)characterBlur.Clone());
                    }
                }
                Config.Save();

            }
            if (ImGui.Checkbox("FriendList", ref Config.FriendListBlur))
            {
                if (!Config.FriendListBlur)
                {
                    Blur friendListBlur = null;
                    if (BlurDict.TryGetValue("FriendList", out friendListBlur))
                    {
                        friendListBlur.Enabled = false;
                        PluginLog.Debug("Turn off {0}", friendListBlur.Name);
                        BlurItemsToAdd.Add((Blur)friendListBlur.Clone());
                    }
                }
                Config.Save();
            }
            if (ImGui.Checkbox("Hotbar", ref Config.HotbarBlur))
            {
                if (!Config.HotbarBlur)
                {
                    var hotbars = BlurDict.Where(x => x.Key.Length >= 8 && x.Key[..6] == "Hotbar");
                    if (hotbars.Any())
                    {
                        PluginLog.Debug("Turn off HotbarBlur");
                        foreach(var i in hotbars)
                        {
                            i.Value.Enabled = false;
                            BlurItemsToAdd.Add((Blur)i.Value.Clone());
                        }
                    }
                }
                Config.Save();
            }
            if (Config.HotbarBlur)
            {
                ImGui.SameLine();
                var numbers = string.Join(",", Config.BlurredHotbars.Select(x => x.ToString()));
                ImGui.InputTextWithHint(string.Empty, "hotbar number splitted by comma", ref numbers, 32);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Adding hotbar will effect instantly, but remove need to re-enable hotbar blur to make it change.");
                try
                {
                    Config.BlurredHotbars = numbers.Trim().Replace("，", ",").Split(",").Select(x => int.Parse(x)).ToArray();
                }
                catch { }
            }
            if (ImGui.Checkbox("CastBar", ref Config.CastBarBlur))
            {
                if (!Config.CastBarBlur)
                {
                    Blur castbarBlur = null;
                    if (BlurDict.TryGetValue("CastBar", out castbarBlur))
                    {
                        castbarBlur.Enabled = false;
                        PluginLog.Debug("Turn off {0}", castbarBlur.Name);
                        BlurItemsToAdd.Add((Blur)castbarBlur.Clone());
                    }
                }
                Config.Save();
            }
            /*
            if (ImGui.Checkbox("NamePlate", ref Config.NamePlateBlur))
            {
                if (!Config.NamePlateBlur)
                {
                    OBSRemoveBlurs("NamePlate");
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
            */
            if (Config.BlurAsync)
            {
                ImGui.Separator();
                ImGui.Text($"Current #BlurItemsToAdd: {BlurItemsToAdd.Count}");
                ImGui.Text($"Current #BlurItemsToRemove: {BlurItemsToRemove.Count}");
            }

        }

        private void DrawAbout()
        {
            // ImGui.Text("This plugin is still WIP, lot of functions are still in development.");

            ImGui.Text("You need to install two plugins in your OBS for this plugin to work.");

            ImGui.Separator();

            ImGui.Text("For OBS v27:");
            ImGui.BulletText("");
            ImGui.SameLine();
            if (ImGui.Button("StreamFX 0.11.1"))
            {
                try
                {
                    Process.Start(new ProcessStartInfo()
                    {
                        FileName = "https://github.com/Xaymar/obs-StreamFX/releases/tag/0.11.1",
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
            if (ImGui.Button("OBS-websocket 4.9.1"))
            {
                try
                {
                    Process.Start(new ProcessStartInfo()
                    {
                        FileName = "https://github.com/obsproject/obs-websocket/releases/tag/4.9.1",
                        UseShellExecute = true,
                    });
                }
                catch (Exception ex)
                {
                    PluginLog.Error(ex, "Could not open OBS-websocket url");
                }
            }
            ImGui.SameLine();
            ImGui.Text("You need to set a password and provide it in the #Connection tab.");

            ImGui.Separator();

            ImGui.Text("For OBS v28+:");
            ImGui.BulletText("");
            ImGui.SameLine();
            if (ImGui.Button("StreamFX 0.12.0 alpha"))
            {
                try
                {
                    Process.Start(new ProcessStartInfo()
                    {
                        FileName = "https://github.com/Xaymar/obs-StreamFX/releases/tag/0.12.0a117",
                        UseShellExecute = true,
                    });
                }
                catch (Exception ex)
                {
                    PluginLog.Error(ex, "Could not open StreamFX url");
                }
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("You probably need to uninstall & delete the old plugin if you installed 0.11.x in OBS 27.");
            ImGui.SameLine();
            ImGui.Text("Just download and install.");

            ImGui.BulletText("");
            ImGui.SameLine();
            if (ImGui.Button("OBS-websocket 4.9.1-compat"))
            {
                try
                {
                    Process.Start(new ProcessStartInfo()
                    {
                        FileName = "https://github.com/obsproject/obs-websocket/releases/tag/4.9.1-compat",
                        UseShellExecute = true,
                    });
                }
                catch (Exception ex)
                {
                    PluginLog.Error(ex, "Could not open OBS-websocket url");
                }
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("The latest version of the build-in obs-websocket plugin in OBS 28 lacks some of the APIs used in this plugin and therefore will be pending untill fully supported.");
            ImGui.SameLine();
            ImGui.Text("You need to set a password and provide it in the #Connection tab.");

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

            switch (Plugin.obsStreamStatus)
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
                if (!Plugin.Connected) return;
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

            ImGui.SameLine(ImGui.GetColumnWidth() - 80);
            ImGui.TextColored(Plugin.obsStreamStatus == OutputState.Started ? new(0, 1, 0, 1) : new(1, 0, 0, 1),
                Plugin.obsStreamStatus == OutputState.Started ? "Streaming" : "Stopped");

            if (Plugin.streamStatus == null) return;

            ImGui.Text($"Stream time : {Plugin.streamStatus.TotalStreamTime} sec");
            ImGui.Text($"Kbits/sec : {Plugin.streamStatus.KbitsPerSec} kbit/s");
            ImGui.Text($"Bytes/sec : {Plugin.streamStatus.BytesPerSec} bytes/s");
            ImGui.Text($"Framerate : {Plugin.streamStatus.BytesPerSec} %");
            ImGui.Text($"Strain : {Plugin.streamStatus.Strain} FPS");
            ImGui.Text($"Dropped frames : {Plugin.streamStatus.DroppedFrames}");
            ImGui.Text($"Total frames : {Plugin.streamStatus.TotalFrames}");
        }

        internal void SetRecordingDir()
        {
            SetFilenameFormatting();
            if(Config.RecordDir == null || Config.RecordDir.Length == 0) return;
            if(Plugin.ClientState == null || Plugin.ClientState.TerritoryType == 0) return;

            var curDir = Config.RecordDir;
            if (Config.IncludeTerritory && Plugin.obsRecordStatus == OutputState.Stopped)
            {
                var terriIdx = Plugin.ClientState.TerritoryType;
                var terriName = Plugin.Data.GetExcelSheet<TerritoryType>().GetRow(terriIdx).Map.Value.PlaceName.Value.Name;
                curDir = Path.Combine(curDir, terriName);
            }

            Plugin.obs.SetRecordingFolder(curDir);
        }

        internal void SetFilenameFormatting()
        {
            if(Config.FilenameFormat == null || Config.FilenameFormat.Length == 0) return;
            if(Plugin.ClientState == null || Plugin.ClientState.TerritoryType == 0) return;

            var filenameFormat = Config.FilenameFormat;
            if (Config.ZoneAsSuffix && Plugin.obsRecordStatus == OutputState.Stopped)
            {
                var terriIdx = Plugin.ClientState.TerritoryType;
                var terriName = Plugin.Data.GetExcelSheet<TerritoryType>().GetRow(terriIdx).Map.Value.PlaceName.Value.Name;
                filenameFormat += "_" + terriName;
            }

            Plugin.obs.SetFilenameFormatting(filenameFormat);
        }

        private void DrawRecord()
        {

            string obsButtonText;

            switch (Plugin.obsRecordStatus)
            {
                case OutputState.Starting:
                    obsButtonText = "Record starting...";
                    break;

                case OutputState.Started:
                    obsButtonText = "Stop recording";
                    break;

                case OutputState.Stopping:
                    obsButtonText = "Record stopping...";
                    break;

                case OutputState.Stopped:
                    obsButtonText = "Start recording";
                    break;

                default:
                    obsButtonText = "State unknown";
                    break;
            }

            if (ImGui.Button(obsButtonText))
            {
                if (!Plugin.Connected) return;
                try
                {
                    SetRecordingDir();
                    Plugin.obs.ToggleRecording();
                }
                catch (Exception e)
                {
                    PluginLog.Error("Error on toggle recording: {0}", e);
                    Plugin.Chat.PrintError("[OBSPlugin] Error on toggle recording, check log for details.");
                }
            }

            ImGui.SameLine(ImGui.GetColumnWidth() - 80);
            ImGui.TextColored(Plugin.obsRecordStatus == OutputState.Started ? new(0, 1, 0, 1) : new(1, 0, 0, 1),
                Plugin.obsRecordStatus == OutputState.Started ? "Recording" : "Stopped");

            if (ImGui.InputText("Recordings Directory", ref Config.RecordDir, 256, ImGuiInputTextFlags.EnterReturnsTrue))
            {
                Config.Save();
                if (Plugin.Connected)
                {
                    Plugin.obs.SetRecordingFolder(Config.RecordDir);
                    PluginLog.Information("Recording directory set to {0}", Config.RecordDir);
                }
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Enter to save");

            if (ImGui.Checkbox("Zone as subfolder", ref Config.IncludeTerritory))
            {
                Config.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("If selected, will save recordings to a subfolder named by the current zone name.");

            if (ImGui.Checkbox("Zone as suffix", ref Config.ZoneAsSuffix))
            {
                Config.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("If selected, will add a suffix named by the current zone name to recordings.");

            if (ImGui.Checkbox("Start Recording On Combat", ref Config.StartRecordOnCombat))
            {
                Config.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("If selected, will automatically start recording when combat starts.");

            if (ImGui.Checkbox("Start Recording On CountDown", ref Config.StartRecordOnCountDown))
            {
                Config.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("If selected, will automatically start recording when countdown starts.");

            if (ImGui.Checkbox("Stop Recording On Combat Over In", ref Config.StopRecordOnCombat))
            {
                Config.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("If selected, will automatically stop recording when combat is over in ");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(-1);
            if (ImGui.DragInt("", ref Config.StopRecordOnCombatDelay, 1, 0, 300, "%d second(s)"))
            {
                Config.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Delay of \"Stop Recording On Combat Over\" in seconds.");
            if (Config.StopRecordOnCombat && ImGui.Checkbox("Don't Stop Recording in cutscene", ref Config.DontStopInCutscene))
            {
                Config.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("If selected, will not stop recording if player is viewing cutscenes.");
            if (Config.StopRecordOnCombat && ImGui.Checkbox("Cancel Stop Recording On Combat Resume", ref Config.CancelStopRecordOnResume))
            {
                Config.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("If selected, will not stop recording if another starts before stop countdown.");
        }

        internal void Dispose()
        {
            if (Plugin.Connected)
            {
                foreach (Blur blur in BlurDict.Values)
                {
                    blur.Enabled = false;
                    PluginLog.Debug("Turn off {0}", blur.Name);
                    Plugin.obs.RemoveFilterFromSource(Config.SourceName, blur.Name);
                }
            }
            isThreadRunning = false;
        }

    }
}
