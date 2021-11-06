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
        public int BlurSize = 25;
        public bool ChatLogBlur = true;
        public bool PartyListBlur = true;
        public List<Blur> BlurList = new();

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
