using System.Linq.Expressions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace ApiClassLibrary.Utils
{
    public static class QueryHelper<TModel> where TModel : class
    {
        public static IQueryable<TModel> ApplyFilters(IQueryable<TModel> query, Dictionary<string, string>? filters)
        {
            if (filters == null) return query;

            foreach (var filter in filters)
            {
                var property = typeof(TModel).GetProperties()
                    .FirstOrDefault(p => p.Name.Equals(filter.Key, StringComparison.OrdinalIgnoreCase));
                if (property == null) continue;

                var param = Expression.Parameter(typeof(TModel), "x");
                var member = Expression.PropertyOrField(param, property.Name);

                Expression? expression = null;

                if (filter.Value.StartsWith("[") && filter.Value.EndsWith("]"))
                {
                    var range = filter.Value.Trim('[', ']').Split(',');
                    if (range.Length == 2)
                    {
                        if (!string.IsNullOrWhiteSpace(range[0]))
                        {
                            var lower = Convert.ChangeType(range[0], property.PropertyType);
                            expression = Expression.GreaterThanOrEqual(member, Expression.Constant(lower));
                        }
                        if (!string.IsNullOrWhiteSpace(range[1]))
                        {
                            var upper = Convert.ChangeType(range[1], property.PropertyType);
                            var le = Expression.LessThanOrEqual(member, Expression.Constant(upper));
                            expression = expression != null ? Expression.AndAlso(expression, le) : le;
                        }
                    }
                }
                else if (filter.Value.Contains(','))
                {
                    var values = filter.Value.Split(',').Select(v => Convert.ChangeType(v, property.PropertyType)).ToList();
                    var equals = values.Select(v => (Expression)Expression.Equal(member, Expression.Constant(v)));
                    expression = equals.Aggregate(Expression.OrElse);
                }
                else
                {
                    var value = Convert.ChangeType(filter.Value, property.PropertyType);
                    expression = Expression.Equal(member, Expression.Constant(value));
                }

                if (expression != null)
                {
                    var lambda = Expression.Lambda<Func<TModel, bool>>(expression, param);
                    query = query.Where(lambda);
                }
            }

            return query;
        }

        public static IQueryable<TModel> ApplySorting(IQueryable<TModel> query, string? asc, string? desc)
        {
            IOrderedQueryable<TModel>? ordered = null;

            if (!string.IsNullOrWhiteSpace(asc))
            {
                foreach (var prop in asc.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    ordered = ApplyOrder(query, prop.Trim(), true, ordered != null, ordered);
                    if (ordered != null) query = ordered;
                }
            }

            if (!string.IsNullOrWhiteSpace(desc))
            {
                foreach (var prop in desc.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    ordered = ApplyOrder(query, prop.Trim(), false, ordered != null, ordered);
                    if (ordered != null) query = ordered;
                }
            }

            return query;
        }

        public static IOrderedQueryable<TModel>? ApplyOrder(IQueryable<TModel> source, string property, bool ascending, bool thenBy, IOrderedQueryable<TModel>? current = null)
        {
            var param = Expression.Parameter(typeof(TModel), "x");
            var prop = Expression.PropertyOrField(param, property);
            var lambda = Expression.Lambda(prop, param);

            string method = thenBy
                ? (ascending ? "ThenBy" : "ThenByDescending")
                : (ascending ? "OrderBy" : "OrderByDescending");

            var result = Expression.Call(
                typeof(Queryable),
                method,
                new Type[] { typeof(TModel), prop.Type },
                thenBy && current != null ? current.Expression : source.Expression,
                Expression.Quote(lambda));

            return (IOrderedQueryable<TModel>)source.Provider.CreateQuery<TModel>(result);
        }

        public static (int start, int end) ParseRange(string? range, int total)
        {
            int start = 0, end = Math.Min(49, total - 1);
            if (!string.IsNullOrWhiteSpace(range))
            {
                var parts = range.Split('-');
                if (parts.Length == 2 &&
                    int.TryParse(parts[0], out var s) &&
                    int.TryParse(parts[1], out var e) &&
                    s >= 0 && e >= s)
                {
                    start = s;
                    end = Math.Min(e, total - 1);
                }
            }
            return (start, end);
        }

        public static IEnumerable<object> ShapeFields(IEnumerable<TModel> data, string fields)
        {
            var selected = fields.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(f => f.Trim().ToLower()).ToList();

            return data.Select(item =>
            {
                var dict = new Dictionary<string, object?>();
                foreach (var prop in typeof(TModel).GetProperties())
                {
                    if (selected.Contains(prop.Name.ToLower()))
                        dict[prop.Name.ToLower()] = prop.GetValue(item);
                }
                return dict;
            });
        }

        public static void AddPaginationHeaders(HttpResponse response, int start, int count, int total)
        {
            response.Headers["Content-Range"] = $"{start}-{start + count - 1}/{total}";
            response.Headers["Accept-Ranges"] = typeof(TModel).Name.ToLower();
        }

        public static void AddPaginationLinks(HttpResponse response, HttpRequest request, int start, int end, int total)
        {
            var baseUrl = $"{request.Scheme}://{request.Host}{request.Path}";
            var size = end - start + 1;

            var links = new List<string>
            {
                $"<{baseUrl}?range=0-{Math.Min(size - 1, total - 1)}>; rel=\"first\""
            };

            if (start > 0)
            {
                int prevStart = Math.Max(0, start - size);
                int prevEnd = start - 1;
                links.Add($"<{baseUrl}?range={prevStart}-{prevEnd}>; rel=\"prev\"");
            }

            if (end + 1 < total)
            {
                int nextStart = end + 1;
                int nextEnd = Math.Min(nextStart + size - 1, total - 1);
                links.Add($"<{baseUrl}?range={nextStart}-{nextEnd}>; rel=\"next\"");
            }

            int lastStart = Math.Max(0, total - size);
            int lastEnd = total - 1;
            links.Add($"<{baseUrl}?range={lastStart}-{lastEnd}>; rel=\"last\"");

            response.Headers["Link"] = string.Join(", ", links);
        }
    }
}
