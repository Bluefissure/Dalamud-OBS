using Dalamud.Configuration;
using Dalamud.Logging;
using Dalamud.Plugin;
using Newtonsoft.Json;
using OBSPlugin.Objects;
using System.Collections.Generic;

namespace OBSPlugin
{
    public class Configuration : IPluginConfiguration
    {
        public int Version { get; set; }

        // Add any other properties or methods here.
        [JsonIgnore] private DalamudPluginInterface pluginInterface;

        public bool Enabled = true;
        public bool UIDetection = true;
        public string SourceName = "FFXIV";
        public string Address = "ws://127.0.0.1:4444/";
        public string Password = "";
        // Blur settings
        public int BlurSize = 3;
        public bool BlurAsync = true;
        public bool DrawBlurRect = false;
        public bool ChatLogBlur = true;
        public bool PartyListBlur = true;
        public bool TargetBlur = false;
        public bool TargetTargetBlur = true;
        public bool FocusTargetBlur = false;
        public bool NamePlateBlur = false;
        public bool CharacterBlur = false;
        public bool FriendListBlur = false;
        public int MaxNamePlateCount = 1;
        // Record settings
        public string RecordDir = "";
        public string FilenameFormat = "%CCYY-%MM-%DD %hh-%mm-%ss";
        public bool IncludeTerritory = true;
        public bool ZoneAsSuffix = false;
        public bool StartRecordOnCountDown = false;
        public bool StartRecordOnCombat = false;
        public bool StopRecordOnCombat = false;
        public int StopRecordOnCombatDelay = 5;

        public void Initialize(DalamudPluginInterface pluginInterface)
        {
            this.pluginInterface = pluginInterface;
        }

        public void Save()
        {
            this.pluginInterface.SavePluginConfig(this);
        }
    }
}
