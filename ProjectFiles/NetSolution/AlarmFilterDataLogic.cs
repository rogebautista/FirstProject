#region Using directives
using FTOptix.HMIProject;
using FTOptix.NetLogic;
using FTOptix.UI;
using System;
using System.Collections.Generic;
using UAManagedCore;
using FTOptix.SerialPort;
using FTOptix.EventLogger;
#endregion

public class AlarmFilterDataLogic : BaseNetLogic
{
    public enum FilterAttribute
    {
        AlarmState,
        Name,
        Class,
        EventTime,
        Group,
        Inhibit,
        Message,
        Priority,
        Severity,
        AlarmStatus
    }

    public abstract class Filter
    {
        public abstract bool IsChecked { get; set; }
        public string Name { get; }
        public FilterAttribute Attribute { get; }
        public string SqlCondition { get; set; }

        protected Filter(FilterAttribute attribute, string name)
        {
            Attribute = attribute;
            Name = name;

            SqlCondition = presetSqlConditions.GetValueOrDefault(name) ??
                   GenerateSqlCondition(attribute, name);
        }

        private static string GenerateSqlCondition(FilterAttribute attribute, string checkboxBrowseName)
        {
            if (attribute == FilterAttribute.Inhibit)
                return $"ShelvingState.CurrentState = '{TranslateFilterName(checkboxBrowseName)}'";
            if (attribute == FilterAttribute.Class)
                return $"RAAlarmData.AlarmClass LIKE '%{TranslateFilterName(checkboxBrowseName)}%'";
            if (attribute == FilterAttribute.Group)
                return $"RAAlarmData.AlarmGroup LIKE '%{TranslateFilterName(checkboxBrowseName)}%'";
            if (attribute == FilterAttribute.Name)
                return $"BrowseName LIKE '%{TranslateFilterName(checkboxBrowseName)}%'";
            if (attribute == FilterAttribute.AlarmState)
                return GenerateSqlConditionAlarmState(checkboxBrowseName);

            return $"{attribute} LIKE '%{TranslateFilterName(checkboxBrowseName)}%'";
        }

        private static string GenerateSqlConditionAlarmState(string checkboxBrowseName)
        {
            var highHigh = TranslateFilterName("HighHighState");
            var high = TranslateFilterName("HighState");
            var lowLow = TranslateFilterName("LowLowState");
            var low = TranslateFilterName("LowState");
            var active = TranslateFilterName("ActiveState");
            var inactive = TranslateFilterName("InactiveState");

            if (checkboxBrowseName == "HighHighState")
                return $"CurrentState IN ('{highHigh}','{highHigh} {high}')";
            if (checkboxBrowseName == "HighState")
                return $"CurrentState IN ('{high}','{highHigh} {high}')";
            if (checkboxBrowseName == "LowLowState")
                return $"CurrentState IN ('{lowLow}','{low} {lowLow}')";
            if (checkboxBrowseName == "LowState")
                return $"CurrentState IN ('{low}','{low} {lowLow}')";
            if (checkboxBrowseName == "ActiveStateDigital")
                return $"CurrentState IN ('{active}')";
            if (checkboxBrowseName == "InactiveState")
                return $"CurrentState IN ('{inactive}')";

            return "";
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

        private static readonly Dictionary<string, string> presetSqlConditions = new()
        {
            { "Urgent", "(Severity >= 751 AND Severity <= 1000)" },
            { "High", "(Severity >= 501 AND Severity <= 750)" },
            { "Medium", "(Severity >= 251 AND Severity <= 500)" },
            { "Low", "(Severity >= 1 AND Severity <= 250)" },
            { "NormalUnacked", "(ActiveState.Id = 0 AND AckedState.Id = 0)" },
            { "InAlarm", "ActiveState.Id = 1" },
            { "InAlarmAcked", "(ActiveState.Id = 1 AND AckedState.Id = 1)" },
            { "InAlarmUnacked", "(ActiveState.Id = 1 AND AckedState.Id = 0)" },
            { "InAlarmConfirmed", "(ActiveState.Id = 1 AND ConfirmedState.Id = 1)" },
            { "InAlarmUnconfirmed", "(ActiveState.Id = 1 AND ConfirmedState.Id = 0)" },
            { "Enabled", "EnabledState.Id = 1" },
            { "Disabled", "EnabledState.Id = 0" },
            { "Suppressed", "SuppressedState.Id = 1" },
            { "Unsuppressed", "SuppressedState.Id = 0" },
            { "Severity", ""},
            { "FromEventTime", ""},
            { "ToEventTime", ""}
        };
    }

    public class CheckBoxFilter : Filter
    {
        public override bool IsChecked { get => checkbox.Checked; set => checkbox.Checked = value; }
        public Accordion Accordion { get => accordion; }

        public CheckBoxFilter(CheckBox checkbox, FilterAttribute attribute, Accordion accordion) : base(attribute, checkbox.BrowseName)
        {
            this.checkbox = checkbox;
            this.accordion = accordion;
        }

        private readonly Accordion accordion;
        private readonly CheckBox checkbox;
    }

    public class ToggleFilter : Filter
    {
        public override bool IsChecked { get; set; }
        public ToggleFilter(string name, bool isChecked, FilterAttribute attribute) : base(attribute, name)
        {
            IsChecked = isChecked;
        }
    }

    public interface IFilterData
    {
        DateTime FromEventTime { get; }
        DateTime ToEventTime { get; }
        int FromSeverity { get; }
        int ToSeverity { get; }
    }

    public class CheckBoxFilterData : IFilterData
    {
        public DateTime FromEventTime => eventTimePickers.GetValueOrDefault(fromEventTimeBrowseName).Value;

        public DateTime ToEventTime
        {
            get { return eventTimePickers.GetValueOrDefault(toEventTimeBrowseName).Value; }
        }

        public int FromSeverity
        {
            get
            {
                if (Int32.TryParse(textBoxes.GetValueOrDefault(fromSeverityBrowseName).Text, out int result))
                    return result;
                else
                {
                    Log.Warning($"TextBox \"FromSeverity\" should contains integer value");
                    return 1;
                }
            }
        }

        public int ToSeverity
        {
            get
            {
                if (Int32.TryParse(textBoxes.GetValueOrDefault(toSeverityBrowseName).Text, out int result))
                    return result;
                else
                {
                    Log.Warning($"TextBox \"ToSeverity\" should contains integer value");
                    return 1000;
                }
            }
        }

        public Dictionary<string, DateTimePicker> EventTimePickers { get => eventTimePickers; }
        public Dictionary<string, TextBox> TextBoxes { get => textBoxes; }

        private readonly Dictionary<string, DateTimePicker> eventTimePickers = [];
        private readonly Dictionary<string, TextBox> textBoxes = [];
    }

    public class ToggleFilterData : IFilterData
    {
        public DateTime FromEventTime { get => fromEventTime; }
        public DateTime ToEventTime { get => toEventTime; }
        public int FromSeverity { get => fromSeverity; }
        public int ToSeverity { get => toSeverity; }

        public void SetFromEventTime(DateTime fromEventTime)
        {
            this.fromEventTime = fromEventTime;
        }
        public void SetToEventTime(DateTime toEventTime)
        {
            this.toEventTime = toEventTime;
        }
        public void SetFromSeverity(int fromSeverity)
        {
            this.fromSeverity = fromSeverity;
        }
        public void SetToSeverity(int toSeverity)
        {
            this.toSeverity = toSeverity;
        }
        private DateTime toEventTime, fromEventTime;
        private int fromSeverity, toSeverity;
    }

    public List<Filter> Filters { get; } = [];
    public IFilterData Data { get; set; }

    public static readonly string eventTimeBrowseName = "EventTime";
    public static readonly string fromEventTimeBrowseName = "FromEventTime";
    public static readonly string toEventTimeBrowseName = "ToEventTime";
    public static readonly string fromEventTimeDateTimeBrowseName = "FromEventTimeDateTime";
    public static readonly string toEventTimeDateTimeBrowseName = "ToEventTimeDateTime";
    public static readonly string dateTimeBrowseName = "DateTime";
    public static readonly string severityBrowseName = "Severity";
    public static readonly string fromSeverityBrowseName = "FromSeverity";
    public static readonly string toSeverityBrowseName = "ToSeverity";
}

