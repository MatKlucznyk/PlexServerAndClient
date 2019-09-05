using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Crestron.SimplSharp;
using Crestron.SimplSharp.Net.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Plex
{
    public static class PlexServer
    {
        private static string token;
        private static string adr;
        private static int p;
        private static bool loggedIn;
        private static string identifier;

        internal static Dictionary<string, ChangeEvent> Clients = new Dictionary<string, ChangeEvent>();
        internal static List<LibraryItem> Playlists = new List<LibraryItem>();
        internal static string Token { get { return token; } }

        public static string MachineIdentifier { get { return identifier; } }
        public static string IpAddress { get { return adr; } }
        public static int Port { get { return p; } }
        public static bool IsLoggedIn { get { return loggedIn; } }

        static internal bool RegisterClient(string name)
        {
            try
            {
                lock (Clients)
                {
                    if (!Clients.ContainsKey(name))
                    {
                        Clients.Add(name, new ChangeEvent());
                    }
                }

                return true;
            }
            catch (Exception e)
            {
                ErrorLog.Error("Error registering client: {0}", e.Message);
                return false;
            }
        }

        private static string Dispatch(string path, bool reqMID, RequestType type)
        {
            try
            {
                string body = string.Empty;

                using (HttpClient client = new HttpClient())
                {
                    client.Port = p;
                    client.TimeoutEnabled = true;
                    client.Timeout = 25;

                    HttpClientRequest request = new HttpClientRequest();

                    if (reqMID)
                        request.Url.Parse(string.Format("http://{0}{1}?uri=server://{2}/com.plexapp.pligins.library/{1}", adr, path, MachineIdentifier, path));
                    else
                        request.Url.Parse(string.Format("http://{0}{1}", adr, path));
                    request.Header.AddHeader(new HttpHeader("Accept", "application/json"));
                    request.Header.AddHeader(new HttpHeader("X-Plex-Token", token));
                    request.RequestType = type;


                    HttpClientResponse response = client.Dispatch(request);

                    body = response.ContentString;
                    response.Dispose();
                }

                return body;
            }
            catch (HttpException h)
            {
                ErrorLog.Error("Error dispatching request: {0}", h.Message);
                return string.Empty;
            }
            catch (Exception e)
            {
                ErrorLog.Error("Error dispatching request: {0}", e.Message);
                return string.Empty;
            }
        }
        
        public static void Initialize(string address, int port, string username, string password)
        {
            try
            {
                token = PlexAuth.Authorize(username, password);
                adr = address;
                p = port;
               
                if (token.Length > 0)
                {
                    string serverResponse = Dispatch("/", false, RequestType.Get);

                    if (serverResponse.Contains("machineIdentifier"))
                    {
                        JObject response = JObject.Parse(serverResponse);
                        identifier = (string)response["MediaContainer"]["machineIdentifier"];

                        loggedIn = true;
                        GetPlaylists(string.Empty);
                    }
                }
            }
            catch (Exception e)
            {
                ErrorLog.Error("Error in Initialize: {0}", e.Message);
            }
        }

        public static string GetClientIdentifier(string ipAddress)
        {
            try
            {
                string serverResponse = Dispatch("/clients", false, RequestType.Get);
                string identifier = string.Empty;

                JObject response = JObject.Parse(serverResponse);

                if (response["MediaContainer"]["Server"] != null)
                {
                    List<JToken> clients = response["MediaContainer"]["Server"].Children().ToList();

                    foreach (var item in clients)
                    {
                        if (item.ToString().Contains(ipAddress))
                        {
                            identifier = (string)item["machineIdentifier"];
                        }
                    }
                }

                if (identifier == string.Empty)
                {
                    identifier = PlexCloudClients.GetClientIdentfier(token, ipAddress);
                }

                return identifier;
            }
            catch (Exception e)
            {
                ErrorLog.Error("Error in GetClientIdentifier: {0}", e.Message);
                return string.Empty;
            }
        }

        public static void SelectItem(string key, string name)
        {
            if (key.Contains("metadata"))
            {
                GetItem(key, name);
            }
            else if (key == string.Empty)
            {
                GetRootLibrary(name);
            }
            else
            {
                GetSection("/library/sections/" + key + "/all", name);
            }
        }

        private static void GetRootLibrary(string name)
        {
            GetSection("/library/sections/", name);
        }

        private static void GetSection(string key, string name)
        {
            try
            {
                var isDirectory = false;
                
                List<LibraryItem> currentSection = new List<LibraryItem>();

                string serverResponse = Dispatch(key, false, RequestType.Get);

                JObject response = JObject.Parse(serverResponse);
 
                var currentTitle1 = string.Empty;
                var currentTitle2 = string.Empty;
                var currentThumb = string.Empty;
                var currentViewType = string.Empty;

                if (serverResponse.Contains("title1"))
                    currentTitle1 = (string)response["MediaContainer"]["title1"];
                if (serverResponse.Contains("title2"))
                    currentTitle2 = (string)response["MediaContainer"]["title2"];

                currentThumb = "/:/resources/dlna-icon-260.png";

                var previousKey = string.Empty;

                List<JToken> sections = new List<JToken>();

                if (serverResponse.Contains("Directory"))
                {
                    sections = response["MediaContainer"]["Directory"].Children().ToList();
                    isDirectory = true;
                }
                else if (serverResponse.Contains("Metadata"))
                    sections = response["MediaContainer"]["Metadata"].Children().ToList();

                foreach (var item in sections)
                {
                    currentSection.Add(JsonConvert.DeserializeObject<LibraryItem>(item.ToString()));
                }

                Clients[name].Fire(new LibraryChangeEventArgs(currentSection, previousKey, currentTitle1, currentTitle2, currentThumb, isDirectory));
            }
            catch (Exception e)
            {
                ErrorLog.Error("Error in GetSection: {0}", e.Message);
            }
        }

        private static void GetItem(string key, string name)
        {
            try
            {
                List<LibraryItem> currentSection = new List<LibraryItem>();

                string serverResponse = Dispatch(key, false, RequestType.Get);

                JObject response = JObject.Parse(serverResponse);

                var currentTitle1 = string.Empty;
                var currentTitle2 = string.Empty;
                var currentThumb = string.Empty;
                var currentViewType = string.Empty;

                if (serverResponse.Contains("title1"))
                    currentTitle1 = (string)response["MediaContainer"]["title1"];
                if (serverResponse.Contains("title2"))
                    currentTitle2 = (string)response["MediaContainer"]["title2"];

                currentThumb = "/:/resources/dlna-icon-260.png";

                currentViewType = (string)response["MediaContainer"]["viewGroup"];

                var previousKey = string.Empty;

                if (currentViewType == "album" || currentViewType == "season")
                {
                    previousKey = ((int)response["MediaContainer"]["librarySectionID"]).ToString();
                }
                else
                {
                    previousKey = string.Format("/library/metadata/{0}/children", ((int)response["MediaContainer"]["grandparentRatingKey"]).ToString());
                }

                List<JToken> sections = response["MediaContainer"]["Metadata"].Children().ToList();

                foreach (var item in sections)
                {
                    currentSection.Add(JsonConvert.DeserializeObject<LibraryItem>(item.ToString()));
                }

                Clients[name].Fire(new LibraryChangeEventArgs(currentSection, previousKey, currentTitle1, currentTitle2, currentThumb, false));
            }
            catch (Exception e)
            {
                ErrorLog.Error("Error in GetItem: {0}", e.Message);
            }
        }

        public static void GetPlaylists(string name)
        {
            try
            {
                Playlists.Clear();
                //List<LibraryItem> currentSection = new List<LibraryItem>();

                string serverResponse = Dispatch("/playlists", false, RequestType.Get);

                JObject response = JObject.Parse(serverResponse);

                List<JToken> playlists = new List<JToken>();

                if (serverResponse.Contains("Metadata"))
                    playlists = response["MediaContainer"]["Metadata"].Children().ToList();

                foreach (var item in playlists)
                {
                    var libItem = JsonConvert.DeserializeObject<LibraryItem>(item.ToString());
                    Playlists.Add(libItem);
                }

                //Clients[name].Fire(new LibraryChangeEventArgs(currentSection, previousKey, currentTitle1, currentTitle2, currentThumb, isDirectory));
            }
            catch (Exception e)
            {
                ErrorLog.Error("Error in GetSection: {0}", e.Message);
            }
        }

        public static void GetPlaylist(string key, string name)
        {
            try
            {
                
                List<LibraryItem> currentSection = new List<LibraryItem>();

                string serverResponse = Dispatch(key, false, RequestType.Get);

                JObject response = JObject.Parse(serverResponse);

                var currentTitle1 = string.Empty;
                var currentTitle2 = string.Empty;
                var currentThumb = string.Empty;
                var currentViewType = string.Empty;

                if (serverResponse.Contains("title"))
                    currentTitle1 = (string)response["MediaContainer"]["title"];

                currentThumb = "/:/resources/dlna-icon-260.png";

                currentViewType = (string)response["MediaContainer"]["playListType"];

                var previousKey = "/playlists";

                List<JToken> items = response["MediaContainer"]["Metadata"].Children().ToList();

                foreach (var item in items)
                {
                    currentSection.Add(JsonConvert.DeserializeObject<LibraryItem>(item.ToString()));
                }

                Clients[name].Fire(new LibraryChangeEventArgs(currentSection, previousKey, currentTitle1, currentTitle2, currentThumb, false));
            }
            catch (Exception e)
            {
                ErrorLog.Error("Error in GetItem: {0}", e.Message);
            }
        }

        public static void CreatePlaylist(string tag, string name)
        {
            try
            {
                LibraryItem playlist;
                if ((playlist = Playlists.Find(x => x.Name == tag)) == null)
                {
                    using (HttpClient client = new HttpClient())
                    {
                        client.Port = p;
                        client.TimeoutEnabled = true;
                        client.Timeout = 25;

                        HttpClientRequest request = new HttpClientRequest();

                        request.Url.Parse(string.Format("http://{0}/playlists/?uri=server://{1}/com.plexapp.plugins.library/library/&title={2}&smart=0&type=audio", adr, MachineIdentifier, tag));
                        request.Header.AddHeader(new HttpHeader("Accept", "application/json"));
                        request.Header.AddHeader(new HttpHeader("X-Plex-Token", token));
                        request.RequestType = RequestType.Post;


                        HttpClientResponse response = client.Dispatch(request);

                        var body = response.ContentString;
                        response.Dispose();
                        GetPlaylists(name);
                    }
                }
            }
            catch (Exception e)
            {
                ErrorLog.Exception("Error dispatching a create playlist post", e);
            }
        }

        public static void AddSongToPlaylist(string key, string playlistName, string name)
        {
            try
            {
                LibraryItem playlist;
                if ((playlist = Playlists.Find(x => x.Name == playlistName)) != null)
                {
                    using (HttpClient client = new HttpClient())
                    {
                        client.Port = p;
                        client.TimeoutEnabled = true;
                        client.Timeout = 25;

                        HttpClientRequest request = new HttpClientRequest();

                        request.Url.Parse(string.Format("http://{0}{1}?uri=server://{2}/com.plexapp.plugins.library{3}", adr, playlist.Key, MachineIdentifier, key));
                        request.Header.AddHeader(new HttpHeader("Accept", "application/json"));
                        request.Header.AddHeader(new HttpHeader("X-Plex-Token", token));
                        request.RequestType = RequestType.Put;


                        HttpClientResponse response = client.Dispatch(request);

                        var body = response.ContentString;
                        response.Dispose();
                        //GetPlaylist(PlayLists[playlistName].Key, name);
                    }
                }
            }
            catch (Exception e)
            {
                ErrorLog.Exception("Error dispatching add song to playlist put", e);
            }
        }

        public static void DeleteSongFromPlaylist(LibraryItem song, string playlistName, string name)
        {
            try
            {
                LibraryItem playlist;
                if ((playlist = Playlists.Find(x => x.Name == playlistName)) != null)
                {
                    using (HttpClient client = new HttpClient())
                    {
                        client.Port = p;
                        client.TimeoutEnabled = true;
                        client.Timeout = 25;

                        HttpClientRequest request = new HttpClientRequest();

                        request.Url.Parse(string.Format("http://{0}{1}/{2}?uri=server://{3}/com.plexapp.plugins.library{4}", adr, playlist.Key, song.PlaylistItemID, MachineIdentifier, song.Key));
                        request.Header.AddHeader(new HttpHeader("Accept", "application/json"));
                        request.Header.AddHeader(new HttpHeader("X-Plex-Token", token));
                        request.RequestType = RequestType.Delete;


                        HttpClientResponse response = client.Dispatch(request);

                        var body = response.ContentString;
                        response.Dispose();
                        GetPlaylist(playlist.Key, name);
                    }
                }
            }
            catch (Exception e)
            {
                ErrorLog.Exception("Error dispatching deleting song to playlist delete", e);
            }
        }
    }

    public class LibraryChangeEventArgs : EventArgs
    {
        public List<LibraryItem> Section;
        public string PreviousKey;
        public string Title1;
        public string Title2;
        public string AlbumArt;
        public bool IsDirectory;

        public LibraryChangeEventArgs(List<LibraryItem> section, string previousKey, string title1, string title2, string albumArt, bool isDirectory)
        {
            this.Section = section;
            this.PreviousKey = previousKey;
            this.Title1 = title1;
            this.Title2 = title2;
            this.IsDirectory = isDirectory;
            this.AlbumArt = string.Format("http://{0}:{1}{2}?X-Plex-Token={3}", PlexServer.IpAddress, PlexServer.Port, albumArt, PlexServer.Token);
        }
    }

    public class ChangeEvent
    {
        private event EventHandler<LibraryChangeEventArgs> onLibraryChange = delegate { };

        public event EventHandler<LibraryChangeEventArgs> OnLibraryChange
        {
            add
            {
                if (!onLibraryChange.GetInvocationList().Contains(value))
                    onLibraryChange += value;
            }
            remove
            {
                onLibraryChange -= value;
            }
        }

        internal void Fire(LibraryChangeEventArgs e)
        {
            onLibraryChange(null, e);
        }
    }
}