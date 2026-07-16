using FieldGraphX.Core.Models;
using FieldGraphX.Core.Parsing;
using Xunit;

namespace FieldGraphX.Core.Tests
{
    public class ParserTriggerTests
    {
        [Fact]
        public void FilteredUpdateTrigger_IsParsedCompletely()
        {
            var trigger = FlowClientDataParser.ParseTrigger(TestData.Fixture("trigger_update_filtered.json"));

            Assert.Equal(TriggerKind.CdsRowChange, trigger.Kind);
            Assert.Equal("msdyn_workorder", trigger.RawEntityName);
            Assert.Equal(ChangeType.Update, trigger.ChangeTypes);
            Assert.Equal(new[] { "msdyn_name", "statuscode" }, trigger.FilteringAttributes);
            Assert.False(trigger.IsBroadTrigger);
            Assert.Equal("When_a_row_is_modified", trigger.TriggerKey);
        }

        [Fact]
        public void MissingFilteringAttributes_IsBroadTrigger()
        {
            var trigger = FlowClientDataParser.ParseTrigger(TestData.Fixture("trigger_broad_update.json"));

            Assert.Equal(TriggerKind.CdsRowChange, trigger.Kind);
            Assert.True(trigger.IsBroadTrigger);
            Assert.Equal(ChangeType.Create | ChangeType.Update, trigger.ChangeTypes);
        }

        [Fact]
        public void EmptyFilteringAttributes_IsBroadTrigger()
        {
            var trigger = FlowClientDataParser.ParseTrigger(TestData.Fixture("trigger_broad_empty_attrs.json"));

            Assert.True(trigger.IsBroadTrigger);
            Assert.Empty(trigger.FilteringAttributes);
        }

        [Fact]
        public void FilterExpression_IsKeptRaw_AndDoesNotBecomeATriggerField()
        {
            // Regression: the old code treated the first token of a filterexpression as a
            // filtering attribute. A filter expression is a run condition, not a trigger field.
            var trigger = FlowClientDataParser.ParseTrigger(TestData.Fixture("trigger_with_filterexpression.json"));

            Assert.True(trigger.IsBroadTrigger);
            Assert.Empty(trigger.FilteringAttributes);
            Assert.Equal("statecode eq 0", trigger.FilterExpression);
        }

        [Fact]
        public void ManualTrigger_IsClassifiedManual()
        {
            var trigger = FlowClientDataParser.ParseTrigger(TestData.Fixture("trigger_manual.json"));
            Assert.Equal(TriggerKind.Manual, trigger.Kind);
        }

        [Fact]
        public void RecurrenceTrigger_IsClassifiedScheduled()
        {
            var trigger = FlowClientDataParser.ParseTrigger(TestData.Fixture("trigger_recurrence.json"));
            Assert.Equal(TriggerKind.Scheduled, trigger.Kind);
        }

        [Fact]
        public void OtherConnectorTrigger_IsClassifiedOther()
        {
            var trigger = FlowClientDataParser.ParseTrigger(TestData.Fixture("trigger_other_connector.json"));
            Assert.Equal(TriggerKind.Other, trigger.Kind);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void EmptyClientData_YieldsKindNone(string clientData)
        {
            var trigger = FlowClientDataParser.ParseTrigger(clientData);
            Assert.Equal(TriggerKind.None, trigger.Kind);
        }

        [Fact]
        public void MalformedClientData_YieldsKindNone()
        {
            var trigger = FlowClientDataParser.ParseTrigger(TestData.Fixture("clientdata_malformed.json"));
            Assert.Equal(TriggerKind.None, trigger.Kind);
        }

        [Theory]
        [InlineData(1, ChangeType.Create)]
        [InlineData(2, ChangeType.Delete)]
        [InlineData(3, ChangeType.Update)]
        [InlineData(4, ChangeType.Create | ChangeType.Update)]
        [InlineData(5, ChangeType.Create | ChangeType.Delete)]
        [InlineData(6, ChangeType.Update | ChangeType.Delete)]
        [InlineData(7, ChangeType.Create | ChangeType.Update | ChangeType.Delete)]
        public void SubscriptionMessage_MapsToChangeTypes(int message, ChangeType expected)
        {
            var clientData = TestData.ClientData(triggerEntity: "account", message: message);
            var trigger = FlowClientDataParser.ParseTrigger(clientData);
            Assert.Equal(expected, trigger.ChangeTypes);
        }
    }
}
