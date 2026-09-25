using Microsoft.Data.Sqlite;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

var connectionString = "Data Source=restaurant.db";

using (var connection = new SqliteConnection(connectionString))
{
    connection.Open();
    var cmd = connection.CreateCommand();
    cmd.CommandText = @"
        CREATE TABLE IF NOT EXISTS MenuItems (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Title TEXT NOT NULL,
            Category TEXT NOT NULL,
            Price REAL NOT NULL,
            Description TEXT NOT NULL,
            Icon TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS Orders (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            CustomerName TEXT NOT NULL,
            TableNumber INTEGER NOT NULL,
            WaitStaff TEXT NOT NULL,
            ItemsJson TEXT NOT NULL,
            TotalAmount REAL NOT NULL,
            Status TEXT NOT NULL,
            CreatedAt TEXT NOT NULL
        );
    ";
    cmd.ExecuteNonQuery();

    cmd.CommandText = "SELECT COUNT(*) FROM MenuItems;";
    long menuCount = (long)(cmd.ExecuteScalar() ?? 0);
    if (menuCount == 0)
    {
        cmd.CommandText = @"
            INSERT INTO MenuItems (Title, Category, Price, Description, Icon) VALUES
            ('Пицца Маргарита', 'Пицца', 490, 'Томатный соус, моцарелла, свежий базилик, оливковое масло', '/images/margarita.jpg'),
            ('Пицца Пепперони', 'Пицца', 620, 'Острая чоризо, моцарелла, фирменный томатный соус', '/images/pepperoni.jpg'),
            ('Паста Карбонара', 'Паста', 540, 'Спагетти, панчетта, сливочный сырный соус, пармезан', '/images/carbonara.jpg'),
            ('Феттуччине с грибами', 'Паста', 590, 'Паста ручной работы, белые лесные грибы, трюфельное масло', '/images/fettuccine.jpg'),
            ('Салат Цезарь', 'Салаты', 580, 'Куриное филе гриль, романо, соус цезарь, гренки, пармезан', '/images/caesar.jpg'),
            ('Салат Капрезе', 'Салаты', 460, 'Томаты, моцарелла буффало, базилик, соус песто', '/images/caprese.jpg'),
            ('Тирамису Классико', 'Десерты', 360, 'Савоярди, крем маскарпоне, крепкий эспрессо, какао', '/images/tiramisu.jpg'),
            ('Ягодный Лимонад', 'Напитки', 220, 'Освежающий авторский лимонад из малины и мяты (350 мл)', '/images/lemonade.jpg');
        ";
        cmd.ExecuteNonQuery();
    }
}

var startTime = DateTime.Now;
var staffList = new[] { "Алексей С.", "Мария В.", "Дмитрий К." };

app.MapGet("/healthz", () => Results.Ok(new
{
    status = "Healthy",
    uptimeSeconds = (int)(DateTime.Now - startTime).TotalSeconds,
    database = "SQLite (Connected)",
    timestamp = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss")
}));

app.MapGet("/api/menu", (string? category, ILogger<Program> logger) =>
{
    logger.LogInformation("Запрос меню. Фильтр: {Category}", category ?? "Все");
    var list = new List<MenuItem>();

    using var connection = new SqliteConnection(connectionString);
    connection.Open();
    var cmd = connection.CreateCommand();

    if (string.IsNullOrWhiteSpace(category) || category.Equals("Все", StringComparison.OrdinalIgnoreCase))
    {
        cmd.CommandText = "SELECT Id, Title, Category, Price, Description, Icon FROM MenuItems";
    }
    else
    {
        cmd.CommandText = "SELECT Id, Title, Category, Price, Description, Icon FROM MenuItems WHERE Category = $cat";
        cmd.Parameters.AddWithValue("$cat", category);
    }

    using var reader = cmd.ExecuteReader();
    while (reader.Read())
    {
        list.Add(new MenuItem(
            reader.GetInt32(0),
            reader.GetString(1),
            reader.GetString(2),
            Convert.ToDecimal(reader.GetDouble(3)),
            reader.GetString(4),
            reader.GetString(5)
        ));
    }
    return Results.Ok(list);
});

app.MapGet("/api/menu/categories", () =>
{
    var cats = new List<string> { "Все" };
    using var connection = new SqliteConnection(connectionString);
    connection.Open();
    var cmd = connection.CreateCommand();
    cmd.CommandText = "SELECT DISTINCT Category FROM MenuItems ORDER BY Category";

    using var reader = cmd.ExecuteReader();
    while (reader.Read())
    {
        cats.Add(reader.GetString(0));
    }
    return Results.Ok(cats);
});

app.MapPost("/api/orders", (CreateOrderDto request, ILogger<Program> logger) =>
{
    if (request.Items == null || !request.Items.Any())
    {
        return Results.BadRequest(new { error = "Корзина не может быть пустой" });
    }

    if (request.TableNumber < 1 || request.TableNumber > 50)
    {
        return Results.BadRequest(new { error = "Номер стола должен быть от 1 до 50" });
    }

    var cleanName = string.IsNullOrWhiteSpace(request.CustomerName) ? "Гость" : request.CustomerName.Trim();
    var waitStaff = staffList[request.TableNumber % staffList.Length];
    var totalSum = request.Items.Sum(i => i.Price * i.Quantity);
    var itemsJson = JsonSerializer.Serialize(request.Items);
    var now = DateTime.Now.ToString("dd.MM.yyyy HH:mm");

    long newId;
    using (var connection = new SqliteConnection(connectionString))
    {
        connection.Open();
        var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO Orders (CustomerName, TableNumber, WaitStaff, ItemsJson, TotalAmount, Status, CreatedAt)
            VALUES ($name, $table, $staff, $items, $total, 'Готовится', $date);
            SELECT last_insert_rowid();
        ";
        cmd.Parameters.AddWithValue("$name", cleanName);
        cmd.Parameters.AddWithValue("$table", request.TableNumber);
        cmd.Parameters.AddWithValue("$staff", waitStaff);
        cmd.Parameters.AddWithValue("$items", itemsJson);
        cmd.Parameters.AddWithValue("$total", totalSum);
        cmd.Parameters.AddWithValue("$date", now);

        newId = (long)(cmd.ExecuteScalar() ?? 0);
    }

    logger.LogInformation("Заказ #{Id} сохранен. Стол {Table}, официант {Staff}, сумма {Sum} руб.",
        newId, request.TableNumber, waitStaff, totalSum);

    return Results.Created($"/api/orders/{newId}", new
    {
        orderId = newId,
        customerName = cleanName,
        tableNumber = request.TableNumber,
        waitStaff,
        totalAmount = totalSum,
        status = "Готовится"
    });
});

app.MapGet("/api/admin/orders", () =>
{
    var list = new List<OrderAdminDto>();
    using var connection = new SqliteConnection(connectionString);
    connection.Open();
    var cmd = connection.CreateCommand();
    cmd.CommandText = "SELECT Id, CustomerName, TableNumber, WaitStaff, ItemsJson, TotalAmount, Status, CreatedAt FROM Orders ORDER BY Id DESC LIMIT 30";

    using var reader = cmd.ExecuteReader();
    var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

    while (reader.Read())
    {
        var rawItems = reader.IsDBNull(4) ? "[]" : reader.GetString(4);
        List<OrderItemDto> parsedItems;
        try
        {
            parsedItems = JsonSerializer.Deserialize<List<OrderItemDto>>(rawItems, jsonOptions) ?? new();
        }
        catch
        {
            parsedItems = new List<OrderItemDto>();
        }

        decimal totalAmount = Convert.ToDecimal(reader.GetDouble(5));

        list.Add(new OrderAdminDto(
            reader.GetInt32(0),
            reader.GetString(1),
            reader.GetInt32(2),
            reader.GetString(3),
            parsedItems,
            totalAmount,
            reader.GetString(6),
            reader.GetString(7)
        ));
    }
    return Results.Ok(list);
});

app.MapPut("/api/admin/orders/{id:int}/status", (int id, UpdateStatusDto dto, ILogger<Program> logger) =>
{
    using var connection = new SqliteConnection(connectionString);
    connection.Open();
    var cmd = connection.CreateCommand();
    cmd.CommandText = "UPDATE Orders SET Status = $status WHERE Id = $id";
    cmd.Parameters.AddWithValue("$status", dto.Status);
    cmd.Parameters.AddWithValue("$id", id);
    int affected = cmd.ExecuteNonQuery();

    if (affected == 0) return Results.NotFound();
    logger.LogInformation("Статус заказа #{Id} изменен на: {Status}", id, dto.Status);
    return Results.Ok(new { message = $"Статус заказа #{id} изменен на '{dto.Status}'" });
});

app.MapDelete("/api/admin/orders/{id:int}", (int id, ILogger<Program> logger) =>
{
    using var connection = new SqliteConnection(connectionString);
    connection.Open();
    var cmd = connection.CreateCommand();
    cmd.CommandText = "DELETE FROM Orders WHERE Id = $id";
    cmd.Parameters.AddWithValue("$id", id);
    int affected = cmd.ExecuteNonQuery();

    if (affected == 0) return Results.NotFound();
    logger.LogInformation("Заказ #{Id} удален из базы данных", id);
    return Results.Ok(new { message = $"Заказ #{id} удален" });
});

app.Run();

public record MenuItem(int Id, string Title, string Category, decimal Price, string Description, string Icon);
public record OrderItemDto(int MenuItemId, string Title, decimal Price, int Quantity);
public record CreateOrderDto(string? CustomerName, int TableNumber, List<OrderItemDto> Items);
public record UpdateStatusDto(string Status);
public record OrderAdminDto(int Id, string CustomerName, int TableNumber, string WaitStaff, List<OrderItemDto> Items, decimal TotalAmount, string Status, string CreatedAt);
