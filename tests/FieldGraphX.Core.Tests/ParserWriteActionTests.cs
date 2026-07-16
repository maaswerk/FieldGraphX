using System.Linq;
using FieldGraphX.Core.Models;
using FieldGraphX.Core.Parsing;
using Xunit;

namespace FieldGraphX.Core.Tests
{
    public class ParserWriteActionTests
    {
        private static bool IsKnownEntity(string name) =>
            name is "msdyn_workorder" or "account" or "systemuser" or "task" or "bookableresourcebooking";

        [Fact]
        public void NestedActions_AreFoundThroughScopesConditionsAndLoops()
        {
            var actions = FlowClientDataParser.ParseWriteActions(
                TestData.Fixture("actions_nested.json"), IsKnownEntity);

            Assert.Equal(2, actions.Count);

            var update = actions.Single(a => a.ActionName == "Update_booking");
            Assert.Equal(WriteActionKind.Update, update.Kind);
            Assert.Equal("bookableresourcebookings", update.RawEntityName);
            Assert.Equal(new[] { "msdyn_workordertravelstatus", "name" }, update.Fields);

            var create = actions.Single(a => a.ActionName == "Create_task");
            Assert.Equal(WriteActionKind.Create, create.Kind);
            Assert.Equal("tasks", create.RawEntityName);
            // "item/regardingobjectid_msdyn_workorder@odata.bind" → "regardingobjectid"
            Assert.Equal(new[] { "subject", "regardingobjectid" }, create.Fields);
        }

        [Fact]
        public void NonDataverseActionsWithItemKeys_AreIgnored()
        {
            // SharePoint PatchItem also uses item/* keys but must not count as a field write.
            var actions = FlowClientDataParser.ParseWriteActions(
                TestData.Fixture("actions_non_dataverse_item_keys.json"));

            Assert.Empty(actions);
        }

        [Fact]
        public void MalformedClientData_YieldsNoActions()
        {
            var actions = FlowClientDataParser.ParseWriteActions(TestData.Fixture("clientdata_malformed.json"));
            Assert.Empty(actions);
        }

        [Theory]
        [InlineData("msdyn_name", "msdyn_name")]
        [InlineData("customerid_account@odata.bind", "customerid")]
        [InlineData("ownerid_systemuser@odata.bind", "ownerid")]
        [InlineData("regardingobjectid_msdyn_workorder@odata.bind", "regardingobjectid")]
        [InlineData("statecode", "statecode")]
        public void NormalizeFieldKey_StripsODataBindSuffixes_WithMetadata(string raw, string expected)
        {
            Assert.Equal(expected, FlowClientDataParser.NormalizeFieldKey(raw, IsKnownEntity));
        }

        [Theory]
        [InlineData("customerid_account@odata.bind", "customerid")]
        [InlineData("title", "title")]
        public void NormalizeFieldKey_FallsBackToLastUnderscore_WithoutMetadata(string raw, string expected)
        {
            Assert.Equal(expected, FlowClientDataParser.NormalizeFieldKey(raw));
        }

        [Fact]
        public void EmittedChange_FollowsActionKind()
        {
            var update = new UpdateActionInfo { Kind = WriteActionKind.Update };
            var create = new UpdateActionInfo { Kind = WriteActionKind.Create };
            var upsert = new UpdateActionInfo { Kind = WriteActionKind.Upsert };

            Assert.Equal(ChangeType.Update, update.EmittedChange);
            Assert.Equal(ChangeType.Create, create.EmittedChange);
            Assert.Equal(ChangeType.Create | ChangeType.Update, upsert.EmittedChange);
        }
    }
}
