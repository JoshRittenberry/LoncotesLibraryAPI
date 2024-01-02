using LoncotesLibrary.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Json;
using System.Net.Security;
using System.Reflection.Metadata.Ecma335;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// allows passing datetimes without time zone data 
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

// allows our api endpoints to access the database through Entity Framework Core
builder.Services.AddNpgsql<LoncotesLibraryDbContext>(builder.Configuration["LoncotesLibraryDbConnectionString"]);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Get Endpoints
app.MapGet("/api/materials", (LoncotesLibraryDbContext db, int? materialTypeId, int? genreId) =>
{
    // https://localhost:7193/api/materials
    // https://localhost:7193/api/materials/?materialTypeId=1
    // https://localhost:7193/api/materials/?materialTypeId=1&genreId=6

    return db.Materials
    .Where(m =>
        m.OutOfCirculationSince == null &&
        (!materialTypeId.HasValue || m.MaterialTypeId == materialTypeId.Value) &&
        (!genreId.HasValue || m.GenreId == genreId.Value)
    )
    .Select(m => new MaterialDTO
    {
        Id = m.Id,
        MaterialName = m.MaterialName,
        MaterialTypeId = m.MaterialTypeId,
        MaterialType = new MaterialTypeDTO
        {
            Id = m.MaterialType.Id,
            Name = m.MaterialType.Name,
            CheckoutDays = m.MaterialType.CheckoutDays
        },
        GenreId = m.GenreId,
        Genre = new GenreDTO
        {
            Id = m.Genre.Id,
            Name = m.Genre.Name
        },
        OutOfCirculationSince = m.OutOfCirculationSince
    }).ToList();
});

app.MapGet("/api/materials/{id}", (LoncotesLibraryDbContext db, int id) =>
{
    // https://localhost:7193/api/materials/1

    var material = db.Materials
        .Include(m => m.MaterialType)
        .Include(m => m.Genre)
        .Include(m => m.Checkouts)
            .ThenInclude(c => c.Patron)
        .SingleOrDefault(m => m.Id == id);

    if (material == null)
    {
        return Results.NotFound(); // Or handle not found scenario appropriately
    }

    var materialCheckouts = material.Checkouts.Select(co =>
        new CheckoutDTO
        {
            Id = co.Id,
            MaterialId = co.MaterialId,
            PatronId = co.PatronId,
            Patron = new PatronDTO
            {
                Id = co.Patron.Id,
                FirstName = co.Patron.FirstName,
                LastName = co.Patron.LastName,
                Address = co.Patron.Address,
                Email = co.Patron.Email,
                IsActive = co.Patron.IsActive
            },
            CheckoutDate = co.CheckoutDate,
            ReturnDate = co.ReturnDate
        }).ToList();

    return Results.Ok(new MaterialDTO
    {
        Id = material.Id,
        MaterialName = material.MaterialName,
        MaterialTypeId = material.MaterialTypeId,
        GenreId = material.GenreId,
        OutOfCirculationSince = material.OutOfCirculationSince,
        Checkouts = materialCheckouts
    });
});

app.MapGet("/api/materialTypes/", (LoncotesLibraryDbContext db) =>
{
    // https://localhost:7193/api/materialTypes/

    return db.MaterialTypes
    .Select(mt => new MaterialTypeDTO
    {
        Id = mt.Id,
        Name = mt.Name,
        CheckoutDays = mt.CheckoutDays
    }).ToList();
});

app.MapGet("/api/genres", (LoncotesLibraryDbContext db) =>
{
    // https://localhost:7193/api/genres/

    return db.Genres
    .Select(g => new GenreDTO
    {
        Id = g.Id,
        Name = g.Name
    }).ToList();
});

app.MapGet("/api/patrons", (LoncotesLibraryDbContext db) =>
{
    var patrons = db.Patrons
        .Include(p => p.Checkouts)
            .ThenInclude(c => c.Material)
                .ThenInclude(m => m.MaterialType)
            .Include(p => p.Checkouts)
                .ThenInclude(c => c.Material)
                    .ThenInclude(m => m.Genre)
        .Select(p => new PatronDTO
        {
            Id = p.Id,
            FirstName = p.FirstName,
            LastName = p.LastName,
            Address = p.Address,
            Email = p.Email,
            IsActive = p.IsActive,
            Checkouts = p.Checkouts.Select(co => new CheckoutDTO
            {
                Id = co.Id,
                MaterialId = co.MaterialId,
                Material = new MaterialDTO
                {
                    Id = co.Material.Id,
                    MaterialName = co.Material.MaterialName,
                    MaterialTypeId = co.Material.MaterialTypeId,
                    MaterialType = new MaterialTypeDTO
                    {
                        Id = co.Material.MaterialType.Id,
                        Name = co.Material.MaterialType.Name,
                        CheckoutDays = co.Material.MaterialType.CheckoutDays
                    },
                    GenreId = co.Material.GenreId,
                    Genre = new GenreDTO
                    {
                        Id = co.Material.Genre.Id,
                        Name = co.Material.Genre.Name
                    },
                    OutOfCirculationSince = co.Material.OutOfCirculationSince,
                },
                PatronId = co.PatronId,
                CheckoutDate = co.CheckoutDate,
                ReturnDate = co.ReturnDate
            })
            .ToList()
        })
        .ToList();

    return patrons;
});

// Post Endpoints
app.MapPost("/api/materials", (LoncotesLibraryDbContext db, Material material) =>
{
    // https://localhost:7193/api/materials
    // {
    //     "materialName": "Test",
    //     "materialTypeId": 2,
    //     "genreId": 3
    // }

    try
    {
        db.Materials.Add(material);
        db.SaveChanges();

        // Load the related entities
        var materialWithDetails = db.Materials
            .Include(m => m.MaterialType)
            .Include(m => m.Genre)
            .FirstOrDefault(m => m.Id == material.Id);

        if (materialWithDetails == null)
        {
            return Results.NotFound();
        }

        return Results.Created($"/api/materials/{material.Id}", materialWithDetails);
    }
    catch (DbUpdateException)
    {
        return Results.BadRequest("Invalid data submitted");
    }
});

// Put Endpoints
app.MapPut("/api/materials/{id}", (LoncotesLibraryDbContext db, int id) =>
{
    // https://localhost:7193/api/materials/33

    Material materialToSoftDelete = db.Materials.SingleOrDefault(material => material.Id == id);
    if (materialToSoftDelete == null)
    {
        return Results.NotFound();
    }
    materialToSoftDelete.OutOfCirculationSince = DateTime.Now;

    db.SaveChanges();
    return Results.NoContent();
});

app.Run();