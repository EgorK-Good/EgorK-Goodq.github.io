namespace ClientBaseConnect.ApiMock;

internal sealed class CrmPortalStore
{
    private int _nextClientMessageId = 2;

    public List<ClientChatMessageDto> ClientMessages { get; } =
    [
        new()
        {
            Id = 1,
            ClientId = 1,
            SenderId = 4,
            Text = "Здравствуйте! Я ваш ответственный сотрудник. Пишите по любым вопросам.",
            CreatedAt = DateTime.UtcNow.AddHours(-5),
        },
    ];

    public List<ClientChatMessageDto> GetClientMessages(int clientId) =>
        ClientMessages
            .Where(m => m.ClientId == clientId)
            .OrderBy(m => m.CreatedAt)
            .ToList();

    public ClientChatMessageDto AddMessage(int clientId, int senderId, string text)
    {
        var msg = new ClientChatMessageDto
        {
            Id = _nextClientMessageId++,
            ClientId = clientId,
            SenderId = senderId,
            Text = text.Trim(),
            CreatedAt = DateTime.UtcNow,
        };
        ClientMessages.Add(msg);
        return msg;
    }
}

internal sealed class ClientChatMessageDto
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public int SenderId { get; set; }
    public string Text { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public UserDto? Sender { get; set; }
}

internal sealed class AssignWorkerDto
{
    public int WorkerId { get; set; }
}

internal sealed class CreatePortalUserDto
{
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
}

internal sealed class PortalProfileDto
{
    public UserDto User { get; set; } = new();
    public ClientDto Company { get; set; } = new();
    public UserDto? AssignedWorker { get; set; }
}
