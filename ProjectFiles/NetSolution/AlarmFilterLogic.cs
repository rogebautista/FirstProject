#region Using directives
using UAManagedCore;
using FTOptix.NetLogic;
using FTOptix.UI;
using System.Collections.Generic;
using FTOptix.HMIProject;
using System;
using System.Linq;
using FTOptix.SerialPort;
using FTOptix.EventLogger;
using static AlarmFilterDataLogic;
#endregion

public class AlarmFilterLogic : BaseNetLogic
{
    public override void Start()
    {
        alarmFilter = new AlarmFilter(Owner);
    }

    [ExportMethod]
    public void Filter(string filterBrowseName)
    {
        alarmFilter.IsValidFilterBrowseName(filterBrowseName);
        alarmFilter.Refresh();
    }

    [ExportMethod]
    public void Refresh()
    {
        alarmFilter.Refresh();
    }

    [ExportMethod]
    public void Apply()
    {
        alarmFilter.SaveAll();
        alarmFilter.Refresh();
    }

    [ExportMethod]
    public void ClearAll()
    {
        alarmFilter.ClearFilters();
        alarmFilter.SaveAll();
        alarmFilter.Refresh();
    }

    private sealed class AlarmFilter
    {
        public AlarmFilter(IUANode owner)
        {
            Owner = owner;

            try
            {
                filterConfiguration = AlarmFilterEditModelLogic.GetEditModel(AlarmWidgetConfiguration, "FiltersConfiguration");
            }
            catch (CoreConfigurationException ex)
            {
                Log.Warning("Filters configuration in AlarmWidgetEditModel not found: " + ex.Message);
            }

            InitializeAlarmFilterData();
            queryBuilder = new()
            {
                Query = AlarmWidget.Get("Layout/AlarmsDataGrid").GetVariable("Query")
            };
            AlarmFilterEditModelLogic.CreateEditModel(AlarmWidgetConfiguration, filterConfiguration);

            InitializeCheckBoxes();
            InitializeDateTimePickers();
            InitializeTextBoxes();
            ExpandAccordions();
        }

        public void SaveAll()
        {
            SaveCheckBoxes();
            SaveDateTimePickers();
            SaveTextBoxes();
        }

        public void IsValidFilterBrowseName(string filterBrowseName)
        {
            if (!alarmFilterData.Filters.Any(x => x.Name == filterBrowseName))
            {
                Log.Warning($"Filter {filterBrowseName} browse name not found");
            }
        }

        public void ClearFilters()
        {
            foreach (var filter in alarmFilterData.Filters)
            {
                filter.IsChecked = false;
            }
        }

        public void Refresh()
        {
            queryBuilder.BuildQuery(alarmFilterData, filterConfiguration);
            queryBuilder.RefreshQuery();
        }

        public IUANode AlarmWidget
        {
            get
            {
                var aliasNodeId = Owner.GetVariable("ModelAlias").Value;
                var alarmWidget = InformationModel.Get(aliasNodeId);
                return alarmWidget ?? throw new CoreConfigurationException("ModelAlias node id not found");
            }
        }

        public IUANode AlarmWidgetConfiguration
        {
            get
            {
                var nodePointer = AlarmWidget.GetVariable("ConfigurationPointer").Value;
                var alarmWidgetConfiguration = InformationModel.Get(nodePointer);
                return alarmWidgetConfiguration ?? throw new CoreConfigurationException("AlarmWidgetConfiguration not found");
            }
        }

        private void InitializeAlarmFilterData()
        {
            var baseLayout = Owner.Get("Filters/ScrollView/Layout");
            AlarmWidgetObjectsGenerator.GenerateAccordions(baseLayout, filterConfiguration, filterData);
            ProcessAttribute([.. baseLayout.Children]);
        }

        private void ProcessAttribute(IEnumerable<IUANode> nodes)
        {
            foreach (var node in nodes)
            {
                if (node == null)
                    return;

                if (node is Accordion accordion)
                {
                    if (Enum.TryParse(accordion.BrowseName, out FilterAttribute attribute))
                    {
                        ProcessContent([.. accordion.Get("Content").Children], attribute, accordion);
                    }
                    else
                    {
                        Log.Warning($"Accordion {accordion.BrowseName} browse name is not a valid FilterAttribute.");
                    }
                }
            }
        }

        private void ProcessContent(IEnumerable<IUANode> nodes, FilterAttribute attribute, Accordion accordion)
        {
            foreach (var node in nodes)
            {
                if (node == null)
                    return;

                if (node is ColumnLayout columnLayout)
                {
                    ProcessContent([.. columnLayout.Children], attribute, accordion);
                }

                if (node is RowLayout rowLayout)
                {
                    ProcessContent([.. rowLayout.Children], attribute, accordion);
                }

                if (node is CheckBox checkbox)
                {
                    alarmFilterData.Filters.Add(new CheckBoxFilter(checkbox, attribute, accordion));
                }
            }
        }

        private void InitializeDateTimePickers()
        {
            var eventTimeVariable = filterConfiguration.GetVariable(eventTimeBrowseName);
            if (eventTimeVariable.Value)
            {
                if (eventTimeVariable.GetVariable(fromEventTimeBrowseName).Value)
                    InitializeDateTimePicker(fromEventTimeBrowseName);
                if (eventTimeVariable.GetVariable(toEventTimeBrowseName).Value)
                    InitializeDateTimePicker(toEventTimeBrowseName);
            }
        }

        private void InitializeDateTimePicker(string name)
        {
            var filter = alarmFilterData.Filters.First(x => x.Name == name &&
                                             x.Attribute == FilterAttribute.EventTime);

            if (filter.IsChecked)
                filterData.EventTimePickers.GetValueOrDefault(name).Value = 
                    GetFiltersModelVariable(name + dateTimeBrowseName, FilterAttribute.EventTime).Value;
            else
                filterData.EventTimePickers.GetValueOrDefault(name).Value = DateTime.Now;
        }

        private void InitializeTextBoxes()
        {
            var severityAttributeVariable = filterConfiguration.GetVariable(severityBrowseName);
            var severityChildVariable = severityAttributeVariable.GetVariable(severityBrowseName);

            if (severityAttributeVariable.Value && severityChildVariable.Value)
            {
                var severityFilter = alarmFilterData.Filters.First(x => x.Name == severityBrowseName &&
                                                    x.Attribute == FilterAttribute.Severity);

                if (severityFilter.IsChecked)
                {
                    filterData.TextBoxes.GetValueOrDefault(fromSeverityBrowseName).Text =
                        GetFiltersModelVariable(fromSeverityBrowseName, FilterAttribute.Severity).Value;
                    filterData.TextBoxes.GetValueOrDefault(toSeverityBrowseName).Text =
                        GetFiltersModelVariable(toSeverityBrowseName, FilterAttribute.Severity).Value;
                }
                else
                {
                    filterData.TextBoxes.GetValueOrDefault(fromSeverityBrowseName).Text = "1";
                    filterData.TextBoxes.GetValueOrDefault(toSeverityBrowseName).Text = "1000";
                }
            }
        }

        private void InitializeCheckBoxes()
        {
            foreach (var (filter, isChecked) in from filter in alarmFilterData.Filters
                                                let isChecked = GetFiltersModelVariable(filter.Name, filter.Attribute).Value
                                                select (filter, isChecked))
                filter.IsChecked = isChecked;
        }

        private void ExpandAccordions()
        {
            var attributes = alarmFilterData.Filters
                      .Select(x => x.Attribute)
                      .Distinct()
                      .ToList();

            foreach (var attribute in attributes)
            {
                var isChecked = alarmFilterData.Filters.FindAll(x => x.Attribute == attribute)
                                       .Any(x => x.IsChecked);

                var filter = (CheckBoxFilter)alarmFilterData.Filters.First(x => x.Attribute == attribute);
                ExpandAccordion(filter, isChecked);
            }
        }

        private static void ExpandAccordion(CheckBoxFilter filter, bool value)
        {
            filter.Accordion.Expanded = value;
        }

        private void SaveCheckBoxes()
        {
            foreach (var filter in alarmFilterData.Filters)
            {
                GetFiltersModelVariable(filter.Name, filter.Attribute).Value = filter.IsChecked;
            }
        }

        private void SaveDateTimePickers()
        {
            var eventTimeVariable = filterConfiguration.GetVariable(eventTimeBrowseName);
            if (eventTimeVariable.Value)
            {
                if (eventTimeVariable.GetVariable(fromEventTimeBrowseName).Value)
                    GetFiltersModelVariable(fromEventTimeDateTimeBrowseName, FilterAttribute.EventTime).Value = 
                        filterData.EventTimePickers.GetValueOrDefault(fromEventTimeBrowseName).Value;
                if (eventTimeVariable.GetVariable(toEventTimeBrowseName).Value)
                    GetFiltersModelVariable(toEventTimeDateTimeBrowseName, FilterAttribute.EventTime).Value =
                        filterData.EventTimePickers.GetValueOrDefault(toEventTimeBrowseName).Value;
            }

        }

        private void SaveTextBoxes()
        {
            var severityAttributeVariable = filterConfiguration.GetVariable(severityBrowseName);
            var severityChildVariable = severityAttributeVariable.GetVariable(severityBrowseName);
            if (severityAttributeVariable.Value && severityChildVariable.Value)
            {
                GetFiltersModelVariable(fromSeverityBrowseName, FilterAttribute.Severity).Value =
                    filterData.TextBoxes.GetValueOrDefault(fromSeverityBrowseName).Text;
                GetFiltersModelVariable(toSeverityBrowseName, FilterAttribute.Severity).Value =
                    filterData.TextBoxes.GetValueOrDefault(toSeverityBrowseName).Text;
            }
        }

        private IUAVariable GetFiltersModelVariable(string browseName, FilterAttribute attribute)
        {
            var filtersModel = AlarmFilterEditModelLogic.GetEditModel(AlarmWidgetConfiguration);
            var attributeVariable = filtersModel.GetVariable(attribute.ToString());
            if (attributeVariable == null)
            {
                Log.Warning($"FilterModel attribute: {attribute} not found. Generate default settings again.");
            }

            var variable = attributeVariable.GetVariable(browseName);
            if (variable == null)
            {
                Log.Warning($"FilterModel variable: {browseName} not found. Generate default settings again.");
            }
            return variable;
        }

        private CheckBoxFilterData filterData { get => (CheckBoxFilterData)alarmFilterData.Data; }
        private readonly AlarmFilterDataLogic alarmFilterData = new()
        {
            Data = new CheckBoxFilterData()
        };
        private readonly AlarmFilterQueryBuilderLogic queryBuilder;
        private readonly IUANode Owner;
        private readonly IUANode filterConfiguration;
    }

    private AlarmFilter alarmFilter;
}
