using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Crestron.SimplSharp;
using Crestron.SimplSharp.Net.Https;
using Crestron.SimplSharp.CrestronDataStore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Plex
{
    public static class PlexAuth
    {
        private static string usr;
        private static string pass;

        public static string Authorize(string username, string password)
        {
            try
            {
                string authToken = string.Empty;

                usr = username;
                pass = password;

                if (CrestronDataStoreStatic.GetLocalStringValue("authToken", out authToken) != CrestronDataStore.CDS_ERROR.CDS_SUCCESS)
                {
                    if (usr.Length > 0 && pass.Length > 0)
                    {
                        using (HttpsClient authServer = new HttpsClient())
                        {
                            IPHostEntry entry;

                            if ((entry = Dns.GetHostEntry("plex.tv")) != null)
                            {
                                HttpsClientRequest authServerRequest = new HttpsClientRequest();
                                HttpsClientResponse authServerResponse;

                                authServer.TimeoutEnabled = true;
                                authServer.Timeout = 25;
                                authServer.AuthenticationMethod = Crestron.SimplSharp.Net.AuthMethod.BASIC;
                                authServer.PeerVerification = false;
                                authServer.UserName = usr;
                                authServer.Password = pass;

                                authServerRequest.Url.Parse("https://plex.tv/users/sign_in.json");
                                authServerRequest.Header.AddHeader(new HttpsHeader("X-Plex-Client-Identifier", "Crestron"));
                                authServerRequest.RequestType = RequestType.Post;

                                authServerResponse = authServer.Dispatch(authServerRequest);

                                JObject response = JObject.Parse(authServerResponse.ContentString);

                                authToken = (string)response["user"]["authToken"];

                                CrestronDataStoreStatic.SetLocalStringValue("authToken", authToken);
                            }
                        }
                    }
                }

                return authToken;
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