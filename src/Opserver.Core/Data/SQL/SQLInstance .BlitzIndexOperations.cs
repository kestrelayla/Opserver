using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using Dapper;
using Opserver.Data.SQL.QueryPlans;

namespace Opserver.Data.SQL
{
    public partial class SQLInstance
    {
        public LightweightCache<List<BlitzIndexOperation>> GetBlitzIndexOperations(BlitzIndexSearchOptions options = null)
        {
            return TimedCache(nameof(GetBlitzIndexOperations) + "-" + (options?.GetHashCode() ?? 0).ToString(),
                 conn =>
                {
                    var hasOptions = options != null;
                    var sql = string.Format(GetFetchSQL<BlitzIndexOperation>());
                    var resultSet = new List<BlitzIndexOperation>();
                    var items = new List<BlitzIndexOperation>();

                    using (var reader = conn.ExecuteReader(sql, options))
                    {
                        while (reader.Read())
                        {
                            items.Add(new BlitzIndexOperation()
                            {

                                Priority = (int)reader.GetValue(0),
                                Finding = reader.GetValue(1).ToString(),
                                DatabaseName = reader.GetValue(2).ToString(),
                                Details = reader.GetValue(3).ToString(),
                                Definition = reader.GetValue(4).ToString(),
                                SecretColumns = reader.GetValue(5).ToString(),
                                Usage = reader.GetValue(6).ToString(),
                                Size = reader.GetValue(7).ToString(),
                                MoreInfo = reader.GetValue(8).ToString(),
                                URL = reader.GetValue(9).ToString(),
                                CreateTSQL = reader.GetValue(10).ToString()
                            });
                        }
                    }
                    return items;
                }, 10.Seconds(), 5.Minutes());
        }

        public LightweightCache<BlitzIndexOperation> GetBlitzIndexOperation(byte[] planHandle, int? statementStartOffset = null)
        {
            var clause = " And (qs.plan_handle = @planHandle OR qs.sql_handle = @planHandle)";
            if (statementStartOffset.HasValue) clause += " And qs.statement_start_offset = @statementStartOffset";
            var sql = string.Format(GetFetchSQL<BlitzIndexOperation>(), clause, "");
            return TimedCache(nameof(GetBlitzIndexOperation) + "-" + planHandle.GetHashCode().ToString() + "-" + statementStartOffset.ToString(),
                conn => conn.QueryFirstOrDefault<BlitzIndexOperation>(sql, new { planHandle, statementStartOffset, MaxResultCount = 1 }),
                60.Seconds(), 60.Seconds());
        }

        public class BlitzIndexOperation : ISQLVersioned
        {
            Version IMinVersioned.MinVersion => SQLServerVersions.SQL2005.RTM;
            SQLServerEditions ISQLVersioned.SupportedEditions => SQLServerEditions.All;

            public int Priority { get; internal set; }
            public string Finding { get; internal set; }
            public string DatabaseName { get; internal set; }
            public string QueryPlan { get; internal set; }
            public string Details { get; internal set; }
            public string Definition { get; internal set; }
            public string SecretColumns { get; internal set; }
            public string Usage { get; internal set; }
            public string Size { get; internal set; }
            public string MoreInfo { get; internal set; }
            public string URL { get; internal set; }
            public string CreateTSQL { get; internal set; }
            public byte[] PlanHandle { get; internal set; }
       
            public string ReadablePlanHandle => string.Concat(PlanHandle.Select(x => x.ToString("X2")));

            public ShowPlanXML GetShowPlanXML()
            {
                if (QueryPlan == null) return new ShowPlanXML();
                var s = new XmlSerializer(typeof(ShowPlanXML));
                using var r = new StringReader(QueryPlan);
                return (ShowPlanXML)s.Deserialize(r);
            }

            internal const string FetchSQL = @"EXEC master..sp_BlitzIndex";
            public string GetFetchSQL(in SQLServerEngine e)
            {
                return FetchSQL;
            }
        }

        public enum BlitzIndexSorts
        {
            [Description("Priority")] Priority = 0,
            [Description("Finding")] Finding = 1,
            [Description("Database name")] DatabaseName = 2,
            [Description("Details: schema.table.index(indexid)")] Details = 3,
            [Description("Definition: [Property] ColumnName {datatype maxbytes}")] Definition = 4,
            [Description("Secret Columns")] SecretColumns = 5,
            [Description("Usage")] Usage = 6,
            [Description("Size")] Size = 7,
            [Description("More Info")] MoreInfo = 8,
            [Description("URL")] URL = 9,
            [Description("CreateTSQL")] CreateTSQL = 10
        }

        public class BlitzIndexSearchOptions
        {
            public BlitzIndexSorts? Sort { get; set; }
            public int? MinExecs { get; set; }
            public int? MinExecsPerMin { get; set; }
            public DateTime? MinLastRunDate { get; set; }
            private int? _lastRunSeconds;

            public int? LastRunSeconds
            {
                get { return _lastRunSeconds; }
                set
                {
                    if (!value.HasValue) return;
                    _lastRunSeconds = value;
                    MinLastRunDate = DateTime.UtcNow.AddSeconds(-1 * value.Value);
                }
            }

            public string Search { get; set; }
            public int? MaxResultCount { get; set; }
            public int? Database { get; set; }

            public static readonly BlitzIndexSearchOptions Default = new BlitzIndexSearchOptions().SetDefaults();

            private const int DefaultMinExecs = 25;
            private const int DefaultLastRunSeconds = 24*60*60;
            private const int DefaultMaxResultCount = 100;

            public BlitzIndexSearchOptions SetDefaults()
            {
                return this;
            }

            public bool IsNonDefault
            {
                get
                {
                    if (MinExecs != DefaultMinExecs) return true;
                    if (LastRunSeconds != DefaultLastRunSeconds) return true;
                    if (MaxResultCount != DefaultMaxResultCount) return true;
                    if (Database.HasValue) return true;
                    if (Search.HasValue()) return true;
                    return false;
                }
            }

            public string ToSQLWhere()
            {
                var clauses = new List<string>();
                if (MinExecs.GetValueOrDefault(0) > 0) clauses.Add("execution_count >= @MinExecs");
                if (MinExecsPerMin.GetValueOrDefault(0) > 0) clauses.Add("(Case When DATEDIFF(mi, creation_time, qs.last_execution_time) > 0 Then CAST((1.00 * execution_count / DATEDIFF(mi, creation_time, qs.last_execution_time)) AS money) Else Null End) >= @MinExecsPerMin");
                if (MinLastRunDate.HasValue) clauses.Add("qs.last_execution_time >= @MinLastRunDate");
                if (Database.HasValue) clauses.Add("Cast(pa.value as Int) = @Database");

                return clauses.Count > 0 ? "\n       And " + string.Join("\n       And ", clauses) : "";
            }

            public string ToSQLSearch()
            {
                return Search.HasValue() ? @"Where SUBSTRING(st.text,
                 (StatementStartOffset / 2) + 1,
                 ((CASE StatementEndOffset
                   WHEN -1 THEN DATALENGTH(st.text)
                   ELSE StatementEndOffset
                   END - StatementStartOffset) / 2) + 1) Like '%' + @Search + '%'" : "";
            }

            public string ToSQLOrder()
            {
                return Sort.HasValue ? $"\nORDER BY {Sort} DESC" : "";
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hashCode = _lastRunSeconds.GetHashCode();
                    hashCode = (hashCode*397) ^ Sort.GetHashCode();
                    hashCode = (hashCode*397) ^ MinExecs.GetHashCode();
                    hashCode = (hashCode*397) ^ MinExecsPerMin.GetHashCode();
                    hashCode = (hashCode*397) ^ (Search?.GetHashCode() ?? 0);
                    hashCode = (hashCode*397) ^ MaxResultCount.GetHashCode();
                    hashCode = (hashCode*397) ^ Database.GetHashCode();
                    return hashCode;
                }
            }
        }
    }
}
