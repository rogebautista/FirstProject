#region Using directives
using FTOptix.HMIProject;
using FTOptix.NetLogic;
using System;
using UAManagedCore;
using FTOptix.SerialPort;
using FTOptix.EventLogger;
using OpcUa = UAManagedCore.OpcUa;
using static AlarmFilterDataLogic;
#endregion

public class AlarmFilterEditModelLogic : BaseNetLogic
{
    public static void CreateEditModel(IUANode parentNode, IUANode filtersConfiguration, string editModelBrowseName = DefaultEditModelBrowseName)
    {
        FilterEditModel.Create(parentNode, filtersConfiguration, editModelBrowseName);
    }

    public static IUAObject GetEditModel(IUANode parentNode, string editModelBrowseName = DefaultEditModelBrowseName)
    {
        var filterEditModel = parentNode.GetObject(editModelBrowseName);
        return filterEditModel ?? throw new CoreConfigurationException($"Edit model {editModelBrowseName} filters not found");
    }

    public static void DeleteEditModel(IUANode parentNode, string editModelBrowseName = DefaultEditModelBrowseName)
    {
        FilterEditModel.Delete(parentNode, editModelBrowseName);
    }

    private static class FilterEditModel
    {
        public static void Create(IUANode parentNode, IUANode filtersConfiguration, string editModelBrowseName)
        {
            
            var typeObject = CreateType(parentNode, editModelBrowseName);

            UpdateType(typeObject, filtersConfiguration);

            var editModelFilters = parentNode.Find(editModelBrowseName);
            if (editModelFilters == null)
            {
                editModelFilters = InformationModel.MakeObject(editModelBrowseName, typeObject.NodeId);
                parentNode.Add(editModelFilters);
            }

            UpdateInstance(editModelFilters, typeObject);
        }

        public static void Delete(IUANode parentNode, string editModelBrowseName)
        {
            var editModelFilters = parentNode.GetObject(editModelBrowseName);
            if (editModelFilters != null)
                parentNode.Remove(editModelFilters);

            DeleteType(parentNode, editModelBrowseName);
        }

        private static void DeleteType(IUANode parentNode, string editModelBrowseName)
        {
            var componentsId = parentNode.GetVariable(AlarmWidgetComponentsBrowseName).Value;
            var componentsFolder = InformationModel.Get(componentsId);
            var editModelFilters = componentsFolder.Get(editModelBrowseName + parentNode.BrowseName);

            if (editModelFilters != null)
                componentsFolder.Remove(editModelFilters);
        }

        private static IUANode CreateType(IUANode parentNode, string editModelBrowseName)
        {
            var componentsId = parentNode.GetVariable(AlarmWidgetComponentsBrowseName).Value;
            var componentsFolder = InformationModel.Get(componentsId);
            var editModelFilters = componentsFolder.Find(editModelBrowseName + parentNode.BrowseName);

            if (editModelFilters == null)
            {
                var newEditModelFiltersType = InformationModel.MakeObjectType(editModelBrowseName + parentNode.BrowseName);
                componentsFolder.Add(newEditModelFiltersType);
                return newEditModelFiltersType;
            }
            return editModelFilters;
        }

        private static void UpdateType(IUANode editModel, IUANode filtersConfiguration)
        {
            foreach (var attribute in filtersConfiguration.Children)
            {
                var setting = editModel.GetVariable(attribute.BrowseName);

                if (!IsVisible(attribute))
                {
                    if (setting != null)
                        editModel.Remove(setting);
                }
                else
                {
                    if (setting == null)
                    {
                        setting = InformationModel.MakeVariable(attribute.BrowseName, OpcUa.DataTypes.BaseDataType);
                        editModel.Add(setting);
                    }
                    UpdateAttribute(setting, attribute);
                }
            }
        }

        private static void UpdateAttribute(IUANode editModel, IUANode filtersConfigurationAttribute)
        {
            if (filtersConfigurationAttribute.BrowseName == eventTimeBrowseName)
            {
                UpdateEventTime(editModel, filtersConfigurationAttribute);
                return;
            }

            if (filtersConfigurationAttribute.BrowseName == severityBrowseName)
            {
                UpdateSeverity(editModel, filtersConfigurationAttribute);
                return;
            }

            foreach (var child in filtersConfigurationAttribute.Children)
                Update(editModel, child);
        }

        private static void UpdateEventTime(IUANode editModel, IUANode filtersConfigurationAttribute)
        {
            foreach (var child in filtersConfigurationAttribute.Children)
            {
                Update(editModel, child);
                var childVisible = IsVisible(child);
                if (child.BrowseName == fromEventTimeBrowseName)
                {
                    var dateTime = editModel.GetVariable(fromEventTimeDateTimeBrowseName);

                    if (!childVisible)
                    {
                        if (dateTime != null)
                            editModel.Remove(dateTime);
                    }
                    else if (dateTime == null)
                    {
                        dateTime = InformationModel.MakeVariable(fromEventTimeDateTimeBrowseName, OpcUa.DataTypes.DateTime);
                        dateTime.Value = DateTime.Now;
                        editModel.Add(dateTime);
                    }
                }

                if (child.BrowseName == toEventTimeBrowseName)
                {
                    var dateTime = editModel.GetVariable(toEventTimeDateTimeBrowseName);

                    if (!childVisible)
                    {
                        if (dateTime != null)
                            editModel.Remove(dateTime);
                    }
                    else if (dateTime == null)
                    {
                        dateTime = InformationModel.MakeVariable(toEventTimeDateTimeBrowseName, OpcUa.DataTypes.DateTime);
                        dateTime.Value = DateTime.Now;
                        editModel.Add(dateTime);
                    }
                }
            }
        }

        private static void UpdateSeverity(IUANode editModel, IUANode filtersConfigurationAttribute)
        {
            foreach (var child in filtersConfigurationAttribute.Children)
            {
                Update(editModel, child);

                var childVisible = IsVisible(child);
                if (child.BrowseName == severityBrowseName)
                {
                    var fromSeverity = editModel.GetVariable(fromSeverityBrowseName);
                    var toSeverity = editModel.GetVariable(toSeverityBrowseName);

                    if (!childVisible)
                    {
                        if (fromSeverity != null)
                            editModel.Remove(fromSeverity);
                        if (toSeverity != null)
                            editModel.Remove(toSeverity);
                    }
                    else
                    {
                        if (fromSeverity == null)
                        {
                            fromSeverity = InformationModel.MakeVariable(fromSeverityBrowseName, OpcUa.DataTypes.UInt16);
                            fromSeverity.Value = 1;
                            editModel.Add(fromSeverity);
                        }
                        if (toSeverity == null)
                        {
                            toSeverity = InformationModel.MakeVariable(toSeverityBrowseName, OpcUa.DataTypes.UInt16);
                            toSeverity.Value = 1000;
                            editModel.Add(toSeverity);
                        }
                    }
                }
            }
        }

        private static void Update(IUANode editModel, IUANode filtersConfigurationChild)
        {
            var setting = editModel.GetVariable(filtersConfigurationChild.BrowseName);

            if (!IsVisible(filtersConfigurationChild))
            {
                if (setting != null)
                    editModel.Remove(setting);
            }
            else if (setting == null)
            {
                setting = InformationModel.MakeVariable(filtersConfigurationChild.BrowseName, OpcUa.DataTypes.Boolean);
                editModel.Add(setting);
            }
        }

        private static bool IsVisible(IUANode node)
        {
            return InformationModel.GetVariable(node.NodeId).Value;
        }

        private static void UpdateInstance(IUANode instance, IUANode type)
        {
            RemoveUnnecessaryInstanceProperties(instance, type);
            AddMissingInstanceProperties(instance, type);
        }

        private static void RemoveUnnecessaryInstanceProperties(IUANode instance, IUANode type)
        {
            foreach (var instanceChild in instance.Children)
            {
                var typeChild = type.Get(instanceChild.BrowseName);

                if (typeChild == null)
                    instance.Remove(instanceChild);
                else
                    RemoveUnnecessaryInstanceProperties(instanceChild, typeChild);
            }
        }

        private static void AddMissingInstanceProperties(IUANode instance, IUANode type)
        {
            foreach (var typeChild in type.Children)
            {
                var typeVariable = (IUAVariable)typeChild;
                if (typeVariable == null)
                    continue;

                var instanceChild = instance.Get(typeVariable.BrowseName);
                if (instanceChild == null)
                {
                    var instanceVariable = InformationModel.MakeVariable(typeVariable.BrowseName, typeVariable.DataType, typeVariable.VariableType.NodeId, typeVariable.ArrayDimensions);
                    instanceVariable.Value = typeVariable.Value;
                    instanceVariable.Prototype = typeVariable;
                    instance.Add(instanceVariable);
                }
                else
                    AddMissingInstanceProperties(instanceChild, typeChild);
            }
        }
    }

    private const string DefaultEditModelBrowseName = "DefaultFiltersToggle";
    private const string AlarmWidgetComponentsBrowseName = "AlarmWidgetComponents";
}
