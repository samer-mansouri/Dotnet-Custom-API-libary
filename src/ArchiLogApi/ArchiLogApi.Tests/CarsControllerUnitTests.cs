// Importation des namespaces nécessaires
using ArchiLogApi.Controllers; // Contrôleur à tester
using ArchiLogApi.Data;        // Contexte de base de données EF Core
using ArchiLogApi.Models;      // Modèle Car
using Microsoft.AspNetCore.Http; // Pour manipuler HttpContext dans les tests
using Microsoft.AspNetCore.Mvc;  // Pour manipuler les résultats des contrôleurs
using Microsoft.EntityFrameworkCore; // Pour configurer le contexte EF InMemory

namespace ArchiLogApi.Tests;

// Classe de tests unitaires pour le contrôleur CarsController
public class CarsControllerUnitTests
{
    // Méthode utilitaire pour créer un contrôleur avec une base de données en mémoire préremplie
    private CarsController CreateControllerWithSeededData()
    {
        // Configuration d'une base de données InMemory unique (grâce à un GUID)
        var options = new DbContextOptionsBuilder<ArchiLogDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString()) // ⚠️ Permet d’éviter les conflits entre tests
            .Options;

        var context = new ArchiLogDbContext(options);

        // Ajout de données de test dans la base InMemory
        context.Cars.AddRange(new List<Car>
        {
            new Car { Brand = "Toyota", Model = "Yaris", DailyRate = 40, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new Car { Brand = "Honda", Model = "Civic", DailyRate = 50, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }
        });
        context.SaveChanges(); // Sauvegarde les données dans la base InMemory

        var controller = new CarsController(context); // Création du contrôleur avec le contexte simulé

        // Correction importante : on initialise HttpContext pour éviter des erreurs NullReference lors des tests
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        return controller; // Retourne le contrôleur prêt à être testé
    }

    // ✅ Test unitaire pour vérifier que GetAll retourne bien toutes les voitures
    [Fact]
    public async Task GetAll_ShouldReturnAllCars()
    {
        // Arrange : création du contrôleur avec des données de test
        var controller = CreateControllerWithSeededData();

        // Act : appel de la méthode GetAll
        var result = await controller.GetAll();

        // Assert : vérification que le résultat est bien un OkObjectResult contenant une liste de voitures
        var okResult = Assert.IsType<OkObjectResult>(result.Result); // Vérifie que le résultat est un 200 OK
        var cars = Assert.IsAssignableFrom<IEnumerable<Car>>(okResult.Value); // Vérifie que le contenu est bien une liste de voitures
        Assert.Equal(2, cars.Count()); // Vérifie qu’il y a bien 2 voitures
    }

    // ✅ Test unitaire pour vérifier que GetById retourne bien la voiture avec l'ID correspondant
    [Fact]
    public async Task GetById_ShouldReturnCorrectCar()
    {
        // Arrange : initialisation
        var controller = CreateControllerWithSeededData();

        // Act : récupération de la voiture avec l’ID 1
        var result = await controller.GetById(1);

        // Assert : vérifie que la voiture retournée est bien de la marque attendue
        var car = Assert.IsType<Car>(result.Value); // Vérifie que le retour est bien un objet Car
        Assert.Equal("Toyota", car.Brand); // Vérifie que la marque est bien "Toyota"
    }
}
