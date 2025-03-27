using Xunit;
using Moq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System;
using ApiClassLibrary.Controllers;
using ApiClassLibrary.Models;
using ApiClassLibrary.Data;

namespace ApiClassLibrary.Tests
{
    //  Modèle factice basé sur BaseModel
    public class FakeModel : BaseModel
    {
        public string Label { get; set; } = "";
    }

    //  DbContext de test
    public class FakeDbContext : BaseDbContext
    {
        public FakeDbContext(DbContextOptions options) : base(options) { }

        public DbSet<FakeModel> FakeModels => Set<FakeModel>();
    }

    public class BaseControllerTests
    {
        private readonly FakeDbContext _context;
        private readonly TestFakeController _controller;

        public BaseControllerTests()
        {
            var options = new DbContextOptionsBuilder<FakeDbContext>()
                .UseInMemoryDatabase(databaseName: "TestDb_" + Guid.NewGuid()) // unique à chaque exécution
                .Options;

            _context = new FakeDbContext(options);

            // 💾 Seed de données
            _context.FakeModels.AddRange(new List<FakeModel>
            {
                new FakeModel { Label = "Item1", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Deleted = false },
                new FakeModel { Label = "Item2", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Deleted = false },
                new FakeModel { Label = "DeletedItem", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Deleted = true }
            });
            _context.SaveChanges();

            // 🧪 Création du contrôleur avec HttpContext simulé
            _controller = new TestFakeController(
                _context
            );
        }

        [Fact]
        public async Task GetAll_ShouldReturnOnlyNonDeletedItems()
        {
            // Act
            var result = await _controller.GetAll(null, null, null, null, null); // <== ici

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var models = Assert.IsAssignableFrom<IEnumerable<FakeModel>>(okResult.Value);
            Assert.Equal(3, models.Count());
        }

        // Contrôleur de test héritant du contrôleur abstrait
        private class TestFakeController : BaseController<FakeDbContext, FakeModel>
        {
            public TestFakeController(
                FakeDbContext context)
                : base(context)
            {
                //  Simule l’environnement HTTP pour les headers
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext()
                };
            }
        }
    }
}
