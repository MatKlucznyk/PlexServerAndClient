using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Crestron.SimplSharp;
using Crestron.SimplSharp.Net.Http;
using Crestron.SimplSharp.CrestronXml;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Plex
{   
    public class PlexClient : IDisposable
    {
        private class PlexClientInfo
        {
            public string IpAddress;
            public int PlayerPort;
            public int ListenPort;
            public string Name;


            public PlexClientInfo(string ipAddress, int playerPort, int listenPort, string name)
            {
                this.IpAddress = ipAddress;
                this.PlayerPort = playerPort;
                this.ListenPort = listenPort;
                this.Name = name;
            }
        }

        private List<LibraryItem> section = new List<LibraryItem>();
        private List<string> sectionItemNames = new List<string>();
        private CTimer pollTimer;
        private CTimer playNextItem;
        private CTimer serverStatus;
        private LibraryItem currentlyPlaying;
        private HttpServer server;
        private string adr;
        private string Name;
        private string currentTitle1;
        private string currentTitle2;
        private string currentAlbumArt;
        private string previouskey = string.Empty;
        private int p;
        private int lP;
        private long cmdID;
        private string identifier;
        private bool isDirectory;
        private bool isAlive;
        private bool isPlaying;
        private bool isBusySending;
        private bool isPlayAll;
        private bool isRepeatAll;
        private bool currentSectionIsPlaylist;
        private bool currentSectionIsPlaylistItems;
        private LibraryItem currentlySelectedPlaylist;

        public List<LibraryItem> CurrentSection { get { return section; } }
        public List<LibraryItem> CurrentPlaylists { get; set; }
        public LibraryItem Currentlyplaying { get { return currentlyPlaying; } }
        public LibraryItem CurrentPlaylist { get { return currentlySelectedPlaylist; } }
        public bool IsPlaying { get { return isPlaying; } }
        public bool IsPlayAll { get { return isPlayAll; } }
        public bool IsRepeatAll { get { return isRepeatAll; } }
        public bool IsPlaylists { get { return currentSectionIsPlaylist; } }
        public bool IsPlaylist { get { return currentSectionIsPlaylistItems; } }
        public string PlayerName { get { return Name; } }

        public event EventHandler<LibraryUpdateEventArgs> LibraryUpdate;
        //public event EventHandler<LibraryUpdateEventArgs> PlayListsUpdate;
        public event EventHandler<PlayProgressEventArgs> PlayProgress;
        public event Action<bool, string, string> Playing; 

        public PlexClient(string ipAddress, int playerPort, int listenPort, string name)
        {
            PlexClientInfo info = new PlexClientInfo(ipAddress, playerPort, listenPort, name);

            serverStatus = new CTimer(ServerStatus, info, 0, 10000);
        }

        private void ServerStatus(object o)
        {
            PlexClientInfo client = o as PlexClientInfo;

            if (PlexServer.IsLoggedIn)
            {
                serverStatus.Stop();
                serverStatus.Dispose();

                isAlive = true;

                this.adr = client.IpAddress;
                this.p = client.PlayerPort;
                this.lP = client.ListenPort;
                this.Name = client.Name;

                identifier = PlexServer.GetClientIdentifier(adr);

                if (PlexServer.RegisterClient(Name))
                {
                    PlexServer.Clients[Name].OnLibraryChange += new EventHandler<LibraryChangeEventArgs>(PlexClient_OnLibraryChange);
                }

                server = new HttpServer();
                server.OnHttpRequest += new OnHttpRequestHandler(server_OnHttpRequest);
                server.Port = client.ListenPort;
                server.EthernetAdapterToBindTo = EthernetAdapterType.EthernetLANAdapter;
                server.Active = true;

                pollTimer = new CTimer(PollTimer, this, 0, 30000);
            }
        }

        void server_OnHttpRequest(object sender, OnHttpRequestArgs e)
        {
            try
            {
                if (e.Request.HasContentLength && e.Request.Path == "/:/timeline")
                {

                    XmlDocument doc = new XmlDocument();
                    doc.LoadXml(e.Request.ContentString);
                    XmlNodeList timelineList = doc.SelectNodes("MediaContainer/Timeline");

                    var currentType = "music";

                    foreach (XmlNode item in timelineList)
                    {
                        var type = item.Attributes["type"].Value;


                        if (item.Attributes["state"].Value == "playing")
                        {
                            foreach (var sectionItem in section)
                            {
                                var currentItem = currentlyPlaying;

                                if (sectionItem.Key == item.Attributes["key"].Value)
                                {
                                    currentlyPlaying = sectionItem;
                                    if (Currentlyplaying.Type == "movie" || Currentlyplaying.Type == "episode")
                                        currentType = "video";
                                    else if (Currentlyplaying.Type == "track")
                                        currentType = "music";

                                    if (!isPlaying || currentItem != currentlyPlaying)
                                    {
                                        isPlaying = true;
                                        Playing(true, "playing", Name);
                                    }
                                    break;
                                }
                            }
                        }
                        if (type == currentType)
                        {
                            if (item.Attributes["time"] != null)
                                PlayProgress(this, new PlayProgressEventArgs(Convert.ToInt64(item.Attributes["time"].Value) / 1000, Name));

                            if (item.Attributes["state"].Value == "paused")
                            {
                                isPlaying = false;
                                Playing(false, "paused", Name);
                            }
                            else if (item.Attributes["state"].Value == "stopped")
                            {
                                if (isPlaying)
                                {
                                    if (isPlayAll && section.Count > section.IndexOf(currentlyPlaying) + 1)
                                    {
                                        playNextItem = new CTimer(PlayNextItemCallback, this, 5000);
                                    }
                                    else if (isRepeatAll)
                                    {
                                        SelectItem(CurrentSection[0]);
                                    }
                                    else
                                        isPlayAll = false;

                                    isPlaying = false;
                                    PlayProgress(this, new PlayProgressEventArgs(Convert.ToInt64(currentlyPlaying.Duration) / 1000, Name));
                                    Playing(false, "stopped", Name);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ex.ToString();
            }
        }

        private void PollTimer(object o)
        {
            if (cmdID == 100000)
                cmdID = 0;

            Dispatch();
        }

        private void PlayNextItemCallback(object o)
        {
            var newItem = section[section.IndexOf(currentlyPlaying) + 1];

            LibraryUpdate(null, new LibraryUpdateEventArgs(sectionItemNames, newItem.Name, string.Empty,
                    string.Format("http://{0}:{1}{2}?X-Plex-Token={3}", PlexServer.IpAddress, PlexServer.Port, newItem.Thumb, PlexServer.Token), Name));

            Play(newItem);
            playNextItem.Dispose();
        }

        public void Dispose()
        {
            if (isAlive)
            {
                pollTimer.Stop();
                pollTimer.Dispose();
                if (playNextItem != null)
                {
                    playNextItem.Stop();
                    playNextItem.Dispose();
                }
                if (serverStatus != null)
                {
                    serverStatus.Stop();
                    serverStatus.Dispose();
                }
                server.Close();
                server.Dispose();
            }
        }

        private string Dispatch()
        {
            try
            {
                while (isBusySending) ;

                isBusySending = true;
                
                var body = SendToClient(string.Format("http://{0}/player/timeline/subscribe?protocol=http&port={1}&commandID={2}", adr, lP, cmdID++));

                isBusySending = false;
                return body;
            }
            catch (HttpException h)
            {
                ErrorLog.Error("HttpException in DispatchPlaybackControl: {0}", h.Message);
                isBusySending = false;
                return string.Empty;
            }
            catch (Exception e)
            {
                ErrorLog.Error("Exception in DispatchPlaybackControl: {0}", e.Message);
                isBusySending = false;
                return string.Empty;
            }
        }

        private string Dispatch(string playbackControl)
        {
            try
            {
                while (isBusySending) ;

                isBusySending = true;

                var newType = string.Empty;

                switch (currentlyPlaying.Type)
                {
                    case "movie":
                        newType = "video";
                        break;
                    case "episode":
                        newType = "video";
                        break;
                    case "track":
                        newType = "music";
                        break;
                    default:
                        break;
                }
                
                var body = SendToClient(string.Format("http://{0}/player/playback/{1}?key={2}&offset=0&machineIdentifer={3}&protocol=http&path={4}&token={5}&type={6}&commandID={7}", adr, playbackControl, currentlyPlaying.Key, identifier, string.Format("http://{0}:{1}{2}", PlexServer.IpAddress, PlexServer.Port, currentlyPlaying.Key), PlexServer.Token,
                        newType, cmdID++));

                isBusySending = false;
                return body;
            }
            catch (HttpException h)
            {
                ErrorLog.Error("HttpException in DispatchPlaybackControl: {0}", h.Message);
                isBusySending = false;
                return string.Empty;
            }
            catch (Exception e)
            {
                ErrorLog.Error("Exception in DispatchPlaybackControl: {0}", e.Message);
                isBusySending = false;
                return string.Empty;
            }
        }

        private string Dispatch(string key, double offset, string type)
        {
            try
            {
                while (isBusySending) ;

                isBusySending = true;
                
                var newType = string.Empty;

                switch (type)
                {
                    case "movie":
                        newType = "video";
                        break;
                    case "episode":
                        newType = "video";
                        break;
                    case "track":
                        newType = "music";
                        break;
                    default:
                        break;
                }

                var body = SendToClient(string.Format("http://{0}/player/playback/playMedia?token={1}&protcol=http&address={2}&machineIdentifier={3}&port={4}&key={5}&path={6}&offset={7}&type={8}&commandID={9}",
                        adr, PlexServer.Token, PlexServer.IpAddress, PlexServer.MachineIdentifier, PlexServer.Port, key, string.Format("http://{0}:{1}{2}", PlexServer.IpAddress, PlexServer.Port, key),
                        offset, newType, cmdID++));

                isBusySending = false;
                return body;
            }
            catch (HttpException h)
            {
                ErrorLog.Error("HttpException in DispatchPlaybackControl: {0}", h.Message);
                isBusySending = false;
                return string.Empty;
            }
            catch (Exception e)
            {
                ErrorLog.Error("Exception in DispatchPlaybackControl: {0}", e.Message);
                isBusySending = false;
                return string.Empty;
            }
        }

        private string SendToClient(string url)
        {
            try
            {
                var body = string.Empty;
                using (HttpClient client = new HttpClient())
                {
                    HttpClientRequest request = new HttpClientRequest();

                    client.TimeoutEnabled = true;
                    client.Timeout = 25;
                    client.Port = p;
                    request.Url.Parse(url);
                    request.Header.AddHeader(new HttpHeader("X-Plex-Client-Identifier", identifier));
                    request.Header.AddHeader(new HttpHeader("X-Plex-Target-Client-Identifier", identifier));
                    request.Header.AddHeader(new HttpHeader("X-Plex-Device-Name", "Crestron"));
                    request.Header.AddHeader(new HttpHeader("Access-Control-Expose-Headers", "X-Plex-Client-Identifier"));

                    HttpClientResponse response = client.Dispatch(request);
                    body = response.ContentString;

                    request = null;
                    response.Dispose();
                }
                return body;
            }
            catch (Exception e)
            {
                e.ToString();
                return string.Empty;
            }
        }

        public void GetRootDirectory()
        {
            PlexServer.SelectItem(string.Empty, Name);
        }

        public void Back()
        {
            PlexServer.SelectItem(previouskey, Name);
        }

        public void SelectItem(LibraryItem item)
        {
            if ((item.Type == "movie" || item.Type == "track" || item.Type == "episode") && !IntTryParse(item.Key))
            {
                LibraryUpdate(null, new LibraryUpdateEventArgs(sectionItemNames, item.Name, string.Empty,
                    string.Format("http://{0}:{1}{2}?X-Plex-Token={3}", PlexServer.IpAddress, PlexServer.Port, item.Thumb, PlexServer.Token), Name));

                //isPlayAll = false;
                Play(item);
            }
            else
            {
                PlexServer.SelectItem(item.Key, Name);
            }
        }

        private void PlexClient_OnLibraryChange(object sender, LibraryChangeEventArgs e)
        {
            currentAlbumArt = e.AlbumArt;
            section = e.Section;
            currentTitle1 = e.Title1;
            currentTitle2 = e.Title2;
            isDirectory = e.IsDirectory;
            previouskey = e.PreviousKey;

            sectionItemNames.Clear();

            foreach (var item in section)
            {
                sectionItemNames.Add(item.Name);
            }

            LibraryUpdate(null, new LibraryUpdateEventArgs(sectionItemNames, currentTitle1, currentTitle2, currentAlbumArt, Name));
        }

        private bool IntTryParse(string num)
        {
            try
            {
                int x = int.Parse(num);
                return true;
            }
            catch(Exception e)
            {
                var x = e;
                return false;
            }
        }

        #region Playback Controls
        public void Play()
        {
            Dispatch("play");
        }

        public void Pause()
        {
            Dispatch("pause");

            isPlaying = false;
            Playing(false, "paused", Name);
        }

        public void StepBack()
        {
            Dispatch("stepBack");
        }

        public void StepForward()
        {
            Dispatch("stepForward");
        }

        public void SkipNext()
        {
            Dispatch("skipNext");
        }

        public void SkipPrevious()
        {
            Dispatch("skipPrevious");
        }

        public void Next()
        {
            if (section.IndexOf(currentlyPlaying) != section.Count - 1)
            {
                var newItem = section[section.IndexOf(currentlyPlaying) + 1];

                LibraryUpdate(null, new LibraryUpdateEventArgs(sectionItemNames, newItem.Name, string.Empty,
                        string.Format("http://{0}:{1}{2}?X-Plex-Token={3}", PlexServer.IpAddress, PlexServer.Port, newItem.Thumb, PlexServer.Token), Name));

                Play(newItem);
            }
        }

        public void Previous()
        {
            if (section.IndexOf(currentlyPlaying) != 0)
            {
                var newItem = section[section.IndexOf(currentlyPlaying) - 1];

                LibraryUpdate(null, new LibraryUpdateEventArgs(sectionItemNames, newItem.Name, string.Empty,
                        string.Format("http://{0}:{1}{2}?X-Plex-Token={3}", PlexServer.IpAddress, PlexServer.Port, newItem.Thumb, PlexServer.Token), Name));

                Play(newItem);
            }
        }

        /*public void Stop()
        {
            Dispatch("stop");
            progress.Stop();
            progressCnt = 0;

            Playing(false);
        }*/

        private void Play(LibraryItem item)
        {
            Dispatch(item.Key, 0, item.Type);
        }

        /*public void Resume(LibraryItem item)
        {
            Dispatch(item.Key, item.Offset, item.Type);
        }*/

        public void PlayAll()
        {
            if (section.Count > 0)
            {
                isPlayAll = true;
            }
        }

        public void RepeatAll()
        {
            isRepeatAll = true;
        }

        public void CancelPlayAll()
        {
            isPlayAll = false;

            if (playNextItem != null)
            {
                if (!playNextItem.Disposed)
                    playNextItem.Dispose();
            }
        }

        public void CancelRepeatAll()
        {
            isRepeatAll = false;
        }
        #endregion

        #region Playlists
        public List<LibraryItem> GetPlaylists()
        {
            previouskey = "/playlists";
            PlexServer.GetPlaylists(Name);
            CurrentPlaylists = PlexServer.Playlists;
            currentSectionIsPlaylist = true;
            currentSectionIsPlaylistItems = false;

            return PlexServer.Playlists;
        }

        public void SelectPlaylist(LibraryItem item)
        {
            previouskey = item.Key;
            PlexServer.GetPlaylist(item.Key, Name);
            currentSectionIsPlaylistItems = true;
            currentlySelectedPlaylist = item;
        }

        public void ClosePlayLists()
        {
            currentSectionIsPlaylist = false;
            GetRootDirectory();
        }

        public bool AddSongToPlayList(string playlistName, LibraryItem song)
        {
            PlexServer.AddSongToPlaylist(song.Key, playlistName, Name);
            return true;
        }

        public bool DeleteSongFromPlayList(string playlistName, LibraryItem song)
        {
            PlexServer.DeleteSongFromPlaylist(song, playlistName, Name);
            return true;
        }

        public bool CreatePlaylist(string playlistName)
        {
            PlexServer.CreatePlaylist(playlistName, Name);

            return true;
        }
        #endregion
    }

    public class LibraryUpdateEventArgs : EventArgs
    {
        public List<string> ItemNames;
        public string Title1;
        public string Title2;
        public string AlbumArt;
        public string PlayerName;

        public LibraryUpdateEventArgs(List<string> itemNames, string title1, string title2, string albumArt, string playerName)
        {
            this.ItemNames = itemNames;
            this.Title1 = title1;
            this.Title2 = title2;
            this.AlbumArt = albumArt;
            this.PlayerName = playerName;
        }
    }

    public class PlayProgressEventArgs : EventArgs
    {
        public long Time;
        public string PlayerName;

        public PlayProgressEventArgs(long time, string playerName)
        {
            this.Time = time;
            this.PlayerName = playerName;
        }
    }
}