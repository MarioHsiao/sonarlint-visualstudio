﻿/*
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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SonarQube.Client.Api.Requests;
using SonarQube.Client.Helpers;
using SonarQube.Client.Messages;
using SonarQube.Client.Models;
using SonarQube.Client.Services;

namespace SonarQube.Client.Api
{
    public class SonarQubeService : ISonarQubeService
    {
        internal const int MaximumPageSize = 500;
        internal static readonly Version OrganizationsFeatureMinimalVersion = new Version(6, 2);

        private readonly HttpMessageHandler messageHandler;
        private readonly RequestFactory requestFactory;
        public readonly string userAgent;

        private HttpClient httpClient;

        private Version sonarQubeVersion = null;

        public bool HasOrganizationsFeature
        {
            get
            {
                EnsureIsConnected();

                return sonarQubeVersion >= OrganizationsFeatureMinimalVersion;
            }
        }

        public bool IsConnected { get; private set; }

        public SonarQubeService(HttpMessageHandler messageHandler, RequestFactory requestFactory, string userAgent)
        {
            if (messageHandler == null)
            {
                throw new ArgumentNullException(nameof(messageHandler));
            }
            if (requestFactory == null)
            {
                throw new ArgumentNullException(nameof(requestFactory));
            }
            if (userAgent == null)
            {
                throw new ArgumentNullException(nameof(userAgent));
            }
            this.messageHandler = messageHandler;
            this.requestFactory = requestFactory;
            this.userAgent = userAgent;
        }

        /// <summary>
        /// Convenience overload for requests that do not need configuration.
        /// </summary>
        private Task<TResponse> InvokeRequestAsync<TRequest, TResponse>(CancellationToken token)
            where TRequest : IRequest<TResponse>
        {
            return InvokeRequestAsync<TRequest, TResponse>(request => { }, token);
        }

        /// <summary>
        /// Creates a new instance of the specified TRequest request, configures and invokes it and returns its response.
        /// </summary>
        /// <typeparam name="TRequest">The request interface to invoke.</typeparam>
        /// <typeparam name="TResponse">The type of the request response result.</typeparam>
        /// <param name="configure">Action that configures a type instance that implements TRequest.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Returns the result of the request invocation.</returns>
        private async Task<TResponse> InvokeRequestAsync<TRequest, TResponse>(Action<TRequest> configure,
            CancellationToken token)
            where TRequest : IRequest<TResponse>
        {
            EnsureIsConnected();

            var request = requestFactory.Create<TRequest>(sonarQubeVersion);
            configure(request);

            var result = await request.InvokeAsync(httpClient, token);

            return result;
        }

        public async Task ConnectAsync(ConnectionInformation connection, CancellationToken token)
        {
            httpClient = new HttpClient(messageHandler)
            {
                BaseAddress = connection.ServerUri,
                DefaultRequestHeaders =
                {
                    Authorization = AuthenticationHeaderFactory.Create(
                        connection.UserName, connection.Password, connection.Authentication),
                },
            };

            httpClient.DefaultRequestHeaders.Add("User-Agent", userAgent);

            IsConnected = true;

            var versionResponse = await InvokeRequestAsync<IGetVersionRequest, string>(token);
            sonarQubeVersion = Version.Parse(versionResponse);

            var credentialResponse = await InvokeRequestAsync<IValidateCredentialsRequest, bool>(token);
            if (!credentialResponse)
            {
                IsConnected = false;
                throw new InvalidOperationException("Invalid credentials");
            }
        }

        public void Disconnect()
        {
            IsConnected = false;
            httpClient.Dispose();
        }

        private void EnsureIsConnected()
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("This operation expects the service to be connected.");
            }
        }

        public async Task<IList<SonarQubeOrganization>> GetAllOrganizationsAsync(CancellationToken token) =>
            await InvokeRequestAsync<IGetOrganizationsRequest, SonarQubeOrganization[]>(
                request =>
                {
                    request.OnlyUserOrganizations = true;
                },
                token);

        public async Task<IList<SonarQubePlugin>> GetAllPluginsAsync(CancellationToken token) =>
            await InvokeRequestAsync<IGetPluginsRequest, SonarQubePlugin[]>(token);

        public async Task<IList<SonarQubeProject>> GetAllProjectsAsync(string organizationKey, CancellationToken token) =>
            await InvokeRequestAsync<IGetProjectsRequest, SonarQubeProject[]>(
                request =>
                {
                    request.OrganizationKey = organizationKey;
                },
                token);

        public async Task<IList<SonarQubeProperty>> GetAllPropertiesAsync(CancellationToken token) =>
            await InvokeRequestAsync<IGetPropertiesRequest, SonarQubeProperty[]>(token);

        public Uri GetProjectDashboardUrl(string projectKey)
        {
            EnsureIsConnected();

            const string ProjectDashboardRelativeUrl = "dashboard/index/{0}";

            return new Uri(httpClient.BaseAddress, string.Format(ProjectDashboardRelativeUrl, projectKey));
        }

        public async Task<SonarQubeQualityProfile> GetQualityProfileAsync(string projectKey, string organizationKey, SonarQubeLanguage language, CancellationToken token)
        {
            var qualityProfiles = await InvokeRequestAsync<IGetQualityProfilesRequest, SonarQubeQualityProfile[]>(
                request =>
                {
                    request.ProjectKey = projectKey;
                    request.OrganizationKey = organizationKey;
                },
                token);

            // Consider adding the language filter to the request configuration above and removing this line
            var profilesWithGivenLanguage = qualityProfiles.Where(x => x.Language == language.Key).ToList();

            if (profilesWithGivenLanguage.Count == 0)
            {
                throw new InvalidOperationException($"The {language.PluginName} plugin is not installed on the connected SonarQube.");
            }

            var qualityProfile = profilesWithGivenLanguage.Count > 1
                ? profilesWithGivenLanguage.Single(x => x.IsDefault)
                : profilesWithGivenLanguage.Single();

            var changeLog = await InvokeRequestAsync<IGetQualityProfileChangeLogRequest, DateTime[]>(
                request =>
                {
                    request.Page = 1;
                    request.PageSize = 1;
                    request.QualityProfileKey = qualityProfile.Key;
                    request.QualityProfileName = qualityProfile.Name;
                    request.LanguageName = language.Key;
                },
                token);

            var updatedDate = changeLog.Any() ? changeLog.Single() : qualityProfile.TimeStamp;

            return new SonarQubeQualityProfile(qualityProfile.Key, qualityProfile.Name, qualityProfile.Language,
                qualityProfile.IsDefault, updatedDate);
        }

        public async Task<RoslynExportProfileResponse> GetRoslynExportProfileAsync(string qualityProfileName,
            string organizationKey, SonarQubeLanguage language, CancellationToken token) =>
            await InvokeRequestAsync<IGetRoslynExportProfileRequest, RoslynExportProfileResponse>(
                request =>
                {
                    request.QualityProfileName = qualityProfileName;
                    request.LanguageKey = language.Key;
                    request.OrganizationKey = organizationKey;
                },
                token);

        public async Task<IList<SonarQubeIssue>> GetSuppressedIssuesAsync(string projectKey, CancellationToken token)
        {
            var result = await InvokeRequestAsync<IGetIssuesRequest, SonarQubeIssue[]>(
                request =>
                {
                    request.ProjectKey = projectKey;
                },
                token);

            return result
                .Where(x => x.ResolutionState != SonarQubeIssueResolutionState.Unresolved)
                .ToList();
        }

        public async Task<IList<SonarQubeNotification>> GetNotificationEventsAsync(string projectKey, DateTimeOffset eventsSince,
            CancellationToken token) =>
            await InvokeRequestAsync<IGetNotificationsRequest, SonarQubeNotification[]>(
                request =>
                {
                    request.ProjectKey = projectKey;
                    request.EventsSince = eventsSince;
                },
                token);
    }
}
