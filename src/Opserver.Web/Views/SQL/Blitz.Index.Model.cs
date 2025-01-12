using System.Collections.Generic;
using Microsoft.AspNetCore.Html;
using Opserver.Data.SQL;
using static Opserver.Data.SQL.SQLInstance;
using static Opserver.Data.SQL.SQLInstance.BlitzIndexSearchOptions;

namespace Opserver.Views.SQL
{
    public class BlitzIndexModel : DashboardModel
    {
        public SQLInstance.BlitzIndexSearchOptions BlitzIndexSearchOptions { get; set; }
        public List<SQLInstance.BlitzIndexOperation> BlitzIndexOperations { get; set; }

        private HtmlString _BlitzIndexOptionsQueryString;
        public HtmlString BlitzIndexOptionsQueryString => _BlitzIndexOptionsQueryString ??= GetQueryString(BlitzIndexSearchOptions);

        public static HtmlString GetQueryString(SQLInstance.BlitzIndexSearchOptions options)
        {
            var sb = StringBuilderCache.Get();

            return sb.ToStringRecycle().AsHtml();
        }
    }
}
