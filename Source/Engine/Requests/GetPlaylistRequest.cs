﻿using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using PandoraMusicBox.Engine.Data;
using PandoraMusicBox.Engine.Responses;

namespace PandoraMusicBox.Engine.Requests {
    internal class GetPlaylistRequest: PandoraRequest {
        public override string MethodName {
            get { return "station.getPlaylist"; }
        }

        public override Type ReturnType {
            get { return typeof(GetPlaylistResponse); }
        }

        public override bool IsSecure {
            get { return true; }
        }

        public override bool IsEncrypted {
            get { return true; }
        }

        [JsonProperty(PropertyName = "stationToken")]
        public string StationToken {
            get;
            set;
        }
        [JsonProperty(PropertyName = "additionalAudioUrl")]
        public string AdditionalAudioTypeRequests {
            get {
                return "HTTP_192_MP3";
            }
        }

        public GetPlaylistRequest(PandoraSession session, string stationToken) :
            base(session) {
            this.StationToken = stationToken;
        }
    }
}
