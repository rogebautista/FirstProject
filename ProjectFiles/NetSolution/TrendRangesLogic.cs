#region Using directives

using FTOptix.Core;
using FTOptix.CoreBase;
using FTOptix.DataLogger;
using FTOptix.HMIProject;
using FTOptix.NetLogic;
using FTOptix.Store;
using FTOptix.UI;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UAManagedCore;
using FTOptix.SQLiteStore;
using FTOptix.WebUI;
using System.Collections;
using FTOptix.Report;
using FTOptix.CommunicationDriver;
using FTOptix.Alarm;
using FTOptix.SerialPort;
using FTOptix.EventLogger;

#endregion

public class TrendRangesLogic : BaseNetLogic
{
    public override void Start()
    {
        // Insert code to be executed when the user-defined logic is started
        var trend = Owner.Owner.Owner.Owner.Owner.Get<Trend>("TrendPanel/MainTrend");
        if (trend == null )
        {
            Log.Error("TrendRangesLogic", "Cannot get to the main trend, if the widget was tampered, make sure to restore a working path");
            return;
        }
        var pens = trend.Get("Pens");
        if (pens.Children.Count == 0)
        {
            Log.Debug("TrendRangesLogic", "No pens to render, skipping...");
            return;
        }
        var logger = InformationModel.Get<DataLogger>(Owner.Owner.Owner.Owner.Owner.GetVariable("Logger").Value);
        var store = InformationModel.Get<Store>(logger.Store);
        var rangesNode = trend.Get("TimeRanges");
        bool localTime = trend.ReferenceTimeZone == ReferenceTimeZone.Local;
        referencesObserver = new ReferencesObserver(rangesNode, pens, LogicObject.Owner.Get<Item>("Scroll/Container"), store, logger, localTime);

        referencesEventRegistration = rangesNode.RegisterEventObserver(
            referencesObserver, EventType.ForwardReferenceAdded | EventType.ForwardReferenceRemoved);
    }

    public override void Stop()
    {
        // Insert code to be executed when the user-defined logic is stopped
        referencesEventRegistration?.Dispose();
        referencesObserver = null;
    }

    private sealed class ReferencesObserver : IReferenceObserver
    {
        public ReferencesObserver(IUANode rangesNode, IUANode pens, Item uiContainer, Store store, DataLogger logger, bool localTime)
        {
            this.uiContainer = uiContainer;
            this.store = store;
            this.pens = pens;
            this.logger = logger;
            this.localTime = localTime;
            rangesNode.Children.ToList().ForEach(CreateRangeUI);
        }

        public void OnReferenceAdded(IUANode sourceNode, IUANode targetNode, NodeId referenceTypeId, ulong senderId)
        {
            CreateRangeUI(targetNode);
        }

        public void OnReferenceRemoved(IUANode sourceNode, IUANode targetNode, NodeId referenceTypeId, ulong senderId)
        {
            var uiRange = uiContainer.Get(targetNode.NodeId.Id.ToString());
            uiRange?.Delete();
        }

        private void CreateRangeUI(IUANode rangeNode)
        {
            Log.Debug("TrendRangesLogic", "Adding " + rangeNode.BrowseName);
            // Extract data from all the ranges
            TimeRange range = (TimeRange)(rangeNode as IUAVariable).Value.Value;
            var trendTimeRange = InformationModel.MakeObject<TrendTimeRange>("TimeRange");
            trendTimeRange.StartTime = range.StartTime;
            trendTimeRange.EndTime = range.EndTime;
            var timeSpan = range.EndTime - range.StartTime;
            trendTimeRange.TimeSpan = timeSpan.TotalMilliseconds;
            var pensAndColumns = GetPenAndColumns();
            // Get name from the DataBase table, either if defined by the user (if defined) or the logger name
            var tableName = string.IsNullOrEmpty(logger.TableName) ? logger.BrowseName : logger.TableName;
            // Populate range data for each pen
            pensAndColumns.ForEach(p =>
            {
                var rangeStatistics = InformationModel.MakeObject<RangeStatistics>(p.column);
                var stats = GetFromStore(p.column, range.StartTime, range.EndTime, tableName, localTime);
                if (stats == null) 
                    return;
                rangeStatistics.Avg = stats.Value.Avg;
                rangeStatistics.Min = stats.Value.Min;
                rangeStatistics.Max = stats.Value.Max;
                rangeStatistics.Pen = p.pen.NodeId;
                trendTimeRange.Get("Statistics").Add(rangeStatistics);
            });

            var rangeUI = InformationModel.MakeObject<AdvancedTrendRangeUI>(rangeNode.NodeId.Id.ToString());
            rangeUI.Add(trendTimeRange);
            rangeUI.GetVariable("Range").Value = trendTimeRange.NodeId;
            uiContainer.Add(rangeUI);
        }

        private struct Statistics
        {
            public double Avg;
            public double Min;
            public double Max;
        }

        private struct PenColumn
        {
            public TrendPen pen;
            public string column;
        }

        private List<PenColumn> GetPenAndColumns()
        {
            var penColumns = new List<PenColumn>();
            pens.Children.OfType<TrendPen>().ToList().ForEach((pen) =>
            {
                pen.Children.OfType<DynamicLink>().ToList().ForEach(dynamicLink =>
                {
                    var pointedVar = dynamicLink.Refs.GetVariable(FTOptix.Core.ReferenceTypes.Resolves);
                    if (pointedVar?.BrowseName == "LastValue")
                    {
                        PenColumn penColumn = new()
                        {
                            pen = pen,
                            column = pointedVar.Owner.BrowseName
                        };
                        Console.WriteLine(pointedVar.Owner.BrowseName);
                        penColumns.Add(penColumn);
                    }
                });
            });
            return penColumns;
        }

        private Statistics? GetFromStore(string column, DateTime start, DateTime end, string tableName, bool isLocalTime = true)
        {
            // We get the column name from the current setting of the DataLogger
            // but if the project was made with a very old version of FT Optix
            // a fallback is used (no LocalTimestamp was logged previously) 
            string localTimestampColumnName = "LocalTimestamp";
            string timestampColumnName = "Timestamp";
            string timeStampColumn = isLocalTime ? localTimestampColumnName : timestampColumnName;
            string[] header;
            object[,] output;

            try
            {
                string query = $"SELECT AVG(\"{column}\"), MAX(\"{column}\"), MIN(\"{column}\") FROM \"{tableName}\" WHERE \"{timeStampColumn}\" BETWEEN \"{start.ToString("o", CultureInfo.InvariantCulture)}\" AND \"{end.ToString("o", CultureInfo.InvariantCulture)}\"";
                store.Query(query, out header, out output);
            }
            catch
            {
                try
                {
                    if (timeStampColumn == localTimestampColumnName)
                    {
                        timeStampColumn = timestampColumnName;
                    }
                    else
                    {
                        timeStampColumn = localTimestampColumnName;
                    }
                    string query = $"SELECT AVG(\"{column}\"), MAX(\"{column}\"), MIN(\"{column}\") FROM \"{tableName}\" WHERE \"{timeStampColumn}\" BETWEEN \"{start.ToString("o", CultureInfo.InvariantCulture)}\" AND \"{end.ToString("o", CultureInfo.InvariantCulture)}\"";
                    store.Query(query, out header, out output);
                }
                catch {
                    Log.Error("TrendRangesLogic.GetFromStore", "Cannot determine Timestamp/LocalTimestamp column from store");
                    return null;
                }
            }

            if (output.Length == 3 &&
                output[0, 0] != null &&
                output[0, 1] != null &&
                output[0, 2] != null)
            {
                Statistics statistics = new()
                {
                    Avg = Convert.ToDouble(output[0, 0], CultureInfo.InvariantCulture),
                    Max = Convert.ToDouble(output[0, 1], CultureInfo.InvariantCulture),
                    Min = Convert.ToDouble(output[0, 2], CultureInfo.InvariantCulture)
                };
                return statistics;
            }
            return null;
        }

        private readonly Item uiContainer;
        private readonly Store store;
        private readonly IUANode pens;
        private readonly DataLogger logger;
        private readonly bool localTime;
    }

    private ReferencesObserver referencesObserver;
    private IEventRegistration referencesEventRegistration;
}
