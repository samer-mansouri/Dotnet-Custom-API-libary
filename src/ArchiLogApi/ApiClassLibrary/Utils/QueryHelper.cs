using System.Linq.Expressions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace ApiClassLibrary.Utils
{
    /// <summary>
    /// Cette classe statique contient des fonctions réutilisables pour :
    /// - filtrer les résultats d'une requête (ex: chercher les voitures de marque "Toyota")
    /// - trier les résultats (ex: du moins cher au plus cher)
    /// - paginer (découper les résultats page par page)
    /// - renvoyer uniquement certains champs (ex: ne montrer que la marque)
    /// - ajouter les en-têtes HTTP de pagination dans la réponse
    ///
    /// Elle est générique : on peut l'utiliser avec n'importe quel modèle (Voiture, Client, etc.)
    /// </summary>
    public static class QueryHelper<TModel> where TModel : class
    {
        /// <summary>
        /// Fonction qui permet de filtrer dynamiquement les données selon un dictionnaire de filtres.
        /// Exemple : filters = { "Brand": "Toyota" } retournera seulement les objets où Brand == "Toyota"
        /// </summary>
        public static IQueryable<TModel> ApplyFilters(IQueryable<TModel> query, Dictionary<string, string>? filters)
        {
            // Si aucun filtre n’est fourni, on retourne la requête telle quelle
            if (filters == null) return query;

            // On parcourt chaque filtre { brand: "test"} , Car { brand, model, ... }
            foreach (var filter in filters)
            {
                // On vérifie que la propriété (ex: Brand) existe dans le modèle (Car, User, etc.)
                var property = typeof(TModel).GetProperties()
                    .FirstOrDefault(p => p.Name.Equals(filter.Key, StringComparison.OrdinalIgnoreCase));
                if (property == null) continue; //skip

                // Création d’un paramètre "x" dans une expression lambda (comme : x => x.Brand)
                var param = Expression.Parameter(typeof(TModel), "x");
                var member = Expression.PropertyOrField(param, property.Name);

                Expression? expression = null;

                // Si la valeur est une plage, ex: [10,100], on filtre entre ces deux valeurs
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
                // Si la valeur contient des virgules, on fait une comparaison multiple (IN)
                else if (filter.Value.Contains(','))
                {
                    var values = filter.Value.Split(',')
                        .Select(v => Convert.ChangeType(v, property.PropertyType))
                        .ToList();

                    var equals = values.Select(v => (Expression)Expression.Equal(member, Expression.Constant(v)));
                    expression = equals.Aggregate(Expression.OrElse); // x => x.Prop == A || x.Prop == B
                }
                // Cas classique : égalité simple
                else
                {
                    var value = Convert.ChangeType(filter.Value, property.PropertyType);
                    expression = Expression.Equal(member, Expression.Constant(value));
                }

                // On applique le filtre à la requête
                if (expression != null)
                {
                    var lambda = Expression.Lambda<Func<TModel, bool>>(expression, param);
                    query = query.Where(lambda);
                }
            }

            return query;
        }

        /// <summary>
        /// Fonction qui permet de trier dynamiquement selon des propriétés demandées
        /// Exemple : asc = "Brand", desc = "DailyRate"
        /// </summary>
        public static IQueryable<TModel> ApplySorting(IQueryable<TModel> query, string? asc, string? desc)
        {
            IOrderedQueryable<TModel>? ordered = null;

            // Tri croissant
            if (!string.IsNullOrWhiteSpace(asc))
            {
                foreach (var prop in asc.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    ordered = ApplyOrder(query, prop.Trim(), true, ordered != null, ordered);
                    if (ordered != null) query = ordered;
                }
            }

            // Tri décroissant
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

        /// <summary>
        /// Fonction qui construit dynamiquement une commande de tri (OrderBy, ThenBy, etc.)
        /// en utilisant les noms de propriétés passés en paramètre.
        /// </summary>
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

        /// <summary>
        /// Permet de lire et valider une chaîne de pagination (ex: "0-49")
        /// Elle retourne les valeurs de début et fin à utiliser dans Skip/Take
        /// </summary>
        /// 0-5 : string ==> 0: int, 5: int
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

        /// <summary>
        /// Permet de renvoyer uniquement certains champs (via le paramètre ?fields=... dans l'URL)
        /// Cela permet d'éviter d’envoyer tous les champs d'un modèle (optimisation)
        /// </summary>
        public static IEnumerable<object> ShapeFields(IEnumerable<TModel> data, string fields)
        {
            var selected = fields.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                 .Select(f => f.Trim().ToLower())
                                 .ToList();

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

        /// <summary>
        /// Ajoute les en-têtes HTTP standards de pagination :
        /// - Content-Range : ex. "0-49/152"
        /// - Accept-Ranges : indique que l’objet peut être paginé
        /// </summary>
        public static void AddPaginationHeaders(HttpResponse response, int start, int count, int total)
        {
            response.Headers["Content-Range"] = $"{start}-{start + count - 1}/{total}";
            response.Headers["Accept-Ranges"] = typeof(TModel).Name.ToLower();
        }

        /// <summary>
        /// Ajoute les liens de navigation (HATEOAS) dans l'en-tête "Link"
        /// Ces liens permettent au frontend de naviguer vers :
        /// - la première page
        /// - la précédente
        /// - la suivante
        /// - la dernière
        /// </summary>
        ///
        /// currentPage (firstPAGE, previousPage, nextPage, lastPage)
        ///
        /// 
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
