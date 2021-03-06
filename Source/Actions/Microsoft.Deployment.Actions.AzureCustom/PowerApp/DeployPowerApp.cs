﻿using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

using Newtonsoft.Json.Linq;

using Microsoft.Deployment.Common.ActionModel;
using Microsoft.Deployment.Common.Actions;
using Microsoft.Deployment.Common.Helpers;

namespace Microsoft.Deployment.Actions.AzureCustom.PowerApp
{
    [Export(typeof(IAction))]
    public class DeployPowerApp : BaseAction
    {
        private const string BASE_POWER_APPS_URL = "https://management.azure.com/providers/Microsoft.PowerApps";
        private const string CREATE_POWER_APPS_URL = "https://create.powerapps.com/v2.0.610.0/api";
        private const string POWER_APP_GUID_CHARACTERS = "0123456789abcdef";

        private readonly int[] POWER_APP_GUID_LENGTHS = { 8, 4, 4, 4, 12 };

        public override async Task<ActionResponse> ExecuteActionAsync(ActionRequest request)
        {
            var azureToken = request.DataStore.GetJson("AzureToken", "access_token");

            AzureHttpClient client = new AzureHttpClient(azureToken);

            string applicationId = GetNewPowerAppGuid();
            string applicationName = "TwitterTemplate" + RandomGenerator.GetDateStamp();
            string environmentId = request.DataStore.GetValue("PowerAppEnvironment");
            string objectId = JsonUtility.GetWebToken(azureToken, "oid");
            string sqlConnectionId = request.DataStore.GetValue("PowerAppSqlConnectionId");

            JObject resourceStorage = JsonUtility.GetJsonObjectFromJsonString(await client.ExecuteGenericRequestWithHeaderAndReadAsync(HttpMethod.Post, $"{BASE_POWER_APPS_URL}/objectIds/{objectId}/generateResourceStorage?api-version=2016-11-01", $"{{\"environment\":{{\"id\":\"/providers/Microsoft.PowerApps/environments/{environmentId}\",\"name\":\"{environmentId}\"}}}}}}"));

            string sharedAccessSignature = JsonUtility.GetJObjectProperty(resourceStorage, "sharedAccessSignature");

            string backgroundImageUri = sharedAccessSignature.Replace("?", "/logoSmallFile?");
            string documentUri = sharedAccessSignature.Replace("?", "/document.msapp?");

            JObject initiateDocumentServerSession = JsonUtility.GetJsonObjectFromJsonString(await client.ExecuteGenericRequestWithHeaderAndReadAsync(HttpMethod.Post, $"{BASE_POWER_APPS_URL}/objectIds/{objectId}/initiateDocumentServerSession?api-version=2016-11-01", string.Empty));

            AzureHttpClient clientPA = new AzureHttpClient(azureToken, new Dictionary<string, string>() { { "AuthoringSessionToken", JsonUtility.GetJObjectProperty(initiateDocumentServerSession, "sessionToken") } });

            await clientPA.ExecuteGenericRequestWithHeaderAsync(HttpMethod.Post, $"{CREATE_POWER_APPS_URL}/authoringsession/newinstance?requestedLocation=unitedstates", string.Empty);
            await clientPA.ExecuteGenericRequestWithHeaderAsync(HttpMethod.Get, $"{CREATE_POWER_APPS_URL}/document/util/createdocumentandloadcontext?apptype=0&locale=en-US", string.Empty);
            await clientPA.ExecuteGenericRequestWithHeaderAsync(HttpMethod.Get, $"{CREATE_POWER_APPS_URL}/document/2/getpublishinfo", string.Empty);
            await clientPA.ExecuteGenericRequestWithHeaderAsync(HttpMethod.Post, $"{CREATE_POWER_APPS_URL}/document/2/setpublishinfo", $"{{\"applicationName\":\"{applicationName}\",\"backgroundColorString\":\"RGBA(0,176,240,1)\",\"logoFile\":{{\"_path\":\"default_icon.png\"}},\"logoFileName\":\"default_icon.png\",\"publishDataLocally\":false,\"publishResourcesLocally\":false,\"publishTarget\":null}}");
            await clientPA.ExecuteGenericRequestWithHeaderAsync(HttpMethod.Post, $"{CREATE_POWER_APPS_URL}/document/2/publishtoblobasync", $"{{\"blobURI\":\"{sharedAccessSignature}\",\"docName\":\"{applicationName}\",\"documentSienaUri\":\"/document.msapp\",\"logoSmallUri\":\"/logoSmallFile\"}}");
            await clientPA.ExecuteGenericRequestWithHeaderAsync(HttpMethod.Post, $"{BASE_POWER_APPS_URL}/apps?api-version=2016-11-01", $"{{\"properties\":{{\"appUris\":{{\"documentUri\":{{\"value\":\"{documentUri}\"}}}},\"description\":\"\",\"backgroundColor\":\"RGBA(0,176,240,1)\",\"minClientVersion\":{{\"major\":2,\"minor\":0,\"build\":610,\"majorRevision\":0,\"minorRevision\":0,\"revision\":0}},\"createdByClientVersion\":{{\"major\":2,\"minor\":0,\"build\":610,\"majorRevision\":0,\"minorRevision\":0,\"revision\":0}},\"backgroundImageUri\":\"{backgroundImageUri}\",\"displayName\":\"{applicationName}\",\"environment\":{{\"id\":\"/providers/Microsoft.PowerApps/environments/{environmentId}\",\"name\":\"{environmentId}\"}}}},\"tags\":{{\"sienaVersion\":\"{GetNewPowerAppGuid()}\",\"deviceCapabilities\":\"\",\"supportsPortrait\":\"false\",\"supportsLandscape\":\"true\",\"primaryFormFactor\":\"Tablet\",\"primaryDeviceWidth\":\"1366\",\"primaryDeviceHeight\":\"768\",\"publisherVersion\":\"2.0.610\",\"minimumRequiredApiVersion\":\"2.1.0\"}},\"name\":\"{applicationId}\"}}");

            return new ActionResponse(ActionStatus.Success, JsonUtility.GetEmptyJObject());
        }

        private string GetNewPowerAppGuid()
        {
            Random randy = new Random();
            StringBuilder sb = new StringBuilder();

            for (int i = 0; i < POWER_APP_GUID_LENGTHS.Length; i++)
            {
                for (int j = 0; j < POWER_APP_GUID_LENGTHS[i]; j++)
                {
                    sb.Append(POWER_APP_GUID_CHARACTERS[randy.Next(POWER_APP_GUID_CHARACTERS.Length)]);
                }
                if (i < POWER_APP_GUID_LENGTHS.Length - 1)
                {
                    sb.Append('-');
                }
            }

            return sb.ToString();
        }
    }
}