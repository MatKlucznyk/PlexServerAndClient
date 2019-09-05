using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Crestron.SimplSharp;
using Crestron.SimplSharp.Net.Https;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Crestron.SimplSharp.CrestronXml;
using Crestron.SimplSharp.CrestronXmlLinq;

namespace Plex
{
    public static class PlexCloudClients
    {
        public static string GetClientIdentfier(string token, string adr)
        {
            try
            {
                var identifier = string.Empty;

                using (HttpsClient clientsServer = new HttpsClient())
                {
                    IPHostEntry entry;

                    if ((entry = Dns.GetHostEntry("plex.tv")) != null)
                    {
                        HttpsClientRequest clientServerRequest = new HttpsClientRequest();
                        HttpsClientResponse clientServerResponse;

                        clientsServer.TimeoutEnabled = true;
                        clientsServer.Timeout = 25;
                        clientsServer.AuthenticationMethod = Crestron.SimplSharp.Net.AuthMethod.BASIC;
                        clientsServer.PeerVerification = false;

                        clientServerRequest.Url.Parse("https://plex.tv/devices.xml");
                        clientServerRequest.Header.AddHeader(new HttpsHeader("X-Plex-Client-Identifier", "Crestron"));
                        clientServerRequest.Header.AddHeader(new HttpsHeader("X-Plex-Token", token));
                        clientServerRequest.RequestType = RequestType.Get;

                        clientServerResponse = clientsServer.Dispatch(clientServerRequest);

                        XmlDocument doc = new XmlDocument();
                        doc.LoadXml(clientServerResponse.ContentString);

                        XmlNodeList deviceList = doc.SelectNodes("MediaContainer/Device");

                        foreach (XmlNode item in deviceList)
                        {
                            XmlNodeList connections;

                            if ((connections = item.SelectNodes("Connection")) != null)
                            {
                                foreach (XmlNode connection in connections)
                                {
                                    if (connection.Attributes["uri"].Value.Contains(adr))
                                    {
                                        identifier = item.Attributes["clientIdentifier"].Value;
                                        break;
                                    }
                                }

                                if (identifier != string.Empty)
                                    break;
                            }
                        }
                    }
                }

                return identifier;
            }
            catch (Exception e)
            {
                if (e.Message.Contains("Unable to resolve address"))
                {
                }
                else
                    ErrorLog.Error("Error authorizing Plex: {0}", e);
                return "";
            }
        }
    }
}