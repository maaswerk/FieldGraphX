using System;
using System.Collections.Generic;
using System.Threading;
using FieldGraphX.Core.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace FieldGraphX.Services
{
    /// <summary>
    /// Fetches all cloud flows (workflow rows with category = 5) via paging.
    /// One fetch serves the entire recursive analysis — the analyzer never queries again.
    /// </summary>
    public sealed class DataverseFlowRepository : IFlowRepository
    {
        // clientdata rows are large; small pages keep responses within service limits.
        private const int PageSize = 500;
        private const int CloudFlowCategory = 5;

        private readonly IOrganizationService _service;

        public DataverseFlowRepository(IOrganizationService service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }

        public IReadOnlyList<FlowRecord> GetAllCloudFlows(
            Action<int> progress = null,
            CancellationToken cancellationToken = default)
        {
            var records = new List<FlowRecord>();

            var query = new QueryExpression("workflow")
            {
                ColumnSet = new ColumnSet(
                    "workflowid", "workflowidunique", "name", "clientdata", "statecode", "type"),
                PageInfo = new PagingInfo { Count = PageSize, PageNumber = 1 }
            };
            query.Criteria.AddCondition("category", ConditionOperator.Equal, CloudFlowCategory);

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var page = _service.RetrieveMultiple(query);

                foreach (var entity in page.Entities)
                {
                    records.Add(new FlowRecord
                    {
                        WorkflowId = entity.GetAttributeValue<Guid>("workflowid"),
                        WorkflowIdUnique = entity.GetAttributeValue<Guid>("workflowidunique"),
                        Name = entity.GetAttributeValue<string>("name") ?? string.Empty,
                        ClientData = entity.GetAttributeValue<string>("clientdata") ?? string.Empty,
                        StateCode = entity.GetAttributeValue<OptionSetValue>("statecode")?.Value ?? 1,
                        Type = entity.GetAttributeValue<OptionSetValue>("type")?.Value ?? 1
                    });
                }

                progress?.Invoke(records.Count);

                if (!page.MoreRecords) break;
                query.PageInfo.PageNumber++;
                query.PageInfo.PagingCookie = page.PagingCookie;
            }

            return records;
        }
    }
}
