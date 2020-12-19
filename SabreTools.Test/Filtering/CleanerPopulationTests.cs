using System.Collections.Generic;

using SabreTools.Filtering;
using Xunit;

namespace SabreTools.Test.Filtering
{
    public class CleanerPopulationTests
    {
        [Fact]
        public void PopulateExclusionNullListTest()
        {
            // Setup the list
            List<string> exclusions = null;

            // Setup the cleaner
            var cleaner = new Cleaner();
            cleaner.PopulateExclusionsFromList(exclusions);

            // Check the exclusion lists
            Assert.Empty(cleaner.ExcludeDatHeaderFields);
            Assert.Empty(cleaner.ExcludeMachineFields);
            Assert.Empty(cleaner.ExcludeDatItemFields);
        }

        [Fact]
        public void PopulateExclusionEmptyListTest()
        {
            // Setup the list
            List<string> exclusions = new List<string>();

            // Setup the cleaner
            var cleaner = new Cleaner();
            cleaner.PopulateExclusionsFromList(exclusions);

            // Check the exclusion lists
            Assert.Empty(cleaner.ExcludeDatHeaderFields);
            Assert.Empty(cleaner.ExcludeMachineFields);
            Assert.Empty(cleaner.ExcludeDatItemFields);
        }

        [Fact]
        public void PopulateExclusionHeaderFieldTest()
        {
            // Setup the list
            List<string> exclusions = new List<string>
            {
                "header.datname",
            };

            // Setup the cleaner
            var cleaner = new Cleaner();
            cleaner.PopulateExclusionsFromList(exclusions);

            // Check the exclusion lists
            Assert.Single(cleaner.ExcludeDatHeaderFields);
            Assert.Empty(cleaner.ExcludeMachineFields);
            Assert.Empty(cleaner.ExcludeDatItemFields);
        }

        [Fact]
        public void PopulateExclusionMachineFieldTest()
        {
            // Setup the list
            List<string> exclusions = new List<string>
            {
                "machine.name",
            };

            // Setup the cleaner
            var cleaner = new Cleaner();
            cleaner.PopulateExclusionsFromList(exclusions);

            // Check the exclusion lists
            Assert.Empty(cleaner.ExcludeDatHeaderFields);
            Assert.Single(cleaner.ExcludeMachineFields);
            Assert.Empty(cleaner.ExcludeDatItemFields);
        }

        [Fact]
        public void PopulateExclusionDatItemFieldTest()
        {
            // Setup the list
            List<string> exclusions = new List<string>
            {
                "item.name",
            };

            // Setup the cleaner
            var cleaner = new Cleaner();
            cleaner.PopulateExclusionsFromList(exclusions);

            // Check the exclusion lists
            Assert.Empty(cleaner.ExcludeDatHeaderFields);
            Assert.Empty(cleaner.ExcludeMachineFields);
            Assert.Single(cleaner.ExcludeDatItemFields);
        }
    
        [Fact]
        public void PopulateFilterNullListTest()
        {
            // Setup the list
            List<string> filters = null;

            // Setup the cleaner
            var cleaner = new Cleaner();
            cleaner.PopulateFiltersFromList(filters);

            // Check the filters
            Assert.NotNull(cleaner.MachineFilter);
            Assert.NotNull(cleaner.DatItemFilter);
        }

        [Fact]
        public void PopulateFilterEmptyListTest()
        {
            // Setup the list
            List<string> filters = new List<string>();

            // Setup the cleaner
            var cleaner = new Cleaner();
            cleaner.PopulateFiltersFromList(filters);

            // Check the filters
            Assert.NotNull(cleaner.MachineFilter);
            Assert.NotNull(cleaner.DatItemFilter);
        }

        [Fact]
        public void PopulateFilterMachineFieldTest()
        {
            // Setup the list
            List<string> filters = new List<string>
            {
                "machine.name:foo",
                "!machine.name:bar",
            };

            // Setup the cleaner
            var cleaner = new Cleaner();
            cleaner.PopulateFiltersFromList(filters);

            // Check the filters
            Assert.Contains("foo", cleaner.MachineFilter.Name.PositiveSet);
            Assert.Contains("bar", cleaner.MachineFilter.Name.NegativeSet);
            Assert.NotNull(cleaner.DatItemFilter);
        }

        [Fact]
        public void PopulateFilterDatItemFieldTest()
        {
            // Setup the list
            List<string> filters = new List<string>
            {
                "item.name:foo",
                "!item.name:bar"
            };

            // Setup the cleaner
            var cleaner = new Cleaner();
            cleaner.PopulateFiltersFromList(filters);

            // Check the filters
            Assert.NotNull(cleaner.MachineFilter);
            Assert.Contains("foo", cleaner.DatItemFilter.Name.PositiveSet);
            Assert.Contains("bar", cleaner.DatItemFilter.Name.NegativeSet);
        }
    }
}