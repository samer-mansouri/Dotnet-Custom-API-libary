using ApiClassLibrary.Data;
using ApiClassLibrary.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ConstrainedExecution;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Versioning;
using System.Linq.Expressions;


namespace ApiClassLibrary.Controllers
{
    /// <summary>
    /// Class abstraite (qui doit être héritée) qui permet d'inclure les fonctions de CRUD pour les controlleurs enfants.
    /// </summary>
    /// <typeparam name="TContext">Type générique, qui sera le type du DbContext du controlleur enfant</typeparam>
    /// <typeparam name="TModel">Type générique, qui sera le type du BaseModel du controlleur enfant</typeparam>
    //[Route("api/[controller]")]
    
    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiController]
    [ApiVersion("1.0")]
    public abstract class BaseController<TContext, TModel> : ControllerBase where TContext : BaseDbContext where TModel : BaseModel
    {
        protected readonly TContext _context;

        public BaseController(TContext context)
        {
            _context = context;
        }
        
        private static IOrderedQueryable<TModel>? ApplyOrder(IQueryable<TModel> source, string property, bool ascending, bool thenBy, IOrderedQueryable<TModel>? current = null)
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


        // GET: api/[Models]
        [HttpGet]
        public virtual async Task<ActionResult<IEnumerable<TModel>>> GetAll(
            [FromQuery] string? range = null,
            [FromQuery] string? asc = null,
            [FromQuery] string? desc = null)
        {
            // On récupère les entités non supprimées
            IQueryable<TModel> query = _context.Set<TModel>().Where(x => !x.Deleted);

            //  APPLICATION DU TRI
            if (!string.IsNullOrEmpty(asc) || !string.IsNullOrEmpty(desc))
            {
                // Création d'une liste ordonnée dynamiquement
                IOrderedQueryable<TModel>? orderedQuery = null;

                // Tri croissant sur les champs spécifiés dans "asc"
                if (!string.IsNullOrWhiteSpace(asc))
                {
                    foreach (var prop in asc.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var property = prop.Trim();
                        orderedQuery = ApplyOrder(query, property, ascending: true, thenBy: orderedQuery != null, orderedQuery);
                        if (orderedQuery != null) query = orderedQuery;
                    }
                }

                // Tri décroissant sur les champs spécifiés dans "desc"
                if (!string.IsNullOrWhiteSpace(desc))
                {
                    foreach (var prop in desc.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var property = prop.Trim();
                        orderedQuery = ApplyOrder(query, property, ascending: false, thenBy: orderedQuery != null, orderedQuery);
                        if (orderedQuery != null) query = orderedQuery;
                    }
                }
            }

            //  CALCUL DE LA PAGINATION
            int total = await query.CountAsync();
            int start = 0;
            int end = Math.Min(49, total - 1);

            if (!string.IsNullOrEmpty(range))
            {
                var parts = range.Split('-');
                if (parts.Length == 2 &&
                    int.TryParse(parts[0], out int parsedStart) &&
                    int.TryParse(parts[1], out int parsedEnd) &&
                    parsedStart >= 0 &&
                    parsedEnd >= parsedStart)
                {
                    start = parsedStart;
                    end = Math.Min(parsedEnd, total - 1);
                }
            }

            var data = await query.Skip(start).Take(end - start + 1).ToListAsync();

            //  HEADERS DE PAGINATION
            Response.Headers.Add("Content-Range", $"{start}-{start + data.Count - 1}/{total}");
            Response.Headers.Add("Accept-Ranges", typeof(TModel).Name.ToLower());

            // 🔗 Liens HATEOAS pour la pagination
            var baseUrl = $"{Request.Scheme}://{Request.Host}{Request.Path}";
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

            Response.Headers.Add("Link", string.Join(", ", links));

            return Ok(data);
        }

        // GET: api/[Models]/5
        [HttpGet("{id}")]
        public virtual async Task<ActionResult<TModel>> GetById(int id)
        {
            var model = await _context.Set<TModel>().FindAsync(id);

            if (model == null)
            {
                return NotFound();
            }
            if (model.Deleted == true)
            {
                return NotFound();
            }
            return model;
        }

        // PUT: api/[Models]/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public virtual async Task<IActionResult> Put(int id, TModel model)
        {
            if (id != model.ID)
            {
                return BadRequest();
            }

            _context.Entry(model).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!ModelExists(id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return NoContent();
        }

        // POST: api/[Models]
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public virtual async Task<ActionResult<TModel>> Post(TModel model)
        {
            _context.Set<TModel>().Add(model);
            await _context.SaveChangesAsync();

            return CreatedAtAction("GetById", new { id = model.ID }, model);
        }

        // DELETE: api/[Models]/5
        [HttpDelete("{id}")]
        public virtual async Task<IActionResult> Delete(int id)
        {
            var car = await _context.Set<TModel>().FindAsync(id);
            if (car == null)
            {
                return NotFound();
            }
            if (car.Deleted == true)
            {
                return NotFound();
            }

            _context.Set<TModel>().Remove(car);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private bool ModelExists(int id)
        {
            return _context.Set<TModel>().Any(e => e.ID == id);
        }
    }
}
