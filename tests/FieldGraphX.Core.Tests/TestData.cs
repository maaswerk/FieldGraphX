using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FieldGraphX.Core.Abstractions;

namespace FieldGraphX.Core.Tests
{
    internal static class TestData
    {
        public static string Fixture(string name) =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

        /// <summary>Builds a minimal but structurally realistic clientdata JSON.</summary>
        public static string ClientData(
            string triggerEntity = null,
            string filteringAttributes = null,
            int message = 3,
            params (string entity, string[] fields, string operationId)[] writes)
        {
            var trigger = triggerEntity == null
                ? @"""manual"": { ""type"": ""Request"", ""inputs"": {} }"
                : $@"""When_a_row_changes"": {{
                    ""type"": ""OpenApiConnectionWebhook"",
                    ""inputs"": {{
                      ""host"": {{ ""operationId"": ""SubscribeWebhookTrigger"" }},
                      ""parameters"": {{
                        ""subscriptionRequest/message"": {message},
                        ""subscriptionRequest/entityname"": ""{triggerEntity}""
                        {(filteringAttributes == null ? "" : $@", ""subscriptionRequest/filteringattributes"": ""{filteringAttributes}""")}
                      }}
                    }}
                  }}";

            var actions = string.Join(",", writes.Select((w, i) =>
            {
                var items = string.Join(",", w.fields.Select(f => $@"""item/{f}"": ""value"""));
                return $@"""Write_{i}"": {{
                    ""type"": ""OpenApiConnection"",
                    ""inputs"": {{
                      ""host"": {{ ""operationId"": ""{w.operationId}"" }},
                      ""parameters"": {{ ""entityName"": ""{w.entity}"", {items} }}
                    }}
                  }}";
            }));

            return $@"{{ ""properties"": {{ ""definition"": {{
                ""triggers"": {{ {trigger} }},
                ""actions"": {{ {actions} }}
            }} }} }}";
        }

        public static FlowRecord Record(
            string name,
            string clientData,
            int stateCode = 1,
            int type = 1)
        {
            return new FlowRecord
            {
                WorkflowId = DeterministicGuid(name),
                WorkflowIdUnique = DeterministicGuid(name + "|unique"),
                Name = name,
                ClientData = clientData,
                StateCode = stateCode,
                Type = type
            };
        }

        /// <summary>Stable guid per name so tests can reference flows by name.</summary>
        public static Guid DeterministicGuid(string name)
        {
            var bytes = new byte[16];
            var hash = name.Aggregate(17, (acc, c) => unchecked(acc * 31 + c));
            BitConverter.GetBytes(hash).CopyTo(bytes, 0);
            BitConverter.GetBytes(name.Length).CopyTo(bytes, 8);
            return new Guid(bytes);
        }
    }
}
