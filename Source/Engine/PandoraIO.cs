﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.IO;
using PandoraMusicBox.Engine.Encryption;
using System.Xml;
using PandoraMusicBox.Engine.Data;

namespace PandoraMusicBox.Engine {
    /// <summary>
    /// Low level class providing direct access to the Pandora API. 
    /// </summary>
    public class PandoraIO {
        private const string baseUrl = "http://www.pandora.com/radio/xmlrpc/v27?";

        BlowfishCipher encrypter = new BlowfishCipher(PandoraCryptKeys.Out);

        private string RouteId {
            get { 
                if (_routeId == null)
                    _routeId = (DateTime.UtcNow.ToFileTime() % 10000000).ToString("0000000") + "P";
                
                return _routeId;
            }
        } private string _routeId = null;

        /// <summary>
        /// Given the username and password, attempts to log into the Pandora music service.
        /// </summary>
        /// <returns>If login is successful, returns a PandoraUser object. If invalid username or password
        /// null is returned.</returns>
        public PandoraUser AuthenticateListener(string username, string password) {
            try {        
                string reply = ExecuteRequest(null, PandoraRequest.AuthenticateListener, username, password);
                PandoraUser user = PandoraUser.Parse(reply);
                user.Password = password;

                return user;
            }
            catch (PandoraException e) {
                if (e.ErrorCode == ErrorCodeEnum.AUTH_INVALID_USERNAME_PASSWORD)
                    return null;

                throw;
            }
        }

        /// <summary>
        /// Retrieves a list of stations for the given user.
        /// </summary>
        public List<PandoraStation> GetStations(PandoraUser user) {
            if (user == null)
                throw new PandoraException("User must be logged in to make this request.");

            string reply = ExecuteRequest(user, PandoraRequest.GetStations);
            return PandoraStation.Parse(reply);
        }

        /// <summary>
        /// Retrieves a playlist for the given station.
        /// </summary>
        public List<PandoraSong> GetSongs(PandoraUser user, PandoraStation station) {
            if (user == null) throw new PandoraException("User must be logged in to make this request.");

            // grab song list from server
            string reply = ExecuteRequest(user, PandoraRequest.GetFragment, station.Id, "mp3-hifi");
            List<PandoraSong> songs = PandoraSong.Parse(reply);

            // loop through our song list and estimate the length of each song based on file size
            foreach (PandoraSong currSong in songs) {
                WebRequest dummyRequest = WebRequest.Create(currSong.AudioURL);
                using (WebResponse response = dummyRequest.GetResponse()) {
                    long bytes = response.ContentLength;
                    int seconds = (int)((bytes * 8) / (user.AccountType == AccountType.PREMIUM ? 192000 : 128000));
                    currSong.Length = new TimeSpan(0, 0, seconds);
                }
            }

            return songs;
        }

        public void RateSong(PandoraUser user, PandoraStation station, PandoraSong song, PandoraRating rating) {
            if (user == null) throw new PandoraException("User must be logged in to make this request.");

            string matchingSeed = "";
            string userSeed = "";
            string focusTraitId = "";
            int apiRating = (rating == PandoraRating.Love) ? 1 : 0;

            string reply = ExecuteRequest(user, PandoraRequest.RateSong, station.Id, song.MusicId,  matchingSeed, userSeed, focusTraitId, apiRating);

            song.Rating = rating;
        }

        public void AddTiredSong(PandoraUser user, PandoraSong song) {
            if (user == null) throw new PandoraException("User must be logged in to make this request.");

            string reply = ExecuteRequest(user, PandoraRequest.AddTiredSong, song.MusicId);
            song.TemporarilyBanned = true;
        }

        public bool CanListen(PandoraUser user) {
            if (user == null) throw new PandoraException("User must be logged in to make this request.");

            string reply = ExecuteRequest(user, false, PandoraRequest.CanListen, user.WebAuthorizationToken);
            try { 
                Dictionary<string, string> vars = PandoraData.GetVariables(reply);
                return vars["canListen"] == "1";
            }
            catch {
                throw new PandoraException("XML-RPC response missing expected value: 'canListen'");
            }
        }

        public PandoraSong GetAdvertisement(PandoraUser user) {
            string baseUrl = "http://ad.doubleclick.net/pfadx/pand.default/prod.tuner;fb=0;ag={0};gnd=1;zip={1};hours=0;comped=0;clean=0;playlist=pandora;genre=;segment=1;u=clean*0!playlist*pandora!segment*1!fb*0!ag*{2}!gnd*1!zip*{3}!hours*0!comped*0;sz=134x185;ord={4}";     
            string url = string.Format(baseUrl, user.Age, user.ZipCode, user.Age, user.ZipCode, GetTime() * 10000000);
            Cookie cookie = getDoubleclickIdCookie(url);

            // build request to ad server
            HttpWebRequest webRequest = (HttpWebRequest)WebRequest.Create(url);
            webRequest.CookieContainer = new CookieContainer();
            webRequest.CookieContainer.Add(cookie);
            
            // grab response from server
            using (WebResponse response = webRequest.GetResponse()) {
                StreamReader sr = new StreamReader(response.GetResponseStream());
                string reply = sr.ReadToEnd();

                // parse results and return
                return PandoraSong.ParseAdvertisement(reply);
            }
        }

        private Cookie getDoubleclickIdCookie(string url) {
            HttpWebRequest webRequest = (HttpWebRequest)WebRequest.Create(url);
            webRequest.CookieContainer = new CookieContainer();
             
            for (int i = 0; i < 3; i++) {
                using (WebResponse response = webRequest.GetResponse()) {
                    System.Net.HttpWebResponse resp = ((System.Net.HttpWebResponse)response);
                    webRequest.CookieContainer = new CookieContainer();
                    foreach (Cookie c in resp.Cookies) {
                        if (c.Name == "id")
                            return c;

                        webRequest = (HttpWebRequest)WebRequest.Create(url);
                        webRequest.CookieContainer = new CookieContainer();
                        webRequest.CookieContainer.Add(c);
                    }
                }
            }

            return null;
        }

        private string ExecuteRequest(PandoraUser user, PandoraRequest request, params object[] paramList) {
            return ExecuteRequest(user, true, request, paramList);
        }

        private string ExecuteRequest(PandoraUser user, bool useAuthToken, PandoraRequest request, params object[] paramList) {
            string reply;

            try {
                ASCIIEncoding encoder = new ASCIIEncoding();

                // build method specific info for request to pandora servers
                string url = baseUrl + "rid=" + RouteId;
                if (user != null) url += "&lid=" + user.ListenerId;
                url += String.Format(request.URLSuffix, paramList);

                // build parameter list for the xml-rpc request
                int index = 0;
                object[] xmlParams = new object[paramList.Length + 2];
                xmlParams[index++] = GetTime();
                if (user != null && useAuthToken) xmlParams[index++] = user.AuthorizationToken;
                foreach(object currParam in paramList)
                    xmlParams[index++] = currParam;

                string postStr = String.Format(request.XmlRpcRequest, xmlParams);
                byte[] postData = encoder.GetBytes(encrypter.Encrypt(postStr));

                // configure request object
                WebRequest webRequest = WebRequest.Create(url);
                webRequest.ContentType = "text/xml";
                webRequest.ContentLength = postData.Length;
                webRequest.Method = "POST";

                // send request to remote servers
                Stream os = webRequest.GetRequestStream();
                os.Write(postData, 0, postData.Length);
                os.Close();

                // retrieve reply from servers
                using (WebResponse response = webRequest.GetResponse()) {
                    StreamReader sr = new StreamReader(response.GetResponseStream());
                    reply = sr.ReadToEnd();
                }
            }
            catch (Exception ex) {
                throw new PandoraException("Unexpected error communicating with server.", ex);
            }

            // check for error response
            PandoraException error = PandoraException.ParseError(reply);
            if (error != null) throw error;

            return reply;
        }

        private long GetTime() {
            return (int)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
        }

    }

    public enum PandoraRating {
        Love = 1,
        Unrated = 0,
        Hate = -1
    }
}
