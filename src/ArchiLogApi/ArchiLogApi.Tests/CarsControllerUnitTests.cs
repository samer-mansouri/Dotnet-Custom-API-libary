using ArchiLogApi.Controllers;
using ArchiLogApi.Data;
using ArchiLogApi.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ArchiLogApi.Tests;

public class CarsControllerUnitTests
{
    private CarsController CreateControllerWithSeededData()
    {
        var options = new DbContextOptionsBuilder<ArchiLogDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString()) // pour éviter les conflits
            .Options;

        var context = new ArchiLogDbContext(options);

        context.Cars.AddRange(new List<Car>
        {
            new Car { Brand = "Toyota", Model = "Yaris", DailyRate = 40, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new Car { Brand = "Honda", Model = "Civic", DailyRate = 50, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }
        });
        context.SaveChanges();

        var controller = new CarsController(context);

        // 🧩 Correction ici pour éviter NullReference sur Response.Headers
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        return controller;
    }


    [Fact]
    public async Task GetAll_ShouldReturnAllCars()
    {
        // Arrange
        var controller = CreateControllerWithSeededData();

        // Act
        var result = await controller.GetAll();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var cars = Assert.IsAssignableFrom<IEnumerable<Car>>(okResult.Value);
        Assert.Equal(2, cars.Count());
    }

    [Fact]
    public async Task GetById_ShouldReturnCorrectCar()
    {
        // Arrange
        var controller = CreateControllerWithSeededData();

        // Act
        var result = await controller.GetById(1);

        // Assert
        var car = Assert.IsType<Car>(result.Value);
        Assert.Equal("Toyota", car.Brand);
    }
}