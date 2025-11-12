namespace DataTrustHub.API.User.DTOs
{
    public record UserDto
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }
}
