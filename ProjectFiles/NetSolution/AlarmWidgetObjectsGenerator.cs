#region Using directives
using FTOptix.HMIProject;
using FTOptix.NetLogic;
using FTOptix.UI;
using System.Linq;
using UAManagedCore;
using FTOptix.SerialPort;
using FTOptix.EventLogger;
using static AlarmFilterDataLogic;
#endregion

public class AlarmWidgetObjectsGenerator : BaseNetLogic
{
    public static void GenerateAccordions(IUANode baseLayout, IUANode filterConfiguration, CheckBoxFilterData filterData)
    {
        foreach (var child in filterConfiguration.Children)
        {
            //Accordions should not be generated if they are not visible
            if (!IsAttributeEnabled(child.BrowseName, filterConfiguration))
                continue;

            var accordion = GenerateAccordion(child, filterConfiguration, filterData);
            baseLayout.Add(accordion);
        }
    }

    private static Accordion GenerateAccordion(IUANode filterConfigurationNode, IUANode filterConfiguration, CheckBoxFilterData filterData)
    {
        var accordion = InformationModel.Make<Accordion>(filterConfigurationNode.BrowseName);

        accordion.BrowseName = filterConfigurationNode.BrowseName;
        accordion.HorizontalAlignment = HorizontalAlignment.Stretch;
        accordion.VerticalAlignment = VerticalAlignment.Center;
        accordion.RightMargin = Margin;
        accordion.Expanded = false;

        //Header
        var label = InformationModel.Make<Label>("Label");
        label.Text = TranslateFilterName(filterConfigurationNode.BrowseName);
        label.LeftMargin = Margin;
        accordion.Header.Add(label);

        //Content
        if (filterConfigurationNode.BrowseName == eventTimeBrowseName)
            accordion.Content.Add(GenerateLayoutForEventTime(filterConfigurationNode, filterConfiguration, filterData));
        else if (filterConfigurationNode.BrowseName == severityBrowseName)
            accordion.Content.Add(GenerateLayoutForSeverity(filterConfigurationNode, filterConfiguration, filterData));
        else
            accordion.Content.Add(GenerateColumnLayout(filterConfigurationNode, filterConfiguration));

        return accordion;
    }

    private static ColumnLayout GenerateColumnLayout(IUANode node, IUANode filterConfiguration)
    {
        var columnLayout = InformationModel.Make<ColumnLayout>(node.BrowseName);

        foreach (var childBrowseName in node.Children.Select(child => child.BrowseName))
        {
            if (!IsCheckboxEnabled(childBrowseName, node.BrowseName, filterConfiguration))
                continue;
            var checkbox = GenerateCheckbox(childBrowseName);
            columnLayout.Children.Add(checkbox);
        }

        columnLayout.LeftMargin = Margin;
        columnLayout.TopMargin = Margin;
        columnLayout.RightMargin = Margin;

        return columnLayout;
    }

    private static ColumnLayout GenerateLayoutForEventTime(IUANode node, IUANode filterConfiguration, CheckBoxFilterData filterData)
    {
        var columnLayout = InformationModel.Make<ColumnLayout>(node.BrowseName);
        columnLayout.LeftMargin = Margin;
        columnLayout.TopMargin = Margin;
        columnLayout.RightMargin = Margin;
        columnLayout.BottomMargin = Margin;
        columnLayout.VerticalGap = VerticalGap;
        columnLayout.HorizontalAlignment = HorizontalAlignment.Stretch;
        columnLayout.VerticalAlignment = VerticalAlignment.Center;

        foreach (var childBrowseName in node.Children.Select(child => child.BrowseName))
        {
            if (!IsCheckboxEnabled(childBrowseName, node.BrowseName, filterConfiguration))
                continue;

            var rowLayout = InformationModel.Make<RowLayout>("RowLayout");
            rowLayout.HorizontalAlignment = HorizontalAlignment.Stretch;

            var checkBox = InformationModel.Make<CheckBox>(childBrowseName);
            checkBox.VerticalAlignment = VerticalAlignment.Center;
            checkBox.Width = CheckBoxWidth;
            rowLayout.Add(checkBox);

            var columnLayout2 = InformationModel.Make<ColumnLayout>("ColumnLayout");
            columnLayout2.HorizontalAlignment = HorizontalAlignment.Stretch;
            columnLayout2.VerticalGap = SmallVerticalGap;
            rowLayout.Add(columnLayout2);

            var label = InformationModel.Make<Label>("Label");
            label.Text = TranslateFilterName(childBrowseName);
            label.Elide = Elide.Right;
            columnLayout2.Add(label);

            var dateTimePicker = InformationModel.Make<DateTimePicker>(childBrowseName);
            dateTimePicker.HorizontalAlignment = HorizontalAlignment.Stretch;

            filterData.EventTimePickers.Add(childBrowseName, dateTimePicker);

            columnLayout2.Add(dateTimePicker);

            columnLayout.Children.Add(rowLayout);
        }

        return columnLayout;
    }

    private static RowLayout GenerateLayoutForSeverity(IUANode node, IUANode filterConfiguration, CheckBoxFilterData filterData)
    {
        var child = node.Get(severityBrowseName);

        var rowLayout = InformationModel.Make<RowLayout>(child.BrowseName);
        rowLayout.LeftMargin = Margin;
        rowLayout.TopMargin = Margin;
        rowLayout.RightMargin = Margin;
        rowLayout.BottomMargin = Margin;
        rowLayout.HorizontalGap = HorizontalGap;
        rowLayout.HorizontalAlignment = HorizontalAlignment.Stretch;
        rowLayout.VerticalAlignment = VerticalAlignment.Center;

        if (!IsCheckboxEnabled(child.BrowseName, node.BrowseName, filterConfiguration))
            return rowLayout;

        var checkBox = InformationModel.Make<CheckBox>(child.BrowseName);
        checkBox.VerticalAlignment = VerticalAlignment.Center;
        checkBox.Width = CheckBoxWidth;
        rowLayout.Add(checkBox);

        rowLayout.Add(GenerateColumnLayoutForSeverity(fromSeverityBrowseName, filterData));
        rowLayout.Add(GenerateColumnLayoutForSeverity(toSeverityBrowseName, filterData ));

        return rowLayout;
    }

    private static ColumnLayout GenerateColumnLayoutForSeverity(string name, CheckBoxFilterData filterData)
    {
        var columnLayout = InformationModel.Make<ColumnLayout>(name);
        columnLayout.HorizontalAlignment = HorizontalAlignment.Stretch;
        columnLayout.VerticalGap = SmallVerticalGap;

        var label = InformationModel.Make<Label>("Label");
        label.Text = TranslateFilterName(name);
        label.Elide = Elide.Right;
        columnLayout.Add(label);

        var textBox = InformationModel.Make<TextBox>(name);
        textBox.HorizontalAlignment = HorizontalAlignment.Stretch;
        columnLayout.Add(textBox);
        filterData.TextBoxes.Add(name, textBox);

        return columnLayout;
    }

    private static CheckBox GenerateCheckbox(string browseName)
    {
        var checkBox = InformationModel.Make<CheckBox>(browseName);
        checkBox.Text = TranslateFilterName(browseName);
        checkBox.BottomMargin = Margin;

        return checkBox;
    }

    private static bool IsAttributeEnabled(string browseName, IUANode filterConfiguration)
    {
        var config = filterConfiguration.GetVariable(browseName);
        if (config != null)
            return config.Value;
        else
        {
            Log.Warning($"FilterConfiguration not contains configuration for accordion: {browseName}.");
            return false;
        }
    }

    private static bool IsCheckboxEnabled(string browseName, string attribute, IUANode filterConfiguration)
    {
        var config = filterConfiguration.Get(attribute).GetVariable(browseName);
        if (config != null)
            return config.Value;
        else
        {
            Log.Warning($"FilterConfiguration not contains configuration for checkbox: {browseName} for attribute {attribute}.");
            return false;
        }
    }

    private static string TranslateFilterName(string textId)
    {
        var translation = InformationModel.LookupTranslation(new LocalizedText(textId));
        if (!translation.IsEmpty())
        {
            return translation.Text;
        }
        return textId;
    }

    private const int Margin = 8;
    private const int VerticalGap = 8;
    private const int SmallVerticalGap = 4;
    private const int HorizontalGap = 8;
    private const int CheckBoxWidth = 32;
}
