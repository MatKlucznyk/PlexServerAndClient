using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Crestron.SimplSharp;
using Newtonsoft.Json;

namespace Plex
{
    public class LibraryItem
    {
        [JsonProperty("title")]
        public string Name { get; set; }
        [JsonProperty("thumb", Required = Required.Default)]
        public string Thumb { get; set; }
        [JsonProperty("key")]
        public string Key { get; set; }
        [JsonProperty("type")]
        public string Type { get; set; }
        [JsonProperty("duration", Required = Required.Default)]
        public double Duration { get; set; }
        [JsonProperty("viewOffset", Required = Required.Default)]
        public double Offset { get; set; }
        [JsonProperty("playlistItemID", Required = Required.Default)]
        public int PlaylistItemID { get; set; }
    }
}