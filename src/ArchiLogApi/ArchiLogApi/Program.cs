// Importation des espaces de noms nécessaires
using ArchiLogApi.Data; // Contient la classe DbContext pour l'accès à la base de données
using Microsoft.EntityFrameworkCore; // Fournit les fonctionnalités Entity Framework Core
using Microsoft.AspNetCore.Mvc.Versioning; // Permet la gestion de version d’API
using Microsoft.AspNetCore.Mvc; // Fournit les fonctionnalités de base pour ASP.NET MVC
using Serilog; // Bibliothèque de journalisation structurée

// Création du constructeur d’application Web
var builder = WebApplication.CreateBuilder(args);

// Configuration de Serilog comme système de journalisation
builder.Host.UseSerilog((context, services, configuration) =>
{
    configuration
        .Enrich.FromLogContext() // Ajoute le contexte de journalisation (comme l'ID de requête)
        .Enrich.WithEnvironmentName() // Ajoute le nom de l’environnement (Développement, Production, etc.)
        .WriteTo.Console() // Écrit les logs dans la console
        .WriteTo.File("Logs/log-.txt", rollingInterval: RollingInterval.Day) // Enregistre les logs dans un fichier journal quotidien
        .ReadFrom.Configuration(context.Configuration) // Lit les configurations à partir du fichier appsettings.json
        .ReadFrom.Services(services); // Permet l’injection des services dans Serilog
});

// Enregistrement des services dans le conteneur d'injection de dépendances

builder.Services.AddControllers(); // Active les contrôleurs MVC pour traiter les requêtes HTTP

// Ajoute les services pour explorer les points de terminaison d'API et générer la documentation Swagger
builder.Services.AddEndpointsApiExplorer(); 
builder.Services.AddSwaggerGen(); // Génère la documentation OpenAPI (Swagger)

// Configuration de la base de données avec Entity Framework Core
builder.Services.AddDbContext<ArchiLogDbContext>(options =>
    options.UseLazyLoadingProxies() // Active le chargement paresseux pour les relations
           .UseSqlServer(builder.Configuration.GetConnectionString("archilog_db"))); // Utilise SQL Server avec la chaîne de connexion définie dans appsettings.json

// Configuration de la gestion de version de l'API
builder.Services.AddApiVersioning(options =>
{
    options.AssumeDefaultVersionWhenUnspecified = true; // Utilise une version par défaut si aucune version n’est spécifiée
    options.DefaultApiVersion = new ApiVersion(1, 0); // Définit la version par défaut à 1.0
    options.ReportApiVersions = true; // Informe le client des versions disponibles dans les en-têtes de réponse
    options.ApiVersionReader = new UrlSegmentApiVersionReader(); // Permet d’utiliser des segments d’URL comme /v1/
});

// Construction de l’application
var app = builder.Build();

// Configuration du pipeline HTTP

if (app.Environment.IsDevelopment())
{
    // Active Swagger uniquement en environnement de développement
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSerilogRequestLogging(); // Active la journalisation des requêtes HTTP avec Serilog

app.UseHttpsRedirection(); // Force les redirections HTTPS

app.UseAuthorization(); // Active la vérification des autorisations (sans authentification ici)

app.MapControllers(); // Mappe les routes vers les contrôleurs

app.Run(); // Démarre l'application

// Déclaration partielle de la classe Program pour la rendre accessible à WebApplicationFactory (utile pour les tests)
public partial class Program { }
