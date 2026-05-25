using System.Text.Json;

namespace ClientBaseConnect.ApiMock;

internal sealed class CrmMockStore
{
    public CrmAuthStore Auth { get; } = new();
    public CrmPortalStore Portal { get; } = new();

    private readonly object _lock = new();
    private int _nextClientId = 4;
    private int _nextTaskId = 4;
    private int _nextMessageId = 4;
    private int _nextCommentId = 2;

    public List<ClientDto> Clients { get; private set; } =
    [
        new() { Id = 1, Name = "ООО Спектр", Inn = "7701234567", Kpp = "770101001", ClientType = "legal", Status = "active", TaxSystem = "УСН 6%", AssignedWorkerId = 4, PortalUserId = 5 },
        new() { Id = 2, Name = "ИП Иванов", Inn = "500100123456", ClientType = "individual", Status = "active" },
        new() { Id = 3, Name = "ООО Альфа", Inn = "7702000000", Kpp = "770201001", ClientType = "legal", Status = "inactive" },
    ];

    public List<UserDto> Users => Auth.ListPublicUsers();

    public List<TaskDto> Tasks { get; } =
    [
        new() { Id = 1, Title = "Сдать отчётность", Status = "in_progress", Priority = "high", ClientId = 1, CreatedAt = DateTime.UtcNow.AddDays(-2) },
        new() { Id = 2, Title = "Сверка с 1С", Status = "new", Priority = "medium", ClientId = 1, CreatedAt = DateTime.UtcNow.AddDays(-1) },
        new() { Id = 3, Title = "Запрос документов", Status = "done", Priority = "low", ClientId = 2, CreatedAt = DateTime.UtcNow.AddDays(-5) },
        new() { Id = 4, Title = "Проверка декларации", Status = "review", Priority = "high", ClientId = 1, CreatedAt = DateTime.UtcNow.AddHours(-6) },
    ];

    public List<MessageDto> Messages { get; } =
    [
        new() { Id = 1, Text = "Добро пожаловать в Клиенты+!", SenderId = 1, CreatedAt = DateTime.UtcNow.AddHours(-3) },
        new() { Id = 2, Text = "Не забудьте импорт из 1С.", SenderId = 2, CreatedAt = DateTime.UtcNow.AddHours(-2) },
    ];

    public List<CommentDto> Comments { get; } =
    [
        new() { Id = 1, Text = "Клиент на УСН.", ClientId = 1, AuthorId = 2, CreatedAt = DateTime.UtcNow.AddDays(-1) },
    ];

    public ClientListDto ListClients(string? search, string? status, int page, int limit)
    {
        var q = Clients.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(c => c.Name.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                             (c.Inn?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        if (!string.IsNullOrWhiteSpace(status) && !status.Equals("all", StringComparison.OrdinalIgnoreCase))
            q = q.Where(c => c.Status.Equals(status, StringComparison.OrdinalIgnoreCase));

        var list = q.OrderBy(c => c.Name).ToList();
        return new ClientListDto { Total = list.Count, Clients = list.Skip((page - 1) * limit).Take(limit).ToList() };
    }

    public ImportResultDto Import1C(JsonElement data)
    {
        var result = new ImportResultDto();
        if (data.ValueKind != JsonValueKind.Array)
        {
            result.Errors.Add("Ожидается массив.");
            return result;
        }

        foreach (var item in data.EnumerateArray())
        {
            var externalId = GetStr(item, "externalId");
            var name = GetStr(item, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                result.Skipped++;
                continue;
            }

            var existing = Clients.FirstOrDefault(c =>
                !string.IsNullOrWhiteSpace(externalId) && c.ExternalId == externalId);

            if (existing is not null)
            {
                existing.Name = name;
                existing.Inn = GetStr(item, "inn") ?? existing.Inn;
                existing.Kpp = GetStr(item, "kpp") ?? existing.Kpp;
                existing.Source1c = true;
                result.Updated++;
            }
            else
            {
                Clients.Add(new ClientDto
                {
                    Id = _nextClientId++,
                    Name = name,
                    Inn = GetStr(item, "inn"),
                    Kpp = GetStr(item, "kpp"),
                    Ogrn = GetStr(item, "ogrn"),
                    LegalAddress = GetStr(item, "legalAddress"),
                    Email = GetStr(item, "email"),
                    Phone = GetStr(item, "phone"),
                    ContactPerson = GetStr(item, "contactPerson"),
                    TaxSystem = GetStr(item, "taxSystem"),
                    ClientType = GetStr(item, "clientType") ?? "legal",
                    Status = "active",
                    Source1c = true,
                    ExternalId = externalId,
                });
                result.Imported++;
            }
        }

        return result;
    }

    private static string? GetStr(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    // Thread-safe methods for adding entities
    public int GetNextClientId()
    {
        lock (_lock)
        {
            return _nextClientId++;
        }
    }

    public int GetNextTaskId()
    {
        lock (_lock)
        {
            return _nextTaskId++;
        }
    }

    public int GetNextMessageId()
    {
        lock (_lock)
        {
            return _nextMessageId++;
        }
    }

    public int GetNextCommentId()
    {
        lock (_lock)
        {
            return _nextCommentId++;
        }
    }

    public bool ClientInnExists(string? inn, int? excludeClientId = null)
    {
        if (string.IsNullOrWhiteSpace(inn)) return false;
        lock (_lock)
        {
            return Clients.Any(c => c.Inn == inn && c.Id != excludeClientId);
        }
    }

    public bool ClientExternalIdExists(string? externalId)
    {
        if (string.IsNullOrWhiteSpace(externalId)) return false;
        lock (_lock)
        {
            return Clients.Any(c => c.ExternalId == externalId);
        }
    }

    public ClientDto? GetClientById(int id)
    {
        lock (_lock)
        {
            return Clients.FirstOrDefault(c => c.Id == id);
        }
    }

    public ClientDto EnrichClient(ClientDto client)
    {
        if (client.AssignedWorkerId is > 0)
            client.AssignedWorker = Auth.FindAccountById(client.AssignedWorkerId.Value)?.ToPublic();

        if (client.PortalUserId is > 0)
        {
            var portal = Auth.FindAccountById(client.PortalUserId.Value);
            client.PortalEmail = portal?.Email;
        }

        return client;
    }

    public ClientDto AssignWorker(UserAccount? actor, int clientId, int workerId)
    {
        if (actor is null || actor.Role != "admin")
            throw new UnauthorizedAccessException("Назначать сотрудников может только администратор.");

        var client = GetClientById(clientId) ?? throw new KeyNotFoundException("Клиент не найден.");
        var worker = Auth.FindAccountById(workerId)
            ?? throw new ArgumentException("Сотрудник не найден.");

        if (worker.Role is not ("worker" or "accountant" or "manager"))
            throw new ArgumentException("На клиента можно назначить только сотрудника, бухгалтера или руководителя.");

        if (!worker.IsActive)
            throw new ArgumentException("Сотрудник не активирован.");

        client.AssignedWorkerId = workerId;
        return EnrichClient(client);
    }
}

internal sealed class ClientDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Inn { get; set; }
    public string? Kpp { get; set; }
    public string? Ogrn { get; set; }
    public string? LegalAddress { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? ContactPerson { get; set; }
    public string? TaxSystem { get; set; }
    public string ClientType { get; set; } = "legal";
    public string Status { get; set; } = "active";
    public bool? Source1c { get; set; }
    public string? ExternalId { get; set; }
    public int? AssignedWorkerId { get; set; }
    public UserDto? AssignedWorker { get; set; }
    public int? PortalUserId { get; set; }
    public string? PortalEmail { get; set; }
}

internal sealed class ClientListDto
{
    public int Total { get; set; }
    public List<ClientDto> Clients { get; set; } = [];
}

internal sealed class UserDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Role { get; set; } = "";
    public int? ClientId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
}

internal sealed class TaskDto
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string Status { get; set; } = "new";
    public string Priority { get; set; } = "medium";
    public DateTime CreatedAt { get; set; }
    public DateTime? DueDate { get; set; }
    public int? ClientId { get; set; }
    public ClientDto? Client { get; set; }
}

internal sealed class MessageDto
{
    public int Id { get; set; }
    public string Text { get; set; } = "";
    public int SenderId { get; set; }
    public DateTime CreatedAt { get; set; }
    public UserDto? Sender { get; set; }
}

internal sealed class CommentDto
{
    public int Id { get; set; }
    public string Text { get; set; } = "";
    public int ClientId { get; set; }
    public int AuthorId { get; set; }
    public DateTime CreatedAt { get; set; }
    public UserDto? Author { get; set; }
}

internal sealed class ImportResultDto
{
    public int Imported { get; set; }
    public int Updated { get; set; }
    public int Skipped { get; set; }
    public List<string> Errors { get; set; } = [];
}
