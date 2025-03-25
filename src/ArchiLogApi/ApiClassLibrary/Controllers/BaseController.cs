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
using ApiClassLibrary.Utils;


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
        
        

        // GET: api/[Models]
        [HttpGet]
        public virtual async Task<ActionResult<IEnumerable<TModel>>> GetAll(
            [FromQuery] string? range = null,
            [FromQuery] string? asc = null,
            [FromQuery] string? desc = null,
            [FromQuery] Dictionary<string, string>? filters = null,
            [FromQuery] string? fields = null
        )
        {
            var query = _context.Set<TModel>().Where(x => !x.Deleted);

            query = QueryHelper<TModel>.ApplyFilters(query, filters);
            query = QueryHelper<TModel>.ApplySorting(query, asc, desc);

            var total = await query.CountAsync();
            (int start, int end) = QueryHelper<TModel>.ParseRange(range, total);

            var data = await query.Skip(start).Take(end - start + 1).ToListAsync();

            QueryHelper<TModel>.AddPaginationHeaders(Response, start, data.Count, total);
            QueryHelper<TModel>.AddPaginationLinks(Response, Request, start, end, total);

            if (!string.IsNullOrWhiteSpace(fields))
            {
                return Ok(QueryHelper<TModel>.ShapeFields(data, fields));
            }

            return Ok(data);
        }

        [HttpGet("search")]
        public virtual async Task<ActionResult<IEnumerable<TModel>>> Search(
            [FromQuery] Dictionary<string, string>? filters = null,
            [FromQuery] string? sort = null)
        {
            IQueryable<TModel> query = _context.Set<TModel>().Where(x => !x.Deleted);

            if (filters != null)
            {
                foreach (var filter in filters)
                {
                    var propertyName = filter.Key.ToLower();
                    var value = filter.Value;

                    var prop = typeof(TModel).GetProperties()
                        .FirstOrDefault(p => p.Name.ToLower() == propertyName);

                    if (prop == null) continue;

                    var parameter = Expression.Parameter(typeof(TModel), "x");
                    var member = Expression.Property(parameter, prop.Name);

                    Expression? predicate = null;

                    // 🔍 Si la valeur contient des *, on fait une recherche floue (Contains)
                    if (value.StartsWith("*") || value.EndsWith("*"))
                    {
                        var cleaned = value.Replace("*", "");
                        var method = typeof(string).GetMethod("Contains", new[] { typeof(string) });
                        predicate = Expression.Call(member, method!, Expression.Constant(cleaned));
                    }
                    else if (value.Contains(','))
                    {
                        // 🎯 IN : plusieurs valeurs séparées par virgule
                        var values = value.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                          .Select(v => Convert.ChangeType(v.Trim(), prop.PropertyType))
                                          .ToList();

                        var equals = values.Select(v =>
                            (Expression)Expression.Equal(member, Expression.Constant(v))
                        );

                        predicate = equals.Aggregate(Expression.OrElse);
                    }
                    else
                    {
                        // 🟢 Égalité simple
                        var val = Convert.ChangeType(value, prop.PropertyType);
                        predicate = Expression.Equal(member, Expression.Constant(val));
                    }

                    if (predicate != null)
                    {
                        var lambda = Expression.Lambda<Func<TModel, bool>>(predicate, parameter);
                        query = query.Where(lambda);
                    }
                }
            }

            // 🔀 Tri combiné si "sort" est spécifié
            if (!string.IsNullOrWhiteSpace(sort))
            {
                IOrderedQueryable<TModel>? ordered = null;
                foreach (var field in sort.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    var prop = typeof(TModel).GetProperties()
                        .FirstOrDefault(p => p.Name.Equals(field.Trim(), StringComparison.OrdinalIgnoreCase));

                    if (prop == null) continue;

                    var param = Expression.Parameter(typeof(TModel), "x");
                    var property = Expression.Property(param, prop.Name);
                    var lambda = Expression.Lambda(property, param);

                    string method = ordered == null ? "OrderBy" : "ThenBy";

                    var result = Expression.Call(
                        typeof(Queryable),
                        method,
                        new Type[] { typeof(TModel), prop.PropertyType },
                        (ordered ?? query).Expression,
                        Expression.Quote(lambda)
                    );

                    ordered = (IOrderedQueryable<TModel>)query.Provider.CreateQuery(result);
                }

                if (ordered != null)
                {
                    query = ordered;
                }
            }

            return Ok(await query.ToListAsync());
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
