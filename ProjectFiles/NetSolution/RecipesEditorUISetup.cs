#region Using directives
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FTOptix.CommunicationDriver;
using FTOptix.CoreBase;
using FTOptix.HMIProject;
using FTOptix.NetLogic;
using FTOptix.Recipe;
using FTOptix.UI;
using UAManagedCore;
using OpcUa = UAManagedCore.OpcUa;
#endregion

public class RecipesEditorUISetup : BaseNetLogic
{
    [ExportMethod]
    public void Setup()
    {
        try
        {
            // Check if we want to create a translation key for each element of the recipes editor
            var generateTranslationKeysVariable = Owner.GetVariable("GenerateTranslationKeys");
            generateTranslationKeys = generateTranslationKeysVariable != null && (bool)generateTranslationKeysVariable.Value;
            // The localization fallback locale is created to generate the text for the translation keys
            if (generateTranslationKeys)
            {
                try
                {
                    defaultLocale = ((string[])Project.Current.GetVariable("Localization/LocaleFallbackList").Value.Value)[0];
                }
                catch
                {
                    Log.Error("RecipesEditor", "Cannot get \"Translations fallback locales\" for the current project, disabling localizations");
                    generateTranslationKeys = false;
                }
            }

            schema = GetRecipeSchema();

            var rootNode = schema.Get("Root");
            if (rootNode == null)
                throw new Exception("Root node not found in recipe schema " + schema.BrowseName);

            var controlsContainer = GetControlsContainer();
            CleanUI(controlsContainer);

            ConfigureComboBox();

            var targetNode = GetTargetNode();

            BuildUIFromSchemaRecursive(rootNode, targetNode, controlsContainer, []);
        }
        catch (Exception e)
        {
            Log.Error("RecipesEditor", e.Message);
        }
    }

    private RecipeSchema GetRecipeSchema()
    {
        var recipeSchemaPtr = Owner.GetVariable("RecipeSchema");
        if (recipeSchemaPtr == null)
            throw new Exception("RecipeSchema variable not found");

        var nodeId = (NodeId)recipeSchemaPtr.Value;
        if (nodeId == null)
            throw new Exception("RecipeSchema not set");

        var recipeSchemaNode = InformationModel.Get(nodeId);
        if (recipeSchemaNode == null)
            throw new Exception("Recipe not found");

        // Check if it has correct type
        if (recipeSchemaNode is not RecipeSchema recipeSchema)
            throw new Exception(recipeSchemaNode.BrowseName + " is not a recipe");

        return recipeSchema;
    }

    private ColumnLayout GetControlsContainer()
    {
        var scrollView = Owner.Get("ScrollView");
        if (scrollView == null)
            throw new Exception("ScrollView not found");

        var controlsContainer = scrollView.Get<ColumnLayout>("ColumnLayout");
        if (controlsContainer == null)
            throw new Exception("ColumnLayout not found");

        return controlsContainer;
    }

    private void CleanUI(ColumnLayout controlsContainer)
    {
        controlsContainer.Children.Clear();
        controlsContainer.Height = 0;
        controlsContainer.HorizontalAlignment = HorizontalAlignment.Stretch;
    }

    private void ConfigureComboBox()
    {
        // Set store as model for ComboBox
        var recipesComboBox = Owner.Get<ComboBox>("RecipesComboBox");
        if (recipesComboBox == null)
            throw new Exception("Recipes ComboBox not found");

        if (schema.Store == null)
            throw new Exception("Store of schema " + schema.BrowseName + " is not set");

        recipesComboBox.Model = schema.Store;

        // Set query of combobox with correct table name
        string tableName = !string.IsNullOrEmpty(schema.TableName) ? schema.TableName : schema.BrowseName;
        recipesComboBox.Query = "SELECT Name FROM \"" + tableName + "\"";
    }

    private IUANode GetTargetNode()
    {
        var targetNode = schema.GetVariable("TargetNode");
        if (targetNode == null)
            throw new Exception("Target Node variable not found in schema " + schema.BrowseName);

        if ((NodeId)targetNode.Value == NodeId.Empty)
            throw new Exception("Target Node variable not set in schema " + schema.BrowseName);

        var target = InformationModel.Get(targetNode.Value);
        if (target == null)
            throw new Exception("Target " + targetNode.Value + " not found");

        return target;
    }

    private void BuildUIFromSchemaRecursive(IUANode editModelNode, IUANode targetNode, Item controlsContainer, List<string> browsePath)
    {
        foreach (var editModelChild in editModelNode.Children)
        {
            var targetChild = targetNode?.Get(editModelChild.BrowseName);
            var currentBrowsePath = browsePath.ToList();
            currentBrowsePath.Add(editModelChild.BrowseName);

            if (editModelChild.NodeClass == NodeClass.Variable &&
                ((targetChild != null && targetChild is not TagStructure) ||
                 (targetChild == null)))
            {
                var variable = (IUAVariable)editModelChild;
                var controls = BuildControl(variable, currentBrowsePath);
                foreach (var control in controls)
                {
                    controlsContainer.Height += control.Height;
                    controlsContainer.Add(control);
                }
            }

            if (editModelChild.Children.Count > 0)
                BuildUIFromSchemaRecursive(editModelChild, targetChild, controlsContainer, currentBrowsePath);
        }
    }

    private List<Item> BuildControl(IUAVariable variable, List<string> browsePath)
    {
        var result = new List<Item>();

        var dataType = variable.Context.GetDataType(variable.DataType);
        uint[] arrayDimensions = variable.ArrayDimensions;

        if (arrayDimensions.Length == 0)
        {
            if (dataType.IsSubTypeOf(OpcUa.DataTypes.Integer) || dataType.IsSubTypeOf(OpcUa.DataTypes.UInteger))
                result.Add(BuildSpinbox(browsePath));
            else if (dataType.IsSubTypeOf(OpcUa.DataTypes.Boolean))
                result.Add(BuildSwitch(browsePath));
            else if (dataType.IsSubTypeOf(OpcUa.DataTypes.Duration))
                result.Add(BuildDurationPicker(browsePath));
            else if (dataType.IsSubTypeOf(OpcUa.DataTypes.Float) || dataType.IsSubTypeOf(OpcUa.DataTypes.Double))
                result.Add(BuildSpinbox(browsePath, true));
            else
                result.Add(BuildTextBox(browsePath));
        }
        else if (arrayDimensions.Length == 1)
        {
            
            if (dataType.IsSubTypeOf(OpcUa.DataTypes.Integer) || dataType.IsSubTypeOf(OpcUa.DataTypes.UInteger))
            {
                foreach (var item in BuildSpinBoxArray(variable, browsePath))
                    result.Add(item);
            }
            else if (dataType.IsSubTypeOf(OpcUa.DataTypes.Boolean))
            {
                foreach (var item in BuildSwitchArray(variable, browsePath))
                    result.Add(item);
            }
            else if (dataType.IsSubTypeOf(OpcUa.DataTypes.Duration))
            {
                foreach (var item in BuildDurationPickerArray(variable, browsePath))
                    result.Add(item);
            }
            else if (dataType.IsSubTypeOf(OpcUa.DataTypes.Float) || dataType.IsSubTypeOf(OpcUa.DataTypes.Double))
            {
                foreach (var item in BuildSpinBoxArray(variable, browsePath, true))
                    result.Add(item);
            }
            else
            {
                foreach (var item in BuildTextBoxArray(variable, browsePath))
                    result.Add(item);
            }
        }
        else
            Log.Error("RecipesEditor", "Unsupported multi-dimensional array parameter " + Log.Node(variable));

        return result;
    }

    private Item BuildControlPanel(List<string> browsePath, uint[] indexes = null)
    {
        var panel = InformationModel.MakeObject<Panel>(BrowsePathToBrowseName(browsePath));
        panel.Height = 40;
        panel.HorizontalAlignment = HorizontalAlignment.Stretch;

        var label = InformationModel.MakeObject<Label>("Path");
        if (!generateTranslationKeys)
        {
            label.Text = BrowsePathToNodePath(browsePath);
            if (indexes != null)
                label.Text += "_" + indexes[0];
        }
        else
        {
            if (indexes != null)
            {
                var labelTextArray = new LocalizedText(BrowsePathToNodePath(browsePath) + "_" + indexes[0], BrowsePathToNodePath(browsePath) + "_" + indexes[0], defaultLocale);
                if (!InformationModel.LookupTranslation(labelTextArray).HasTranslation)
                {
                    InformationModel.AddTranslation(labelTextArray);
                }
                label.LocalizedText = labelTextArray;
            }
            else
            {
                var labelText = new LocalizedText(BrowsePathToNodePath(browsePath), BrowsePathToNodePath(browsePath), defaultLocale);
                if (!InformationModel.LookupTranslation(labelText).HasTranslation)
                {
                    InformationModel.AddTranslation(labelText);
                }
                label.LocalizedText = labelText;
            }
        }

        label.LeftMargin = 20;
        label.VerticalAlignment = VerticalAlignment.Center;
        panel.Add(label);

        var target = GetTargetNode();
        var node = target;
        foreach (string nodeBrowseName in browsePath)
        {
            if (node == null)
            {
                Log.Error("RecipesEditor", "Node " + BrowsePathToNodePath(browsePath) + " not found in target " + target.BrowseName);
                continue;
            }

            node = node.Get(nodeBrowseName);
        }

        var variableTarget = (IUAVariable)node;

        var label2 = InformationModel.MakeObject<Label>("CurrentValue");
        if (indexes == null)
            label2.TextVariable.SetDynamicLink(variableTarget);
        else
            label2.TextVariable.SetDynamicLink(variableTarget, indexes[0]);

        label2.VerticalAlignment = VerticalAlignment.Center;
        label2.HorizontalAlignment = HorizontalAlignment.Right;
        panel.Add(label2);

        return panel;
    }

    private Item BuildDurationPicker(List<string> browsePath)
    {
        var panel = BuildControlPanel(browsePath);

        var durationPicker = InformationModel.MakeObject<DurationPicker>("DurationPicker");
        durationPicker.VerticalAlignment = VerticalAlignment.Center;
        durationPicker.HorizontalAlignment = HorizontalAlignment.Right;
        durationPicker.RightMargin = 100;
        durationPicker.Width = 100;

        string aliasRelativeNodePath = MakeNodePathRelativeToAlias(browsePath);
        MakeDynamicLink(durationPicker.GetVariable("Value"), aliasRelativeNodePath);
        panel.Add(durationPicker);

        return panel;
    }

    private List<Item> BuildDurationPickerArray(IUAVariable variable, List<string> browsePath)
    {
        var result = new List<Item>();

        uint[] arrayDimensions = variable.ArrayDimensions;
        for (uint index = 0; index < arrayDimensions[0]; ++index)
        {
            var panel = BuildControlPanel(browsePath, new uint[] { index });

            var durationPicker = InformationModel.MakeObject<DurationPicker>("DurationPicker");
            durationPicker.VerticalAlignment = VerticalAlignment.Center;
            durationPicker.HorizontalAlignment = HorizontalAlignment.Right;
            durationPicker.RightMargin = 100;
            durationPicker.Width = 100;

            string aliasRelativeNodePath = MakeNodePathRelativeToAlias(browsePath);
            MakeDynamicLink(durationPicker.GetVariable("Value"), aliasRelativeNodePath, index);
            panel.Add(durationPicker);

            result.Add(panel);
        }

        return result;
    }

    private Item BuildSpinbox(List<string> browsePath, bool isFloat = false)
    {
        var panel = BuildControlPanel(browsePath);

        var spinbox = InformationModel.MakeObject<SpinBox>("SpinBox");
        spinbox.VerticalAlignment = VerticalAlignment.Center;
        spinbox.HorizontalAlignment = HorizontalAlignment.Right;
        spinbox.RightMargin = 100;
        spinbox.Width = 100;

        spinbox.Format = isFloat ? "n6" : "n0";

        string aliasRelativeNodePath = MakeNodePathRelativeToAlias(browsePath);
        MakeDynamicLink(spinbox.GetVariable("Value"), aliasRelativeNodePath);
        panel.Add(spinbox);

        return panel;
    }

    private List<Item> BuildSpinBoxArray(IUAVariable variable, List<string> browsePath, bool isFloat = false)
    {
        var result = new List<Item>();

        uint[] arrayDimensions = variable.ArrayDimensions;
        for (uint index = 0; index < arrayDimensions[0]; ++index)
        {
            var panel = BuildControlPanel(browsePath, new uint[] { index });

            var spinbox = InformationModel.MakeObject<SpinBox>("SpinBox");
            spinbox.VerticalAlignment = VerticalAlignment.Center;
            spinbox.HorizontalAlignment = HorizontalAlignment.Right;
            spinbox.RightMargin = 100;
            spinbox.Width = 100;

            spinbox.Format = isFloat ? "n6" : "n0";

            string aliasRelativeNodePath = MakeNodePathRelativeToAlias(browsePath);
            MakeDynamicLink(spinbox.GetVariable("Value"), aliasRelativeNodePath, index);
            panel.Add(spinbox);

            result.Add(panel);
        }

        return result;
    }

    private Item BuildTextBox(List<string> browsePath)
    {
        var panel = BuildControlPanel(browsePath);

        var textbox = InformationModel.MakeObject<TextBox>("Textbox");
        textbox.VerticalAlignment = VerticalAlignment.Center;
        textbox.HorizontalAlignment = HorizontalAlignment.Right;
        textbox.RightMargin = 100;
        textbox.Width = 100;

        string aliasRelativeNodePath = MakeNodePathRelativeToAlias(browsePath);
        MakeDynamicLink(textbox.GetVariable("Text"), aliasRelativeNodePath);
        panel.Add(textbox);

        return panel;
    }

    private List<Item> BuildTextBoxArray(IUAVariable variable, List<string> browsePath)
    {
        var result = new List<Item>();

        uint[] arrayDimensions = variable.ArrayDimensions;
        for (uint index = 0; index < arrayDimensions[0]; ++index)
        {
            var panel = BuildControlPanel(browsePath, new uint[] { index });

            var textbox = InformationModel.MakeObject<TextBox>("Textbox");
            textbox.VerticalAlignment = VerticalAlignment.Center;
            textbox.HorizontalAlignment = HorizontalAlignment.Right;
            textbox.RightMargin = 100;
            textbox.Width = 100;

            string aliasRelativeNodePath = MakeNodePathRelativeToAlias(browsePath);
            MakeDynamicLink(textbox.GetVariable("Text"), aliasRelativeNodePath, index);
            panel.Add(textbox);

            result.Add(panel);
        }

        return result;
    }

    private Item BuildSwitch(List<string> browsePath)
    {
        var panel = BuildControlPanel(browsePath);

        var switchControl = InformationModel.MakeObject<Switch>("Switch");
        switchControl.VerticalAlignment = VerticalAlignment.Center;
        switchControl.HorizontalAlignment = HorizontalAlignment.Right;
        switchControl.RightMargin = 100;
        switchControl.Width = 60;

        string aliasRelativeNodePath = MakeNodePathRelativeToAlias(browsePath);
        MakeDynamicLink(switchControl.GetVariable("Checked"), aliasRelativeNodePath);
        panel.Add(switchControl);

        return panel;
    }

    private List<Item> BuildSwitchArray(IUAVariable variable, List<string> browsePath)
    {
        var result = new List<Item>();

        uint[] arrayDimensions = variable.ArrayDimensions;
        for (uint index = 0; index < arrayDimensions[0]; ++index)
        {
            var panel = BuildControlPanel(browsePath, new uint[] { index });

            var switchControl = InformationModel.MakeObject<Switch>("Switch");
            switchControl.VerticalAlignment = VerticalAlignment.Center;
            switchControl.HorizontalAlignment = HorizontalAlignment.Right;
            switchControl.RightMargin = 100;
            switchControl.Width = 60;

            string aliasRelativeNodePath = MakeNodePathRelativeToAlias(browsePath);
            MakeDynamicLink(switchControl.GetVariable("Checked"), aliasRelativeNodePath, index);
            panel.Add(switchControl);

            result.Add(panel);
        }

        return result;
    }

    private void MakeDynamicLink(IUAVariable parent, string nodePath)
    {
        var dynamicLink = InformationModel.MakeVariable<DynamicLink>("DynamicLink", FTOptix.Core.DataTypes.NodePath);
        dynamicLink.Value = nodePath;
        dynamicLink.Mode = DynamicLinkMode.ReadWrite;
        parent.Refs.AddReference(FTOptix.CoreBase.ReferenceTypes.HasDynamicLink, dynamicLink);
    }

    private void MakeDynamicLink(IUAVariable parent, string nodePath, uint index)
    {
        MakeDynamicLink(parent, nodePath + "[" + index.ToString() + "]");
    }

    private string MakeNodePathRelativeToAlias(List<string> browsePath)
    {
        return "{" + NodePath.EscapeNodePathBrowseName(schema.BrowseName) + "}/" + BrowsePathToNodePath(browsePath);
    }

    private string BrowsePathToNodePath(List<string> browsePath)
    {
        if (browsePath.Count == 1)
            return NodePath.EscapeNodePathBrowseName(browsePath[0]);

        var result = new StringBuilder();

        for (int i = 0; i < browsePath.Count; ++i)
        {
            _ = result.Append(NodePath.EscapeNodePathBrowseName(browsePath[i]));
            if (i != browsePath.Count - 1)
                _ = result.Append('/');
        }

        return result.ToString();
    }

    private string BrowsePathToBrowseName(List<string> browsePath)
    {
        if (browsePath.Count == 1)
            return NodePath.EscapeNodePathBrowseName(browsePath[0]);

        var result = new StringBuilder();

        for (int i = 0; i < browsePath.Count; ++i)
        {
            _ = result.Append(NodePath.EscapeNodePathBrowseName(browsePath[i]));
            if (i != browsePath.Count - 1)
                _ = result.Append('_');
        }

        return result.ToString();
    }

    private RecipeSchema schema;
    private bool generateTranslationKeys;
    private string defaultLocale;
}
