﻿using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Deployment.Common.ActionModel;
using Microsoft.Deployment.Common.Helpers;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.WebServiceClient;
using Newtonsoft.Json;

namespace Microsoft.Deployment.Common.Actions.MsCrm
{
    [Export(typeof(IAction))]
    public class CrmGetEntityInitialCounts : BaseAction
    {
        public async override Task<ActionResponse> ExecuteActionAsync(ActionRequest request)
        {

            var dataMovement = request.DataStore.GetValue("DataMovement");
            if(dataMovement == "Scribe")
            {
                return new ActionResponse(ActionStatus.Success);
            }

            string refreshToken = request.DataStore.GetJson("MsCrmToken")["refresh_token"].ToString();
            string organizationUrl = request.DataStore.GetValue("OrganizationUrl");
            string[] entities = request.DataStore.GetValue("Entities").Split(new[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

            var crmToken = CrmTokenUtility.RetrieveCrmOnlineToken(refreshToken, request.Info.WebsiteRootUrl, request.DataStore, organizationUrl);

            Dictionary<string, int> initialCounts = new Dictionary<string, int>();

            var proxy = new OrganizationWebProxyClient(new Uri($"{organizationUrl}XRMServices/2011/Organization.svc/web"), true)
            {
                HeaderToken = crmToken["access_token"].ToString()
            };

            string count = string.Empty;
            int max = 0;

            foreach (var entry in entities)
            {
                try
                {
                    var xml = $@"
                        <fetch distinct='false' mapping='logical' aggregate='true'> 
                            <entity name='{entry}'> 
                                <attribute name='{entry}id' alias='{entry}_count' aggregate='count'/> 
                            </entity> 
                        </fetch>";

                    var fetchRequest = new ExecuteFetchRequest() { FetchXml = xml };

                    var result = (ExecuteFetchResponse)proxy.Execute(fetchRequest);

                    var xdoc = XDocument.Parse(result.FetchXmlResult);

                    count = xdoc.Descendants().First(e => e.Name == $"{entry}_count").Value;
                }
                catch (Exception e)
                {
                    if (e.Message == "AggregateQueryRecordLimit exceeded. Cannot perform this operation.")
                    {
                        count = "-1";
                    }
                    else throw;
                }

                if(Convert.ToInt16(count) > max)
                {
                    max = Convert.ToInt16(count);
                }

                initialCounts.Add(entry.ToLowerInvariant(), Convert.ToInt16(count));
            }
            var missingCounts = new List<string>();

            foreach (var pair in initialCounts)
            {
                if(pair.Value == -1)
                {
                    missingCounts.Add(pair.Key);
                }
            }

            foreach(var entry in missingCounts)
            {
                initialCounts[entry] = max;
            }

            request.DataStore.AddToDataStore("InitialCounts", JsonUtility.GetJObjectFromObject(initialCounts));
            return new ActionResponse(ActionStatus.Success);
        }
    }
}
