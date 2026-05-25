using System.Text.Json;
using ClientBaseConnect.ApiMock;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(builder.Configuration["Urls"] ?? "http://localhost:3000");
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? builder.Configuration["Cors:AllowedOrigins"]?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    ?? [
        "http://localhost:3001",
        "http://127.0.0.1:3001",
        "http://localhost:5173",
        "http://127.0.0.1:5173",
    ];

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy
        .AllowAnyHeader()
        .AllowAnyMethod()
        .SetIsOriginAllowed(origin =>
        {
            if (string.IsNullOrWhiteSpace(origin))
                return false;
            if (corsOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
                return true;
            return Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                && (uri.Host.Equals("github.io", StringComparison.OrdinalIgnoreCase)
                    || uri.Host.EndsWith(".github.io", StringComparison.OrdinalIgnoreCase));
        }));
});

var app = builder.Build();
app.UseCors();
var db = new CrmMockStore();
var jsonOpts = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    PropertyNameCaseInsensitive = true,
};

app.MapGet("/", () => Results.Ok(new { name = "Клиенты+ API", status = "running" }));

app.MapPost("/api/auth/login", async (HttpRequest req) =>
{
    var body = await JsonSerializer.DeserializeAsync<LoginDto>(req.Body, jsonOpts);
    if (body is null)
        return Results.BadRequest(new { error = "Некорректный запрос." });
    var result = db.Auth.Login(body.Email, body.Password);
    return result.Success
        ? Results.Ok(new { token = result.Token, user = result.User })
        : Results.Json(new { error = result.Error }, statusCode: StatusCodes.Status401Unauthorized);
});

app.MapPost("/api/auth/register", async (HttpRequest req) =>
{
    var body = await JsonSerializer.DeserializeAsync<RegisterDto>(req.Body, jsonOpts);
    if (body is null)
        return Results.BadRequest(new { error = "Некорректный запрос." });
    var result = db.Auth.Register(body.Name, body.Email, body.Password);
    return result.Success
        ? Results.Ok(new { message = result.Message, user = result.User })
        : Results.BadRequest(new { error = result.Error });
});

app.MapGet("/api/auth/me", (HttpRequest req) =>
{
    var user = db.Auth.ResolveUser(req.Headers.Authorization);
    return user is null
        ? Results.Unauthorized()
        : Results.Ok(user.ToPublic());
});

UserAccount? RequireUser(HttpRequest req) => db.Auth.ResolveUser(req.Headers.Authorization);

app.MapGet("/api/users", (HttpRequest req) =>
{
    if (RequireUser(req) is null)
        return Results.Unauthorized();
    return Results.Ok(db.Users);
});

app.MapPost("/api/users", async (HttpRequest req) =>
{
    try
    {
        var actor = RequireUser(req);
        var body = await JsonSerializer.DeserializeAsync<CreateUserDto>(req.Body, jsonOpts);
        if (body is null)
            return Results.BadRequest(new { error = "Некорректный запрос." });
        var user = db.Auth.CreateUser(actor, body.Name, body.Email, body.Password, body.Role, body.IsActive);
        return Results.Ok(user);
    }
    catch (UnauthorizedAccessException ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: 403);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPut("/api/users/{id:int}", async (int id, HttpRequest req) =>
{
    try
    {
        var actor = RequireUser(req);
        var body = await JsonSerializer.DeserializeAsync<UpdateUserDto>(req.Body, jsonOpts);
        if (body is null)
            return Results.BadRequest(new { error = "Некорректный запрос." });
        var user = db.Auth.UpdateUser(actor, id, body.Role, body.IsActive);
        return Results.Ok(user);
    }
    catch (UnauthorizedAccessException ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: 403);
    }
    catch (KeyNotFoundException)
    {
        return Results.NotFound();
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapGet("/api/clients", (HttpRequest req) =>
{
    var search = req.Query["search"].FirstOrDefault();
    var status = req.Query["status"].FirstOrDefault();
    _ = int.TryParse(req.Query["page"].FirstOrDefault(), out var page);
    _ = int.TryParse(req.Query["limit"].FirstOrDefault(), out var limit);
    if (page < 1) page = 1;
    if (limit < 1) limit = 50;
    if (limit > 100) limit = 100; // Ограничение на максимальный размер страницы
    return db.ListClients(search, status, page, limit);
});

app.MapGet("/api/clients/{id:int}", (int id) =>
    db.GetClientById(id) is { } c
        ? Results.Ok(db.EnrichClient(c))
        : Results.NotFound(new { error = "Клиент не найден" }));

app.MapPut("/api/clients/{id:int}/assign-worker", async (int id, HttpRequest req) =>
{
    try
    {
        var actor = RequireUser(req);
        var body = await JsonSerializer.DeserializeAsync<AssignWorkerDto>(req.Body, jsonOpts);
        if (body is null || body.WorkerId <= 0)
            return Results.BadRequest(new { error = "Укажите workerId." });
        return Results.Ok(db.AssignWorker(actor, id, body.WorkerId));
    }
    catch (UnauthorizedAccessException ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: 403);
    }
    catch (KeyNotFoundException)
    {
        return Results.NotFound(new { error = "Клиент не найден" });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/api/clients/{id:int}/portal-user", async (int id, HttpRequest req) =>
{
    try
    {
        var actor = RequireUser(req);
        var body = await JsonSerializer.DeserializeAsync<CreatePortalUserDto>(req.Body, jsonOpts);
        if (body is null)
            return Results.BadRequest(new { error = "Некорректный запрос." });
        var user = db.Auth.CreateClientPortalUser(actor, db, id, body.Name, body.Email, body.Password);
        return Results.Ok(new { user, client = db.EnrichClient(db.GetClientById(id)!) });
    }
    catch (UnauthorizedAccessException ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: 403);
    }
    catch (KeyNotFoundException)
    {
        return Results.NotFound(new { error = "Клиент не найден" });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapGet("/api/clients/{id:int}/chat", (int id, HttpRequest req) =>
{
    if (RequireUser(req) is null)
        return Results.Unauthorized();
    if (db.GetClientById(id) is null)
        return Results.NotFound(new { error = "Клиент не найден" });
    return Results.Ok(db.Portal.GetClientMessages(id)
        .Select(m =>
        {
            m.Sender = db.Users.FirstOrDefault(u => u.Id == m.SenderId);
            return m;
        }));
});

app.MapPost("/api/clients/{id:int}/chat", async (int id, HttpRequest req) =>
{
    var actor = RequireUser(req);
    if (actor is null)
        return Results.Unauthorized();
    if (db.GetClientById(id) is null)
        return Results.NotFound(new { error = "Клиент не найден" });

    using var doc = await JsonDocument.ParseAsync(req.Body);
    var text = doc.RootElement.TryGetProperty("text", out var t) ? t.GetString() : null;
    if (string.IsNullOrWhiteSpace(text))
        return Results.BadRequest(new { error = "Текст сообщения обязателен" });

    var msg = db.Portal.AddMessage(id, actor.Id, text);
    msg.Sender = actor.ToPublic();
    return Results.Created($"/api/clients/{id}/chat/{msg.Id}", msg);
});

app.MapGet("/api/portal/profile", (HttpRequest req) =>
{
    var user = db.Auth.RequireClientPortalUser(req.Headers.Authorization);
    if (user is null)
        return Results.Unauthorized();

    var company = db.GetClientById(user.ClientId!.Value);
    if (company is null)
        return Results.NotFound(new { error = "Компания не найдена" });

    db.EnrichClient(company);
    var profile = new PortalProfileDto
    {
        User = user.ToPublic(),
        Company = company,
        AssignedWorker = company.AssignedWorker,
    };
    return Results.Ok(profile);
});

app.MapGet("/api/portal/tasks", (HttpRequest req) =>
{
    var user = db.Auth.RequireClientPortalUser(req.Headers.Authorization);
    if (user is null)
        return Results.Unauthorized();

    var tasks = db.Tasks
        .Where(t => t.ClientId == user.ClientId)
        .OrderByDescending(t => t.CreatedAt)
        .Select(t =>
        {
            t.Client = db.GetClientById(t.ClientId ?? 0);
            return t;
        })
        .ToList();
    return Results.Ok(tasks);
});

app.MapGet("/api/portal/messages", (HttpRequest req) =>
{
    var user = db.Auth.RequireClientPortalUser(req.Headers.Authorization);
    if (user is null)
        return Results.Unauthorized();

    return Results.Ok(db.Portal.GetClientMessages(user.ClientId!.Value)
        .Select(m =>
        {
            m.Sender = db.Users.FirstOrDefault(u => u.Id == m.SenderId);
            return m;
        }));
});

app.MapPost("/api/portal/messages", async (HttpRequest req) =>
{
    var user = db.Auth.RequireClientPortalUser(req.Headers.Authorization);
    if (user is null)
        return Results.Unauthorized();

    using var doc = await JsonDocument.ParseAsync(req.Body);
    var text = doc.RootElement.TryGetProperty("text", out var t) ? t.GetString() : null;
    if (string.IsNullOrWhiteSpace(text))
        return Results.BadRequest(new { error = "Текст сообщения обязателен" });

    var msg = db.Portal.AddMessage(user.ClientId!.Value, user.Id, text);
    msg.Sender = user.ToPublic();
    return Results.Created($"/api/portal/messages/{msg.Id}", msg);
});

app.MapPost("/api/clients", async (HttpRequest req) =>
{
    var body = await JsonSerializer.DeserializeAsync<ClientDto>(req.Body, jsonOpts);
    if (body is null || string.IsNullOrWhiteSpace(body.Name))
        return Results.BadRequest(new { error = "Имя клиента обязательно" });

    // Валидация типа клиента
    if (body.ClientType != "legal" && body.ClientType != "individual")
        return Results.BadRequest(new { error = "Некорректный тип клиента. Допустимые значения: 'legal', 'individual'" });

    // Проверка уникальности ИНН
    if (!string.IsNullOrWhiteSpace(body.Inn) && db.ClientInnExists(body.Inn))
        return Results.Conflict(new { error = "Клиент с таким ИНН уже существует" });

    // Проверка уникальности ExternalId
    if (!string.IsNullOrWhiteSpace(body.ExternalId) && db.ClientExternalIdExists(body.ExternalId))
        return Results.Conflict(new { error = "Клиент с таким ExternalId уже существует" });

    body.Id = db.GetNextClientId();
    db.Clients.Add(body);
    return Results.Created($"/api/clients/{body.Id}", body);
});

app.MapPut("/api/clients/{id:int}", async (int id, HttpRequest req) =>
{
    var existing = db.GetClientById(id);
    if (existing is null) return Results.NotFound(new { error = "Клиент не найден" });

    var patch = await JsonSerializer.DeserializeAsync<ClientDto>(req.Body, jsonOpts);
    if (patch is null) return Results.BadRequest(new { error = "Тело запроса не может быть пустым" });

    // Проверка уникальности ИНН при обновлении
    if (!string.IsNullOrWhiteSpace(patch.Inn) && db.ClientInnExists(patch.Inn, id))
        return Results.Conflict(new { error = "Клиент с таким ИНН уже существует" });

    // Проверка уникальности ExternalId при обновлении
    if (!string.IsNullOrWhiteSpace(patch.ExternalId) && db.ClientExternalIdExists(patch.ExternalId))
        return Results.Conflict(new { error = "Клиент с таким ExternalId уже существует" });

    existing.Name = patch.Name ?? existing.Name;
    existing.Inn = patch.Inn ?? existing.Inn;
    existing.Kpp = patch.Kpp ?? existing.Kpp;
    existing.Ogrn = patch.Ogrn ?? existing.Ogrn;
    existing.LegalAddress = patch.LegalAddress ?? existing.LegalAddress;
    existing.Email = patch.Email ?? existing.Email;
    existing.Phone = patch.Phone ?? existing.Phone;
    existing.ContactPerson = patch.ContactPerson ?? existing.ContactPerson;
    existing.TaxSystem = patch.TaxSystem ?? existing.TaxSystem;
    existing.Status = patch.Status ?? existing.Status;
    existing.ClientType = patch.ClientType ?? existing.ClientType;
    return Results.Ok(existing);
});

app.MapPost("/api/clients/import/1c", async (HttpRequest req) =>
{
    using var doc = await JsonDocument.ParseAsync(req.Body);
    if (!doc.RootElement.TryGetProperty("data", out var data))
        return Results.BadRequest(new { error = "Поле data обязательно." });
    return Results.Ok(db.Import1C(data));
});

string[] ValidTaskStatuses() => ["new", "in_progress", "review", "done"];

app.MapGet("/api/tasks", (int? clientId) =>
{
    var tasks = db.Tasks.AsEnumerable();
    if (clientId is > 0)
        tasks = tasks.Where(t => t.ClientId == clientId);

    return tasks.Select(t =>
    {
        t.Client = db.Clients.FirstOrDefault(c => c.Id == t.ClientId);
        return t;
    }).ToList();
});

app.MapPost("/api/tasks", async (HttpRequest req) =>
{
    var body = await JsonSerializer.DeserializeAsync<TaskDto>(req.Body, jsonOpts);
    if (body is null || string.IsNullOrWhiteSpace(body.Title))
        return Results.BadRequest(new { error = "Название задачи обязательно" });

    var validStatuses = ValidTaskStatuses();
    if (!string.IsNullOrWhiteSpace(body.Status) && !validStatuses.Contains(body.Status))
        return Results.BadRequest(new { error = "Некорректный статус. Допустимые: new, in_progress, review, done" });

    // Валидация приоритета
    var validPriorities = new[] { "low", "medium", "high" };
    if (!string.IsNullOrWhiteSpace(body.Priority) && !validPriorities.Contains(body.Priority))
        return Results.BadRequest(new { error = "Некорректный приоритет. Допустимые значения: 'low', 'medium', 'high'" });

    // Проверка существования клиента
    if (body.ClientId is > 0 && db.GetClientById(body.ClientId.Value) is null)
        return Results.BadRequest(new { error = "Клиент с указанным ID не найден" });

    body.Id = db.GetNextTaskId();
    body.CreatedAt = DateTime.UtcNow;
    body.Status = body.Status ?? "new";
    body.Priority = body.Priority ?? "medium";
    db.Tasks.Add(body);
    body.Client = db.GetClientById(body.ClientId ?? 0);
    return Results.Created($"/api/tasks/{body.Id}", body);
});

app.MapPut("/api/tasks/{id:int}", async (int id, HttpRequest req) =>
{
    var task = db.Tasks.FirstOrDefault(t => t.Id == id);
    if (task is null) return Results.NotFound(new { error = "Задача не найдена" });

    using var doc = await JsonDocument.ParseAsync(req.Body);
    var root = doc.RootElement;

    // Валидация статуса
    if (root.TryGetProperty("status", out var st))
    {
        var status = st.GetString();
        var validStatuses = ValidTaskStatuses();
        if (!string.IsNullOrWhiteSpace(status) && !validStatuses.Contains(status))
            return Results.BadRequest(new { error = "Некорректный статус. Допустимые: new, in_progress, review, done" });
        task.Status = status ?? task.Status;
    }

    if (root.TryGetProperty("priority", out var p))
    {
        var priority = p.GetString();
        var validPriorities = new[] { "low", "medium", "high" };
        if (!string.IsNullOrWhiteSpace(priority) && !validPriorities.Contains(priority))
            return Results.BadRequest(new { error = "Некорректный приоритет. Допустимые значения: 'low', 'medium', 'high'" });
        task.Priority = priority ?? task.Priority;
    }

    if (root.TryGetProperty("title", out var t))
        task.Title = t.GetString() ?? task.Title;

    if (root.TryGetProperty("description", out var d))
        task.Description = d.GetString();

    if (root.TryGetProperty("dueDate", out var dd) && dd.ValueKind == JsonValueKind.String)
        task.DueDate = DateTime.Parse(dd.GetString()!);

    task.Client = db.GetClientById(task.ClientId ?? 0);
    return Results.Ok(task);
});

app.MapDelete("/api/tasks/{id:int}", (int id) =>
{
    var task = db.Tasks.FirstOrDefault(t => t.Id == id);
    if (task is null)
        return Results.NotFound(new { error = "Задача не найдена" });

    db.Tasks.Remove(task);
    return Results.NoContent();
});

app.MapGet("/api/messages", (int? limit) =>
{
    var take = limit is > 0 ? limit.Value : 100;
    return db.Messages
        .OrderByDescending(m => m.CreatedAt)
        .Take(take)
        .Select(m =>
        {
            m.Sender = db.Users.FirstOrDefault(u => u.Id == m.SenderId);
            return m;
        })
        .OrderBy(m => m.CreatedAt)
        .ToList();
});

app.MapPost("/api/messages", async (HttpRequest req) =>
{
    using var doc = await JsonDocument.ParseAsync(req.Body);
    var root = doc.RootElement;
    var text = root.TryGetProperty("text", out var t) ? t.GetString() : null;
    var senderId = root.TryGetProperty("senderId", out var s) && s.TryGetInt32(out var sid) ? sid : 1;
    
    if (string.IsNullOrWhiteSpace(text))
        return Results.BadRequest(new { error = "Текст сообщения обязателен" });

    // Проверка существования отправителя
    var sender = db.Users.FirstOrDefault(u => u.Id == senderId);
    if (sender is null)
        return Results.BadRequest(new { error = "Пользователь с указанным senderId не найден" });

    var msg = new MessageDto
    {
        Id = db.GetNextMessageId(),
        Text = text.Trim(),
        SenderId = senderId,
        CreatedAt = DateTime.UtcNow,
        Sender = sender,
    };
    db.Messages.Add(msg);
    return Results.Created($"/api/messages/{msg.Id}", msg);
});

app.MapGet("/api/comments", (int clientId) =>
    db.Comments
        .Where(c => c.ClientId == clientId)
        .Select(c =>
        {
            c.Author = db.Users.FirstOrDefault(u => u.Id == c.AuthorId);
            return c;
        })
        .ToList());

app.MapPost("/api/comments", async (HttpRequest req) =>
{
    using var doc = await JsonDocument.ParseAsync(req.Body);
    var root = doc.RootElement;
    var text = root.TryGetProperty("text", out var t) ? t.GetString() : null;
    var clientId = root.TryGetProperty("clientId", out var c) && c.TryGetInt32(out var cid) ? cid : 0;
    var authorId = root.TryGetProperty("authorId", out var a) && a.TryGetInt32(out var aid) ? aid : 1;
    
    if (string.IsNullOrWhiteSpace(text))
        return Results.BadRequest(new { error = "Текст комментария обязателен" });
    
    if (clientId <= 0)
        return Results.BadRequest(new { error = "clientId обязателен и должен быть больше 0" });

    // Проверка существования клиента
    if (db.GetClientById(clientId) is null)
        return Results.BadRequest(new { error = "Клиент с указанным clientId не найден" });

    // Проверка существования автора
    var author = db.Users.FirstOrDefault(u => u.Id == authorId);
    if (author is null)
        return Results.BadRequest(new { error = "Пользователь с указанным authorId не найден" });

    var comment = new CommentDto
    {
        Id = db.GetNextCommentId(),
        Text = text.Trim(),
        ClientId = clientId,
        AuthorId = authorId,
        CreatedAt = DateTime.UtcNow,
        Author = author,
    };
    db.Comments.Add(comment);
    return Results.Created($"/api/comments/{comment.Id}", comment);
});

app.Run();

internal sealed class LoginDto
{
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
}

internal sealed class RegisterDto
{
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
}

internal sealed class CreateUserDto
{
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
    public string Role { get; set; } = "worker";
    public bool IsActive { get; set; } = true;
}

internal sealed class UpdateUserDto
{
    public string? Role { get; set; }
    public bool? IsActive { get; set; }
}
