namespace DataTrustHub.API.User.DTOs;

public record UserResponseDto
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
}
