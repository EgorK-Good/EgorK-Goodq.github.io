using System.Security.Cryptography;
using System.Text;

namespace ClientBaseConnect.ApiMock;

internal sealed class CrmAuthStore
{
    private readonly Dictionary<string, int> _tokens = new(StringComparer.Ordinal);
    private int _nextUserId = 6;

    public List<UserAccount> Accounts { get; } =
    [
        new() { Id = 1, Name = "Администратор", Email = "admin@firm.local", Role = "admin", PasswordHash = Hash("admin123"), IsActive = true, CreatedAt = DateTime.UtcNow.AddMonths(-12) },
        new() { Id = 2, Name = "Анна Петрова", Email = "anna@firm.local", Role = "manager", PasswordHash = Hash("anna123"), IsActive = true, CreatedAt = DateTime.UtcNow.AddMonths(-6) },
        new() { Id = 3, Name = "Иван Сидоров", Email = "ivan@firm.local", Role = "accountant", PasswordHash = Hash("ivan123"), IsActive = true, CreatedAt = DateTime.UtcNow.AddMonths(-4) },
        new() { Id = 4, Name = "Мария Рабочая", Email = "maria@firm.local", Role = "worker", PasswordHash = Hash("maria123"), IsActive = true, CreatedAt = DateTime.UtcNow.AddMonths(-1) },
        new() { Id = 5, Name = "ООО Спектр (портал)", Email = "client@spectrum.local", Role = "client", ClientId = 1, PasswordHash = Hash("client123"), IsActive = true, CreatedAt = DateTime.UtcNow.AddDays(-10) },
    ];

    public AuthResultDto Login(string email, string password)
    {
        var user = FindByEmail(email);
        if (user is null || !user.IsActive)
            return AuthResultDto.Fail("Неверный email или пароль, либо учётная запись не активирована.");

        if (user.PasswordHash != Hash(password))
            return AuthResultDto.Fail("Неверный email или пароль.");

        var token = Guid.NewGuid().ToString("N");
        _tokens[token] = user.Id;
        return AuthResultDto.Ok(token, user.ToPublic());
    }

    public AuthResultDto Register(string name, string email, string password)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return AuthResultDto.Fail("Заполните все поля.");

        if (password.Length < 6)
            return AuthResultDto.Fail("Пароль должен быть не короче 6 символов.");

        if (FindByEmail(email) is not null)
            return AuthResultDto.Fail("Пользователь с таким email уже существует.");

        var user = new UserAccount
        {
            Id = _nextUserId++,
            Name = name.Trim(),
            Email = email.Trim().ToLowerInvariant(),
            Role = "worker",
            PasswordHash = Hash(password),
            IsActive = false,
            CreatedAt = DateTime.UtcNow,
        };
        Accounts.Add(user);

        return AuthResultDto.Pending(
            user.ToPublic(),
            "Заявка отправлена. Учётная запись сотрудника будет активирована администратором или руководителем.");
    }

    public UserAccount? ResolveUser(string? authHeader)
    {
        var token = ExtractBearer(authHeader);
        if (token is null || !_tokens.TryGetValue(token, out var userId))
            return null;

        return Accounts.FirstOrDefault(u => u.Id == userId && u.IsActive);
    }

    public bool IsAdminOrManager(UserAccount? user) =>
        user?.Role is "admin" or "manager";

    public UserDto CreateUser(UserAccount? actor, string name, string email, string password, string role, bool isActive)
    {
        if (actor is null)
            throw new UnauthorizedAccessException("Недостаточно прав.");

        if (role == "client")
        {
            if (actor.Role != "admin")
                throw new UnauthorizedAccessException("Учётки клиентов создаёт только администратор. Используйте назначение доступа к компании.");
        }
        else if (!IsAdminOrManager(actor))
        {
            throw new UnauthorizedAccessException("Недостаточно прав.");
        }

        if (!IsKnownRole(role))
            throw new ArgumentException("Неизвестная роль.");

        if (FindByEmail(email) is not null)
            throw new InvalidOperationException("Email уже занят.");

        var user = new UserAccount
        {
            Id = _nextUserId++,
            Name = name.Trim(),
            Email = email.Trim().ToLowerInvariant(),
            Role = role,
            PasswordHash = Hash(password),
            IsActive = isActive,
            CreatedAt = DateTime.UtcNow,
        };
        Accounts.Add(user);
        return user.ToPublic();
    }

    public UserDto CreateClientPortalUser(
        UserAccount? actor,
        CrmMockStore db,
        int clientId,
        string name,
        string email,
        string password)
    {
        if (actor is null || actor.Role != "admin")
            throw new UnauthorizedAccessException("Доступ в клиентский портал настраивает только администратор.");

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("Заполните имя, email и пароль.");

        if (password.Length < 6)
            throw new ArgumentException("Пароль должен быть не короче 6 символов.");

        var client = db.GetClientById(clientId) ?? throw new KeyNotFoundException("Клиент не найден.");

        if (client.PortalUserId is > 0)
            throw new InvalidOperationException("У этого клиента уже есть учётная запись портала.");

        if (FindByEmail(email) is not null)
            throw new InvalidOperationException("Email уже занят.");

        var user = new UserAccount
        {
            Id = _nextUserId++,
            Name = name.Trim(),
            Email = email.Trim().ToLowerInvariant(),
            Role = "client",
            ClientId = clientId,
            PasswordHash = Hash(password),
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };
        Accounts.Add(user);
        client.PortalUserId = user.Id;
        return user.ToPublic();
    }

    public UserDto UpdateUser(UserAccount? actor, int id, string? role, bool? isActive)
    {
        if (actor is null)
            throw new UnauthorizedAccessException("Недостаточно прав.");

        var user = Accounts.FirstOrDefault(u => u.Id == id)
            ?? throw new KeyNotFoundException("Пользователь не найден.");

        if (role is not null)
        {
            if (role == "client" && actor.Role != "admin")
                throw new UnauthorizedAccessException("Роль «клиент» назначает только администратор.");

            if (actor.Role != "admin" && !IsAdminOrManager(actor))
                throw new UnauthorizedAccessException("Недостаточно прав.");

            if (!IsKnownRole(role))
                throw new ArgumentException("Неизвестная роль.");
            user.Role = role;
        }
        else if (!IsAdminOrManager(actor))
        {
            throw new UnauthorizedAccessException("Недостаточно прав.");
        }

        if (isActive is not null)
            user.IsActive = isActive.Value;

        return user.ToPublic();
    }

    public List<UserDto> ListPublicUsers() =>
        Accounts.OrderBy(u => u.Name).Select(u => u.ToPublic()).ToList();

    public UserAccount? FindAccountById(int id) =>
        Accounts.FirstOrDefault(u => u.Id == id);

    public UserAccount? RequireClientPortalUser(string? authHeader)
    {
        var user = ResolveUser(authHeader);
        if (user is null || user.Role != "client" || user.ClientId is not > 0)
            return null;
        return user;
    }

    private UserAccount? FindByEmail(string email) =>
        Accounts.FirstOrDefault(u => u.Email.Equals(email.Trim(), StringComparison.OrdinalIgnoreCase));

    private static string? ExtractBearer(string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
            return null;

        const string prefix = "Bearer ";
        return header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? header[prefix.Length..].Trim()
            : null;
    }

    private static bool IsKnownRole(string role) =>
        role is "admin" or "manager" or "accountant" or "worker" or "client";

    public static string Hash(string password)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        return Convert.ToHexString(bytes);
    }
}

internal sealed class UserAccount
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Role { get; set; } = "";
    public int? ClientId { get; set; }
    public string PasswordHash { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }

    public UserDto ToPublic() => new()
    {
        Id = Id,
        Name = Name,
        Email = Email,
        Role = Role,
        ClientId = ClientId,
        IsActive = IsActive,
        CreatedAt = CreatedAt,
    };
}

internal sealed class AuthResultDto
{
    public bool Success { get; set; }
    public string? Token { get; set; }
    public UserDto? User { get; set; }
    public string? Error { get; set; }
    public string? Message { get; set; }

    public static AuthResultDto Ok(string token, UserDto user) =>
        new() { Success = true, Token = token, User = user };

    public static AuthResultDto Pending(UserDto user, string message) =>
        new() { Success = true, User = user, Message = message };

    public static AuthResultDto Fail(string error) =>
        new() { Success = false, Error = error };
}
