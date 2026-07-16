using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;

namespace FieldGraphX.Services
{
    public sealed class EntityInfo
    {
        public string LogicalName { get; set; }
        public string DisplayName { get; set; }
        public string EntitySetName { get; set; }
        public string LogicalCollectionName { get; set; }

        public override string ToString() =>
            string.IsNullOrEmpty(DisplayName) ? LogicalName : $"{LogicalName} ({DisplayName})";
    }

    public sealed class AttributeInfo
    {
        public string LogicalName { get; set; }
        public string DisplayName { get; set; }

        public override string ToString() =>
            string.IsNullOrEmpty(DisplayName) ? LogicalName : $"{LogicalName} ({DisplayName})";
    }

    /// <summary>
    /// Loads entity/attribute metadata. Plain service class — constructed and used inside
    /// WorkAsync delegates; throws on error so PostWorkCallBack can surface it.
    /// </summary>
    public sealed class MetadataRepository
    {
        private readonly IOrganizationService _service;

        public MetadataRepository(IOrganizationService service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }

        public IReadOnlyList<EntityInfo> LoadEntities()
        {
            var response = (RetrieveAllEntitiesResponse)_service.Execute(
                new RetrieveAllEntitiesRequest
                {
                    EntityFilters = EntityFilters.Entity,
                    RetrieveAsIfPublished = false
                });

            return response.EntityMetadata
                .Where(e => !string.IsNullOrEmpty(e.LogicalName))
                .Select(e => new EntityInfo
                {
                    LogicalName = e.LogicalName,
                    DisplayName = e.DisplayName?.UserLocalizedLabel?.Label,
                    EntitySetName = e.EntitySetName,
                    LogicalCollectionName = e.LogicalCollectionName
                })
                .OrderBy(e => e.LogicalName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public IReadOnlyList<AttributeInfo> LoadAttributes(string entityLogicalName)
        {
            var response = (RetrieveEntityResponse)_service.Execute(
                new RetrieveEntityRequest
                {
                    LogicalName = entityLogicalName,
                    EntityFilters = EntityFilters.Attributes,
                    RetrieveAsIfPublished = false
                });

            return response.EntityMetadata.Attributes
                .Where(a => !string.IsNullOrEmpty(a.LogicalName) &&
                            a.AttributeOf == null) // skip virtual helper attributes (…name, …yominame)
                .Select(a => new AttributeInfo
                {
                    LogicalName = a.LogicalName,
                    DisplayName = a.DisplayName?.UserLocalizedLabel?.Label
                })
                .OrderBy(a => a.LogicalName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>Builds the EntitySetName/LogicalCollectionName → LogicalName map for the resolver.</summary>
        public static IReadOnlyList<KeyValuePair<string, string>> BuildNameMap(IEnumerable<EntityInfo> entities)
        {
            var map = new List<KeyValuePair<string, string>>();
            foreach (var entity in entities)
            {
                // Logical name maps to itself so the resolver knows it as a "known logical name".
                map.Add(new KeyValuePair<string, string>(entity.LogicalName, entity.LogicalName));
                if (!string.IsNullOrEmpty(entity.EntitySetName))
                    map.Add(new KeyValuePair<string, string>(entity.EntitySetName, entity.LogicalName));
                if (!string.IsNullOrEmpty(entity.LogicalCollectionName))
                    map.Add(new KeyValuePair<string, string>(entity.LogicalCollectionName, entity.LogicalName));
            }
            return map;
        }
    }
}
