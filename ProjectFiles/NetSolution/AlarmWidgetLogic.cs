#region Using directives
using UAManagedCore;
using FTOptix.NetLogic;
using FTOptix.HMIProject;
using System;
using FTOptix.SerialPort;
using FTOptix.EventLogger;
#endregion

public class AlarmWidgetLogic : BaseNetLogic
{
    public override void Start()
    {
        alarmsDataGridModel = Owner.GetVariable("Layout/AlarmsDataGrid/Model");

        var currentSession = LogicObject.Context.Sessions.CurrentSessionInfo;
        actualLanguageVariable = currentSession.SessionObject.Get<IUAVariable>("ActualLanguage");
        actualLanguageVariable.VariableChange += OnSessionActualLanguageChange;

        filterConfiguration = AlarmFilterEditModelLogic.GetEditModel(AlarmWidgetConfiguration, filtersConfigurationBrowseName);
        CreateFiltersData();
        PrepareQuery();
    }

    public override void Stop()
    {
        actualLanguageVariable.VariableChange -= OnSessionActualLanguageChange;
    }

    public void OnSessionActualLanguageChange(object sender, VariableChangeEventArgs e)
    {
        var dynamicLink = alarmsDataGridModel.GetVariable("DynamicLink");
        if (dynamicLink == null)
            return;

        // Restart the data bind on the data grid model variable to refresh data
        dynamicLink.Stop();
        dynamicLink.Start();
    }

    private void CreateFiltersData()
    {
        AlarmFilterEditModelLogic.CreateEditModel(AlarmWidgetConfiguration, filterConfiguration);
        var configuration = AlarmFilterEditModelLogic.GetEditModel(AlarmWidgetConfiguration);

        foreach (var attributeNode in configuration.Children)
        {
            if (Enum.TryParse(attributeNode.BrowseName, out AlarmFilterDataLogic.FilterAttribute attribute))
            {
                foreach (var child in attributeNode.Children)
                {
                    var value = InformationModel.GetVariable(child.NodeId).Value;

                    if (child.BrowseName == AlarmFilterDataLogic.fromEventTimeDateTimeBrowseName)
                        filterData.SetFromEventTime((DateTime)value);
                    else if (child.BrowseName == AlarmFilterDataLogic.toEventTimeDateTimeBrowseName)
                        filterData.SetToEventTime((DateTime)value);
                    else if(child.BrowseName == AlarmFilterDataLogic.fromSeverityBrowseName)
                        filterData.SetFromSeverity(value);
                    else if (child.BrowseName == AlarmFilterDataLogic.toSeverityBrowseName)
                        filterData.SetToSeverity(value);
                    else
                        alarmFilterData.Filters.Add(new AlarmFilterDataLogic.ToggleFilter(child.BrowseName, value, attribute));
                }
            }
            else
            {
                Log.Warning($"Accordion {attributeNode.BrowseName} browse name is not a valid FilterAttribute.");
            }
        }
    }

    private void PrepareQuery()
    {
        AlarmFilterQueryBuilderLogic queryBuilder = new()
        {
            Query = Owner.Get("Layout/AlarmsDataGrid").GetVariable("Query")
        };
        queryBuilder.BuildQuery(alarmFilterData, filterConfiguration);
        queryBuilder.RefreshQuery();
    }

    private IUANode AlarmWidgetConfiguration
    {
        get
        {
            var pointedNodeId = Owner.GetVariable("ConfigurationPointer").Value;
            var alarmWidgetConfiguration = InformationModel.Get(pointedNodeId);
            return alarmWidgetConfiguration ?? throw new CoreConfigurationException("AlarmWidgetConfiguration not found");
        }
    }

    private IUAVariable alarmsDataGridModel;
    private IUAVariable actualLanguageVariable;
    private IUAObject filterConfiguration;
    private AlarmFilterDataLogic.ToggleFilterData filterData { get => (AlarmFilterDataLogic.ToggleFilterData)alarmFilterData.Data; }
    private readonly AlarmFilterDataLogic alarmFilterData = new()
    {
        Data = new AlarmFilterDataLogic.ToggleFilterData()
    };
    private readonly string filtersConfigurationBrowseName = "FiltersConfiguration";
}
