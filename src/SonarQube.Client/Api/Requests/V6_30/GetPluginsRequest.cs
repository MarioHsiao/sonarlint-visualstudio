/*
 * SonarQube Client
 * Copyright (C) 2016-2018 SonarSource SA
 * mailto:info AT sonarsource DOT com
 *
 * This program is free software; you can redistribute it and/or
 * modify it under the terms of the GNU Lesser General Public
 * License as published by the Free Software Foundation; either
 * version 3 of the License, or (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
 * Lesser General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public License
 * along with this program; if not, write to the Free Software Foundation,
 * Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.
 */

using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SonarQube.Client.Models;

namespace SonarQube.Client.Api.Requests.V6_30
{
    public class GetPluginsRequest : RequestBase<SonarQubePlugin[]>, IGetPluginsRequest
    {
        protected override string Path => "api/plugins/installed";

        protected override SonarQubePlugin[] ParseResponse(string response) =>
            JObject.Parse(response)["plugins"]
                .ToObject<PluginResponse[]>()
                .Select(ToPlugin)
                .ToArray();

        private SonarQubePlugin ToPlugin(PluginResponse response) =>
            new SonarQubePlugin(response.Key, response.Version);

        private class PluginResponse
        {
            [JsonProperty("key")]
            public string Key { get; set; }

            [JsonProperty("version")]
            public string Version { get; set; }
        }
    }
}
