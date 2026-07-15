using System.Collections.Generic;
using FieldGraphX.Core.Analysis;
using Xunit;

namespace FieldGraphX.Core.Tests
{
    public class EntityNameResolverTests
    {
        private static EntityNameResolver WithMetadata() => new EntityNameResolver(
            new Dictionary<string, string>
            {
                ["accounts"] = "account",
                ["opportunities"] = "opportunity",
                ["msdyn_workorders"] = "msdyn_workorder",
                ["customeraddresses"] = "customeraddress"
            });

        [Theory]
        [InlineData("accounts", "account")]
        [InlineData("opportunities", "opportunity")]
        [InlineData("msdyn_workorders", "msdyn_workorder")]
        [InlineData("ACCOUNTS", "account")]
        public void MetadataMapping_Wins(string input, string expected)
        {
            Assert.Equal(expected, WithMetadata().ToLogicalName(input));
        }

        [Fact]
        public void KnownLogicalName_IsReturnedUnchanged()
        {
            // "customeraddress" ends in "s"-like patterns but is already a logical name.
            Assert.Equal("customeraddress", WithMetadata().ToLogicalName("customeraddress"));
        }

        [Theory]
        [InlineData("opportunities", "opportunity")] // ies → y
        [InlineData("addresses", "address")]         // ses → s (strip "es")
        [InlineData("accounts", "account")]          // plain s
        [InlineData("address", "address")]           // ends in "ss" — untouched
        [InlineData("msdyn_workorder", "msdyn_workorder")]
        public void Heuristic_HandlesStandardPlurals_WhenNoMetadata(string input, string expected)
        {
            var resolver = new EntityNameResolver();
            Assert.Equal(expected, resolver.ToLogicalName(input));
        }
    }
}
